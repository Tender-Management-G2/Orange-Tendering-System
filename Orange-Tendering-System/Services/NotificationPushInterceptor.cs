using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Orange_Tendering_System.Hubs;
using Orange_Tendering_System.Models.Entities;

namespace Orange_Tendering_System.Services;

// Every workflow that adds a Notification row (tender events, report approvals, clarifications, ...) saves through the
// normal DbContext. After a successful save this interceptor pushes the new rows to the owner's open browser tabs, so
// the bell updates in real time without touching any of those workflows. Failures never affect the save.
public sealed class NotificationPushInterceptor(IHubContext<NotificationHub> hub, ILogger<NotificationPushInterceptor> logger) : SaveChangesInterceptor
{
    private readonly ConditionalWeakTable<DbContext, List<Notification>> pending = new();

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Collect(eventData.Context);
        return ValueTask.FromResult(result);
    }
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Collect(eventData.Context);
        return result;
    }
    public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        await Push(eventData.Context);
        return result;
    }
    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        Push(eventData.Context).GetAwaiter().GetResult();
        return result;
    }
    public override void SaveChangesFailed(DbContextErrorEventData eventData) => Clear(eventData.Context);
    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default) { Clear(eventData.Context); return Task.CompletedTask; }

    private void Collect(DbContext? context)
    {
        if (context == null) return;
        var added = context.ChangeTracker.Entries<Notification>().Where(e => e.State == EntityState.Added).Select(e => e.Entity).ToList();
        if (added.Count > 0) pending.AddOrUpdate(context, added);
    }
    private void Clear(DbContext? context) { if (context != null) pending.Remove(context); }

    private async Task Push(DbContext? context)
    {
        if (context == null || !pending.TryGetValue(context, out var rows)) return;
        pending.Remove(context);
        foreach (var n in rows)
        {
            try { await hub.Clients.User(n.UserId).SendAsync("notification", NotificationDto.From(n)); }
            catch (Exception ex) { logger.LogWarning(ex, "Notification push failed for notification {Id}.", n.Id); }
        }
    }
}

public sealed record NotificationDto(int Id, string Title, string Message, string? Link, bool IsRead, DateTime CreatedAt)
{
    // Only same-site relative links are ever sent to the browser.
    public static string? SafeLink(string? link) => !string.IsNullOrWhiteSpace(link) && link.StartsWith('/') && !link.StartsWith("//") && !link.Contains((char)92) ? link : null;
    public static NotificationDto From(Notification n) => new(n.Id, n.Title, n.Message, SafeLink(n.Link), n.IsRead, DateTime.SpecifyKind(n.CreatedAt, DateTimeKind.Utc));
}
