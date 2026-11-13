using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.ViewModels;

namespace Orange_Tendering_System.Services;

// Owner: Saja. Access only the company linked to the authenticated Identity account.
public sealed class SupplierProfileService(ApplicationDbContext context)
{
    public async Task<SupplierProfileViewModel> GetProfileAsync(string userId)
    {
        var user = await context.Users.AsNoTracking()
            .Include(u => u.Supplier)
            .SingleOrDefaultAsync(u => u.Id == userId);

        if (user is null || !user.IsActive)
            return new() { AccessMessage = "Your account is unavailable or inactive. Please contact the administrator." };

        var supplier = user.Supplier;
        if (supplier is null)
            return new() { AccessMessage = "Your account is not linked to a company profile. Please contact the administrator." };

        if (supplier.Status != SupplierStatus.Approved)
            return new() { AccessMessage = "Your company must be approved by the administrator before accessing this page." };

        return new()
        {
            Contact = new() { Phone = supplier.Phone, Address = supplier.Address },
            CompanyName = supplier.CompanyName,
            RegistrationNumber = supplier.RegistrationNumber,
            TaxNumber = supplier.TaxNumber,
            Email = supplier.Email,
            Phone = supplier.Phone,
            Address = supplier.Address,
            Status = supplier.Status.ToString(),
            ReviewedAt = supplier.ReviewedAt
        };
    }

    public async Task<bool> UpdateContactAsync(string userId, SupplierContactInputModel input)
    {
        // 1. نجلب المستخدم مع شركته من قاعدة البيانات.
        var user = await context.Users.Include(u => u.Supplier)
            .SingleOrDefaultAsync(u => u.Id == userId);

        // 2. نتحقق أن الحساب فعال وله شركة معتمدة.
        if (user == null || user.IsActive == false)
        {
            return false;
        }

        var supplier = user.Supplier;
        if (supplier == null || supplier.Status != SupplierStatus.Approved)
        {
            return false;
        }

        var phone = input.Phone?.Trim();
        var address = input.Address?.Trim();
        if (supplier.Phone == phone && supplier.Address == address)
        {
            return true;
        }

        // 3. نحتفظ بالقيم القديمة للسجل، ثم نعدل الهاتف والعنوان فقط.
        var oldValues = JsonSerializer.Serialize(new { supplier.Phone, supplier.Address });
        supplier.Phone = phone;
        supplier.Address = address;

        AddContactAudit(userId, supplier, oldValues);

        // 4. هذا السطر يحفظ التعديل وسجل العملية فعليا في قاعدة البيانات.
        await context.SaveChangesAsync();
        return true;
    }

    // تجهيز سجل التغيير؛ يتم حفظه مع الشركة في SaveChangesAsync أعلاه.
    private void AddContactAudit(string userId, Supplier supplier, string oldValues)
    {
        context.AuditLogs.Add(new AuditLog
        {
            UserId = userId,
            Action = "UpdateContact",
            EntityName = nameof(Supplier),
            EntityId = supplier.Id.ToString(),
            OldValues = oldValues,
            NewValues = JsonSerializer.Serialize(new { supplier.Phone, supplier.Address }),
            CreatedAt = DateTime.UtcNow
        });

    }
}
