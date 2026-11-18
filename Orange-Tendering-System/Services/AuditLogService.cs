using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models.Entities;

namespace Orange_Tendering_System.Services;

/// <summary>
/// Records read-side audit events (views and downloads) in the existing AuditLogs table. Writes go through their own
/// DbContext scope so an audit row never flushes unrelated tracked changes of the request (for example the
/// supplier-filtered collections that TenderWorkflowService.Details prunes in memory). Workflow mutations keep
/// their own audit inside the workflow transaction (TenderWorkflowService.Change).
/// </summary>
public sealed class AuditLogService(IServiceScopeFactory scopes, TimeProvider clock)
{
    public const string TenderViewed = "TenderViewed";
    public const string DocumentDownloaded = "DocumentDownloaded";
    public const string DocumentViewed = "DocumentViewed";
    public const string AiAnalysisPdfDownloaded = "AiAnalysisPdfDownloaded";

    /// <summary>A refresh or re-open by the same person inside this window is the same visit and is not recorded again.</summary>
    public static readonly TimeSpan ViewWindow = TimeSpan.FromMinutes(15);

    public DateTime UtcNow => clock.GetUtcNow().UtcDateTime;

    /// <param name="oncePerWindow">Skip the row when the same user already has this action on this entity inside <see cref="ViewWindow"/>
    /// (used for inline previews that a browser may reload several times).</param>
    public async Task RecordAsync(string? userId, string action, string entityName, string entityId, object? newValues = null, object? oldValues = null, bool oncePerWindow = false)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var since = UtcNow - ViewWindow;
        if (oncePerWindow && await db.AuditLogs.AnyAsync(x => x.EntityName == entityName && x.EntityId == entityId && x.Action == action && x.UserId == userId && x.CreatedAt >= since)) return;
        db.AuditLogs.Add(new AuditLog
        {
            UserId = userId, Action = action, EntityName = entityName, EntityId = entityId, CreatedAt = UtcNow,
            OldValues = oldValues == null ? null : JsonSerializer.Serialize(oldValues),
            NewValues = newValues == null ? null : JsonSerializer.Serialize(newValues)
        });
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Records an authorized visit. The previous viewer (the latest visit by someone else) is read before the current
    /// visit is written and returned; repeated refreshes by the same person inside <see cref="ViewWindow"/> are not recorded.
    /// </summary>
    public async Task<AuditLog?> RecordViewAsync(string userId, string entityName, string entityId, object? context = null)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var views = db.AuditLogs.AsNoTracking().Where(x => x.EntityName == entityName && x.EntityId == entityId && x.Action == TenderViewed);
        var previous = await views.Where(x => x.UserId != userId).OrderByDescending(x => x.Id).FirstOrDefaultAsync();
        var since = UtcNow - ViewWindow;
        if (!await views.AnyAsync(x => x.UserId == userId && x.CreatedAt >= since))
        {
            db.AuditLogs.Add(new AuditLog { UserId = userId, Action = TenderViewed, EntityName = entityName, EntityId = entityId, CreatedAt = UtcNow,
                NewValues = context == null ? null : JsonSerializer.Serialize(context) });
            await db.SaveChangesAsync();
        }
        return previous;
    }
}
