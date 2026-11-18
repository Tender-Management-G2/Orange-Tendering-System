using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.ViewModels;

namespace Orange_Tendering_System.Services;

public partial class AdminService
{
    private static DateTimeOffset BusinessNow() => TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTimeOffset.UtcNow, "Asia/Amman");
    public async Task<AdminBusinessViewModel> GetBusinessOverviewAsync()
    {
        var now = BusinessNow();
        var records = await _context.AdminMaintenancePlans.AsNoTracking().ToListAsync();
        return new()
        {
            Today = DateOnly.FromDateTime(now.DateTime), LoadedAt = now,
            PendingSuppliers = await SubmittedSuppliers().CountAsync(x => x.Status == SupplierStatus.Pending),
            UnassignedRepresentatives = await _context.Users.CountAsync(u => u.IsActive && u.DepartmentId == null &&
                _context.UserRoles.Any(ur => ur.UserId == u.Id && _context.Roles.Any(r => r.Id == ur.RoleId && r.Name == SystemRoles.DepartmentRepresentative))),
            Maintenance = records.OrderBy(x => x.Status is AdminMaintenanceStatus.Completed or AdminMaintenanceStatus.Cancelled ? 1 : 0)
                .ThenBy(x => x.Status is AdminMaintenanceStatus.Completed or AdminMaintenanceStatus.Cancelled ? DateOnly.MaxValue.DayNumber - x.ScheduledDate.DayNumber : x.ScheduledDate.DayNumber)
                .Select(x => new AdminMaintenanceItem(x.Id, x.Title, CategoryLabel(x.Category), x.ScheduledDate, x.DurationMinutes, x.ResponsibleTeam, x.Status, x.CompletedDate, x.Notes)).ToList()
        };
    }
    public static string CategoryLabel(AdminMaintenanceCategory category) => category switch
    {
        AdminMaintenanceCategory.AnnualReview => "Annual review",
        AdminMaintenanceCategory.SecurityUpdate => "Security update",
        AdminMaintenanceCategory.BackupRecovery => "Backup & recovery",
        _ => "Platform care"
    };
    public async Task<AdminMaintenanceForm?> GetMaintenanceAsync(int id) => await _context.AdminMaintenancePlans.AsNoTracking()
        .Where(x => x.Id == id).Select(x => new AdminMaintenanceForm
        {
            Id = x.Id, Title = x.Title, Category = x.Category, ScheduledDate = x.ScheduledDate,
            DurationMinutes = x.DurationMinutes, ResponsibleTeam = x.ResponsibleTeam,
            Status = x.Status, CompletedDate = x.CompletedDate, Notes = x.Notes,
            Version = Convert.ToBase64String(x.RowVersion)
        }).SingleOrDefaultAsync();

    public async Task SaveMaintenanceAsync(AdminMaintenanceForm input, string actorId)
    {
        var errors = new List<ValidationResult>();
        if (!Validator.TryValidateObject(input, new ValidationContext(input), errors, true))
            throw new WorkflowException(string.Join(" ", errors.Select(x => x.ErrorMessage)));
        var today = DateOnly.FromDateTime(BusinessNow().DateTime);
        if (input.CompletedDate > today) throw new WorkflowException("Completion date cannot be in the future.");
        var actor = await _userManager.FindByIdAsync(actorId);
        if (actor is not { IsActive: true } || !await _userManager.IsInRoleAsync(actor, SystemRoles.Admin))
            throw new WorkflowException("An active Admin account is required.");
        var record = input.Id == 0 ? new AdminMaintenancePlan() : await _context.AdminMaintenancePlans.FindAsync(input.Id)
            ?? throw new WorkflowException("Maintenance record not found.");
        if (input.Id != 0)
        {
            if (string.IsNullOrEmpty(input.Version) || input.Version != Convert.ToBase64String(record.RowVersion))
                throw new WorkflowException("This maintenance record changed. Close this window and reopen it before saving.");
        }
        var before = input.Id == 0 ? null : JsonSerializer.Serialize(new { record.Title, record.Category, record.ScheduledDate, record.DurationMinutes, record.Status, record.ResponsibleTeam, record.CompletedDate, record.Notes });
        var now = DateTime.UtcNow;
        record.Title = input.Title.Trim(); record.Category = input.Category; record.ScheduledDate = input.ScheduledDate;
        record.DurationMinutes = input.DurationMinutes; record.ResponsibleTeam = input.ResponsibleTeam.Trim();
        record.Status = input.Status; record.CompletedDate = input.CompletedDate; record.Notes = input.Notes?.Trim(); record.UpdatedAtUtc = now;
        await using var tx = await _context.Database.BeginTransactionAsync();
        if (input.Id == 0) { record.CreatedAtUtc = now; _context.AdminMaintenancePlans.Add(record); }
        try
        {
            await _context.SaveChangesAsync();
            _context.AuditLogs.Add(new AuditLog { UserId = actorId, Action = input.Id == 0 ? "MaintenanceScheduled" : "MaintenanceUpdated",
                EntityName = "AdminMaintenancePlan", EntityId = record.Id.ToString(), OldValues = before,
                NewValues = JsonSerializer.Serialize(new { record.Title, record.Category, record.ScheduledDate, record.DurationMinutes, record.Status, record.ResponsibleTeam, record.CompletedDate, record.Notes }), CreatedAt = now });
            await _context.SaveChangesAsync(); await tx.CommitAsync();
        }
        catch (DbUpdateConcurrencyException) { throw new WorkflowException("Another Admin updated this record. Close and reopen it before saving."); }
    }
}
