using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models.Entities;

namespace Orange_Tendering_System.Services;

public sealed class ProcurementCategoryService(ApplicationDbContext db, TimeProvider clock)
{
    public sealed record Result(ProcurementCategory? Category, string? Error);

    public async Task<Result> SaveAsync(int id, string? name, string? description, int? sortOrder, bool isActive,
        string? actorId, CancellationToken ct)
    {
        name = name?.Trim();
        description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > 100 || (description?.Length ?? 0) > 300 || sortOrder is < 0 or > 10000)
            return new(null, "Invalid");
        if (await db.ProcurementCategories.AnyAsync(c => c.Name == name && c.Id != id, ct))
            return new(null, "Duplicate");

        var now = clock.GetUtcNow().UtcDateTime;
        var category = id == 0 ? new ProcurementCategory { CreatedAt = now }
            : await db.ProcurementCategories.SingleOrDefaultAsync(c => c.Id == id, ct);
        if (category == null) return new(null, "NotFound");
        var old = id == 0 ? null : Snapshot(category);
        var order = sortOrder ?? Math.Min(10000, (await db.ProcurementCategories.MaxAsync(c => (int?)c.SortOrder, ct) ?? 0) + 10);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        category.Name = name;
        category.Description = description;
        category.SortOrder = order;
        category.IsActive = isActive;
        if (id == 0) db.ProcurementCategories.Add(category);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException e) when (e.InnerException is SqlException { Number: 2601 or 2627 })
        {
            db.Entry(category).State = EntityState.Detached;
            return new(null, "Duplicate");
        }
        db.AuditLogs.Add(new AuditLog
        {
            UserId = actorId, EntityName = nameof(ProcurementCategory), EntityId = category.Id.ToString(),
            Action = id == 0 ? "CategoryCreated" : "CategoryUpdated", OldValues = old,
            NewValues = Snapshot(category), CreatedAt = now
        });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new(category, null);
    }

    private static string Snapshot(ProcurementCategory c) => JsonSerializer.Serialize(new { c.Name, c.Description, c.IsActive, c.SortOrder });
}
