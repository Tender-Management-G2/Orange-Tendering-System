using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;

namespace Orange_Tendering_System.Services;

public sealed record ChatReply(int ConversationId, string Title, long MessageId, string Reply, string Source = "ollama");
public sealed class ChatException(string message, int status = 400, int? conversationId = null) : Exception(message) { public int Status { get; } = status; public int? ConversationId { get; } = conversationId; }

// General help assistant shown as the floating chat popup. It talks to the one shared Ollama client (OllamaChatClient), stores the
// conversation in SQL Server per signed-in user, and is given NO tender, offer, price or supplier data. It is not AI Bid Analysis
// (officer-only, evidence based) and not the Clarification threads (human to human).
public sealed class AiChatService(ApplicationDbContext db, OllamaChatClient ollama, TimeProvider clock, ILogger<AiChatService> logger, IOptions<TenderApprovalPolicy> policy, IOptions<OllamaSettings> settings)
{
    public const int MaxMessageLength = 2000, ContextMessages = 12, MaxPerMinute = 8;

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> UserRequests = new();
    private static int EstimateTokens(string text) => (int)Math.Ceiling(text.Sum(c => c <= 127 ? 0.35 : 1.2));

    private static string UserId(ClaimsPrincipal user) => user.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new ChatException("Sign in again.", 401);

    public async Task<List<object>> ListAsync(ClaimsPrincipal user)
    {
        var id = UserId(user);
        return await db.Set<AiChatConversation>().AsNoTracking().Where(c => c.UserId == id).OrderByDescending(c => c.UpdatedAt).Take(30)
            .Select(c => (object)new { c.Id, c.Title, c.UpdatedAt }).ToListAsync();
    }

    public async Task<List<object>?> MessagesAsync(ClaimsPrincipal user, int conversationId)
    {
        var id = UserId(user);
        if (!await db.Set<AiChatConversation>().AnyAsync(c => c.Id == conversationId && c.UserId == id)) return null;
        return await db.Set<AiChatMessage>().AsNoTracking().Where(m => m.ConversationId == conversationId).OrderBy(m => m.Id)
            .Select(m => (object)new { m.Id, m.Role, m.Content, m.CreatedAt }).ToListAsync();
    }

    public async Task<bool> DeleteAsync(ClaimsPrincipal user, int conversationId)
    {
        var id = UserId(user);
        var gate = UserRequests.GetOrAdd(id, _ => new(1, 1));
        if (!await gate.WaitAsync(0)) throw new ChatException("Wait for your current AI request to finish.", 429);
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            var conversation = await db.Set<AiChatConversation>().SingleOrDefaultAsync(c => c.Id == conversationId && c.UserId == id);
            if (conversation == null) return false;
            await db.Set<AiChatMessage>().Where(m => m.ConversationId == conversationId).ExecuteDeleteAsync();
            db.Remove(conversation); await db.SaveChangesAsync();
            await transaction.CommitAsync();
            return true;
        }
        finally { gate.Release(); }
    }

    public async Task<ChatReply> SendAsync(ClaimsPrincipal user, int? conversationId, string? message, CancellationToken ct, string? page = null)
    {
        var gate = UserRequests.GetOrAdd(UserId(user), _ => new(1, 1));
        if (!await gate.WaitAsync(0, ct)) throw new ChatException("Wait for your current AI request to finish.", 429, conversationId);
        try { return await SendCore(user, conversationId, message, ct, page); }
        finally { gate.Release(); }
    }

    private async Task<ChatReply> SendCore(ClaimsPrincipal user, int? conversationId, string? message, CancellationToken ct, string? page)
    {
        var userId = UserId(user);
        message = message?.Trim();
        if (string.IsNullOrEmpty(message)) throw new ChatException("Type a message first.");
        if (message.Length > MaxMessageLength) throw new ChatException($"Messages are limited to {MaxMessageLength} characters.");
        var systemPrompt = OrangeHelpKnowledge.Prompt(user, policy.Value, page);
        var availableContext = Math.Clamp(settings.Value.ContextLength, 1024, 32768) - EstimateTokens(systemPrompt) - 512;
        if (EstimateTokens(message) + 12 > availableContext) throw new ChatException("Please shorten the question so the assistant can keep the system guidance in context.");
        if (!ollama.IsConfigured) throw new ChatException("The AI assistant is not configured. Contact the system administrator.", 503);
        var now = clock.GetUtcNow().UtcDateTime;
        if (await db.Set<AiChatMessage>().CountAsync(m => m.Conversation.UserId == userId && m.Role == "user" && m.CreatedAt > now.AddMinutes(-1), ct) >= MaxPerMinute)
            throw new ChatException("You are sending messages too quickly. Wait a moment and try again.", 429);

        AiChatConversation conversation;
        if (conversationId is { } cid)
            conversation = await db.Set<AiChatConversation>().SingleOrDefaultAsync(c => c.Id == cid && c.UserId == userId, ct) ?? throw new ChatException("Conversation not found.", 404);
        else
        {
            conversation = new AiChatConversation { UserId = userId, Title = message.Length <= 60 ? message : message[..57] + "...", CreatedAt = now, UpdatedAt = now };
            db.Add(conversation);
        }
        var userMessage = new AiChatMessage { Conversation = conversation, Role = "user", Content = message, CreatedAt = now };
        db.Add(userMessage); conversation.UpdatedAt = now;
        await db.SaveChangesAsync(ct);   // the question is kept even if the model is unavailable

        var history = await db.Set<AiChatMessage>().AsNoTracking().Where(m => m.ConversationId == conversation.Id).OrderByDescending(m => m.Id).Take(ContextMessages)
            .Select(m => new { m.Role, m.Content }).ToListAsync(ct);
        var selected = new List<(string Role, string Content)>();
        foreach (var entry in history)
        {
            var cost = EstimateTokens(entry.Content) + 12;
            if (cost > availableContext) break;
            selected.Add((entry.Role, entry.Content)); availableContext -= cost;
        }
        selected.Reverse();
        var messages = new List<object> { new { role = "system", content = systemPrompt } };
        messages.AddRange(selected.Select(m => (object)new { role = m.Role, content = m.Content }));

        string reply;
        try { reply = (await ollama.ChatAsync(messages, json: false, temperature: 0.1, ct)).Trim(); }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException or InvalidDataException or KeyNotFoundException)
        {
            logger.LogWarning(e, "AI chat request failed for conversation {ConversationId}", conversation.Id);
            throw new ChatException("The AI assistant is unavailable right now. Your question was saved; try again or contact the system administrator.", 502, conversation.Id);
        }
        if (reply.Length == 0) throw new ChatException("The AI assistant returned an empty answer. Try rephrasing your question.", 502, conversation.Id);
        var reviewed = OrangeHelpKnowledge.ReviewReply(user, page, message, reply);
        reply = reviewed.Content;
        var answer = new AiChatMessage { ConversationId = conversation.Id, Role = "assistant", Content = reply, CreatedAt = clock.GetUtcNow().UtcDateTime };
        db.Add(answer); conversation.UpdatedAt = answer.CreatedAt;
        await db.SaveChangesAsync(ct);
        return new ChatReply(conversation.Id, conversation.Title, answer.Id, reply, reviewed.Source);
    }
}
