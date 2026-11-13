using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Rendering;
using Orange_Tendering_System.Models;
using System.Text.Json;

namespace Orange_Tendering_System.Services
{
    public class AdminService
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;

        public AdminService(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            RoleManager<IdentityRole> roleManager)
        {
            _context = context;
            _userManager = userManager;
            _roleManager = roleManager;
        }
        // 1. ADMIN DASHBOARD

        public async Task<WorkspaceDashboardViewModel> GetDashboardAsync()
        {
            var metrics = new Dictionary<string, int>
            {
                ["total-users"] =
                    await _context.Users.CountAsync(), // بيحسب عدد السجلات الحقيقي بدون ما نحتاج نجلب كل البيانات

                ["total-departments"] =
                    await _context.Departments.CountAsync(),

                ["registered-suppliers"] =
                    await _context.Suppliers.CountAsync(),

                ["pending-suppliers"] =
                    await _context.Suppliers.CountAsync(
                        s => s.Status == SupplierStatus.Pending),
                // SupplierStatus.Pending بيعطينا عدد الموردين اللي لسه بانتظار مراجعة الـAdmin

                ["approved-suppliers"] =
                    await _context.Suppliers.CountAsync(
                        s => s.Status == SupplierStatus.Approved),

                ["purchase-requests"] =
                    await _context.PurchaseRequests.CountAsync(),

                ["tenders-total"] =
                    await _context.Tenders.CountAsync(),

                ["active-tenders"] =
                    await _context.Tenders.CountAsync(
                        t => t.Status == TenderStatus.Bidding ||
                             t.Status == TenderStatus.Evaluation ||
                             t.Status == TenderStatus.OfficerReview ||
                             t.Status == TenderStatus.ManagerReview ||
                             t.Status == TenderStatus.TopCommitteeReview)
                // استخدمنا القيم الموجودة فعلياً في Enums.cs تبع المشروع، حتى ما نخترع حالات غير موجودة
            };
            // Get latest 5 activities
            var auditLogs = await _context.AuditLogs
                .AsNoTracking() // AsNoTracking() لأننا بنقرأ فقط، وما بدنا نعدل السجلات
                .OrderByDescending(a => a.CreatedAt)  // بترتب العمليات من الأحدث للأقدم.
                .ThenByDescending(a => a.Id) // بترتب السجلات اللي عندها نفس الوقت
                .Take(5) // بتجيب آخر خمس عمليات.
                .Select(a => new
                {
                    a.Action,
                    a.EntityName,
                    a.EntityId,
                    a.CreatedAt
                })
                .ToListAsync(); // بتنّفذ الاستعلام وترجع النتائج.

            // وبعدها بنحول النتائج إلى DashboardActivity حتى تتناسب مع الـViewModel الموجود.

            // Convert AuditLogs into DashboardActivity
            var activities = auditLogs
                .Select(a => new DashboardActivity(
                    a.Action,
                    $"{a.EntityName} - #{a.EntityId}",
                    a.CreatedAt.ToString("yyyy-MM-dd HH:mm")
                ))
                .ToList();


            return new WorkspaceDashboardViewModel
            {
                DataConnected = true,
                Metrics = metrics,
                Activities = activities
            };

        }
        // 2. MANAGE DEPARTMENTS - INDEX

        public async Task<List<DepartmentListViewModel>> GetDepartmentsAsync()
        {
            // Get departments from database
            var departments = await _context.Departments
                .AsNoTracking()
                .OrderBy(d => d.Name)
                .Select(d => new DepartmentListViewModel
                {
                    Id = d.Id,
                    Name = d.Name,
                    Description = d.Description,
                    IsActive = d.IsActive,

                    // Count employees in each department
                    EmployeeCount = _context.Users
                        .Count(u => u.DepartmentId == d.Id),

                    // Count purchase requests for each department
                    PurchaseRequestCount = _context.PurchaseRequests
                        .Count(p => p.DepartmentId == d.Id)
                })
                .ToListAsync();

            return departments;
        }

        // CREATE DEPARTMENT
        public async Task<bool> CreateDepartmentAsync(
            CreateDepartmentViewModel model,
            string adminId)
        {
            var name = model.Name.Trim();

            // Check for duplicate department names
            var exists = await _context.Departments
                .AnyAsync(d => d.Name == name);

            if (exists)
            {
                return false;
            }

            // Create the department
            var department = new Department
            {
                Name = name,
                Description = model.Description?.Trim(),
                IsActive = model.IsActive
            };

            await using var transaction =
                await _context.Database.BeginTransactionAsync();

            try
            {
                _context.Departments.Add(department);
                await _context.SaveChangesAsync();

                // Record the operation in AuditLogs
                var auditLog = new AuditLog
                {
                    UserId = adminId,
                    Action = "Created",
                    EntityName = "Department",
                    EntityId = department.Id.ToString(),
                    NewValues = System.Text.Json.JsonSerializer.Serialize(
                        new
                        {
                            department.Name,
                            department.Description,
                            department.IsActive
                        }),
                    CreatedAt = DateTime.UtcNow
                };

                _context.AuditLogs.Add(auditLog);
                await _context.SaveChangesAsync();

                await transaction.CommitAsync();

                return true;
            }
            catch (DbUpdateException ex)
                when (ex.InnerException is SqlException sqlEx &&
                      (sqlEx.Number == 2601 || sqlEx.Number == 2627))
            {
                await transaction.RollbackAsync();
                return false;
            }
        }

        // GET DEPARTMENT FOR EDIT
        public async Task<EditDepartmentViewModel?> GetDepartmentForEditAsync(int id)
        {
            return await _context.Departments
                .AsNoTracking()
                .Where(d => d.Id == id)
                .Select(d => new EditDepartmentViewModel
                {
                    Id = d.Id,
                    Name = d.Name,
                    Description = d.Description,
                    IsActive = d.IsActive
                })
                .FirstOrDefaultAsync();
        }


        // UPDATE DEPARTMENT
        public async Task<string> UpdateDepartmentAsync(
            EditDepartmentViewModel model,
            string adminId)
        {
            var department = await _context.Departments
                .FirstOrDefaultAsync(d => d.Id == model.Id);

            if (department == null)
            {
                return "NotFound";
            }

            var newName = model.Name.Trim();

            if (newName.Length < 2 || newName.Length > 100)
            {
                return "InvalidName";
            }

            // Check duplicate name, excluding current department
            var exists = await _context.Departments
                .AnyAsync(d => d.Name == newName && d.Id != model.Id);

            if (exists)
            {
                return "Duplicate";
            }

            // Save old values for audit history
            var oldValues = System.Text.Json.JsonSerializer.Serialize(
                new
                {
                    department.Name,
                    department.Description,
                    department.IsActive
                });

            // Update department
            department.Name = newName;
            department.Description = model.Description?.Trim();
            department.IsActive = model.IsActive;

            var newValues = System.Text.Json.JsonSerializer.Serialize(
                new
                {
                    department.Name,
                    department.Description,
                    department.IsActive
                });

            // Save department and audit log in one transaction
            await using var transaction =
                await _context.Database.BeginTransactionAsync();

            try
            {
                await _context.SaveChangesAsync();

                var auditLog = new AuditLog
                {
                    UserId = adminId,
                    Action = "Updated",
                    EntityName = "Department",
                    EntityId = department.Id.ToString(),
                    OldValues = oldValues,  // لاحظي إنه استخدمنا OldValues وNewValues الموجودين أصلاً في AuditLog.cs، عشان نقدر نعرف شو تغير بالضبط.
                    NewValues = newValues,
                    CreatedAt = DateTime.UtcNow
                };

                _context.AuditLogs.Add(auditLog);
                await _context.SaveChangesAsync();

                await transaction.CommitAsync();

                return "Success";
            }
            catch (DbUpdateException ex)
                when (ex.InnerException is Microsoft.Data.SqlClient.SqlException sqlEx &&
                      (sqlEx.Number == 2601 || sqlEx.Number == 2627))
            {
                await transaction.RollbackAsync();
                return "Duplicate";
            }
        }

        // ADMIN - AUDIT LOGS WITH FILTERS & PAGINATION
        public async Task<AuditLogPageViewModel> GetAuditLogsAsync(
            string? search,
            string? actionFilter,
            DateTime? fromDate,
            DateTime? toDate,
            int page = 1)
        {
            const int pageSize = 20;

            search = search?.Trim();
            actionFilter = actionFilter?.Trim();

            // Start query without changing the database
            var query = _context.AuditLogs
                .AsNoTracking()
                .AsQueryable();

            // Search by action, entity, or username
            if (!string.IsNullOrWhiteSpace(search))
            {
                query = query.Where(a =>    // تطبيق شروط البحث والفلترة
                    a.Action.Contains(search) ||
                    a.EntityName.Contains(search) ||
                    (a.User != null &&
                     a.User.UserName != null &&
                     a.User.UserName.Contains(search)));
            }

            // Filter by action
            if (!string.IsNullOrWhiteSpace(actionFilter))
            {
                query = query.Where(a => a.Action == actionFilter);
            }

            // Filter by start date
            if (fromDate.HasValue)
            {
                var start = fromDate.Value.Date;
                query = query.Where(a => a.CreatedAt >= start);
            }

            // Filter by end date (inclusive)
            if (toDate.HasValue)
            {
                var end = toDate.Value.Date.AddDays(1);
                query = query.Where(a => a.CreatedAt < end);
            }

            // Total number of matching records
            var totalRecords = await query.CountAsync(); // 	حساب عدد السجلات المطابقة

            var totalPages = (int)Math.Ceiling(
                (double)totalRecords / pageSize);

            // Ensure valid page number
            page = Math.Max(1, page);

            if (totalPages > 0)
            {
                page = Math.Min(page, totalPages);
            }

            // Retrieve only the selected page
            var logs = await query
                .OrderByDescending(a => a.CreatedAt) // 	ترتيب السجلات بالأحدث
                .ThenByDescending(a => a.Id)
                .Skip((page - 1) * pageSize) // 	تجاوز سجلات الصفحات السابقة
                .Take(pageSize) // 	جلب 20 سجلاً للصفحة
                .Select(a => new AuditLogViewModel
                {
                    Id = a.Id,

                    UserName = a.User != null
                        ? (a.User.UserName ?? "Unknown")
                        : "System",

                    Action = a.Action,
                    EntityName = a.EntityName,
                    EntityId = a.EntityId,
                    OldValues = a.OldValues,
                    NewValues = a.NewValues,
                    CreatedAt = a.CreatedAt
                })
                .ToListAsync();

            return new AuditLogPageViewModel
            {
                Logs = logs,
                Search = search,
                Action = actionFilter,
                FromDate = fromDate,
                ToDate = toDate,
                CurrentPage = page,
                PageSize = pageSize,
                TotalRecords = totalRecords
            };
        }

        // ADMIN - MANAGE USERS - INDEX
        
        public async Task<List<UserListViewModel>> GetUsersAsync()
        {
            // Get user accounts with their department/company
            var users = await _context.Users
                .AsNoTracking()
                .OrderBy(u => u.FullName)
                .Select(u => new UserListViewModel
                {
                    Id = u.Id,
                    FullName = u.FullName,
                    Email = u.Email ?? "",
                    IsActive = u.IsActive,

                    DepartmentOrCompany = u.Department != null
                        ? u.Department.Name
                        : u.Supplier != null
                            ? u.Supplier.CompanyName
                            : "—"
                })
                .ToListAsync();

            // Get Identity role assignments
            var userRoles = await (
                from userRole in _context.UserRoles
                join role in _context.Roles
                    on userRole.RoleId equals role.Id
                select new
                {
                    userRole.UserId,
                    RoleName = role.Name
                }
            ).ToListAsync();

            // Group roles by user
            var rolesByUser = userRoles
                .GroupBy(r => r.UserId)
                .ToDictionary(
                    g => g.Key,
                    g => string.Join(", ",
                        g.Select(r => r.RoleName ?? "")
                         .Where(r => r != "")
                         .OrderBy(r => r))
                );

            // Assign role names to users
            foreach (var user in users)
            {
                user.Roles = rolesByUser.TryGetValue(
                    user.Id, out var roles)
                    ? roles
                    : "No Role";
            }

            return users;
        }

        public async Task FillCreateUserOptionsAsync(
            CreateUserViewModel model)
        {
            var departments = await _context.Departments
                .AsNoTracking()
                .Where(d => d.IsActive)
                .OrderBy(d => d.Name)
                .Select(d => new SelectListItem
                {
                    Value = d.Id.ToString(),
                    Text = d.Name
                })
                .ToListAsync();

            model.Departments = departments;

            // Internal roles only - suppliers register themselves
            var allowedRoles = SystemRoles.All
                .Where(r => r != SystemRoles.Supplier)
                .ToList();

            var existingRoles = await _roleManager.Roles
                .Where(r => r.Name != null &&
                            allowedRoles.Contains(r.Name))
                .OrderBy(r => r.Name)
                .Select(r => r.Name!)
                .ToListAsync();

            model.Roles = existingRoles
                .Select(r => new SelectListItem
                {
                    Value = r,
                    Text = r
                })
                .ToList();
        }
        // هاي الميثود بتجيب الأقسام النشطة فقط، وبتجيب الأدوار الموجودة فعلياً في AspNetRoles والمسموح بإنشاء حسابات موظفين لها.


        public async Task<IdentityResult> CreateUserAsync(
            CreateUserViewModel model,
            string adminId)
        {
            var allowedRoles = SystemRoles.All
                .Where(r => r != SystemRoles.Supplier);

            if (!allowedRoles.Contains(model.Role) ||
                !await _roleManager.RoleExistsAsync(model.Role))
            {
                return IdentityResult.Failed(new IdentityError
                {
                    Description = "The selected role is invalid."
                });
            }

            if (model.DepartmentId.HasValue)
            {
                var departmentExists = await _context.Departments
                    .AnyAsync(d => d.Id == model.DepartmentId.Value &&
                                   d.IsActive);

                if (!departmentExists)
                {
                    return IdentityResult.Failed(new IdentityError
                    {
                        Description = "Please select an active department."
                    });
                }
            }

            // Requesting Department accounts need a department
            if (model.Role == SystemRoles.RequestingDepartment &&
                !model.DepartmentId.HasValue)
            {
                return IdentityResult.Failed(new IdentityError
                {
                    Description = "Department is required for this role."
                });
            }

            var email = model.Email.Trim();

            var emailExists = await _context.Users
                .AnyAsync(u => u.NormalizedEmail == email.ToUpperInvariant());

            if (emailExists)
            {
                return IdentityResult.Failed(new IdentityError
                {
                    Description = "This email is already registered."
                });
            }

            var user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                FullName = model.FullName.Trim(),
                DepartmentId = model.DepartmentId,
                SupplierId = null,
                IsActive = model.IsActive,

                // Confirmation is handled by Authentication module
                EmailConfirmed = false
            };

            await using var transaction =
                await _context.Database.BeginTransactionAsync();

            // Create the account using Identity
            var result = await _userManager.CreateAsync(
                user, model.Password);

            if (!result.Succeeded)
            {
                await transaction.RollbackAsync();
                return result;
            }

            // Assign the initial role
            var roleResult = await _userManager.AddToRoleAsync(
                user, model.Role);

            if (!roleResult.Succeeded)
            {
                await transaction.RollbackAsync();
                return roleResult;
            }

            // Record non-sensitive information in AuditLogs
            _context.AuditLogs.Add(new AuditLog
            {
                UserId = adminId,
                Action = "Created",
                EntityName = "ApplicationUser",
                EntityId = user.Id,
                NewValues = System.Text.Json.JsonSerializer.Serialize(
                    new
                    {
                        user.FullName,
                        user.Email,
                        user.DepartmentId,
                        user.IsActive,
                        Role = model.Role
                    }),
                CreatedAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            return IdentityResult.Success;
        }

        // =============================================
        // ADMIN - USER DETAILS
        // =============================================
        public async Task<UserDetailsViewModel?> GetUserDetailsAsync(
            string id)
        {
            // Find user with related department and supplier
            var user = await _context.Users
                .AsNoTracking()
                .Include(u => u.Department)
                .Include(u => u.Supplier)
                .FirstOrDefaultAsync(u => u.Id == id);

            if (user == null)
            {
                return null;
            }

            // Get roles assigned to this user
            var roles = await _userManager.GetRolesAsync(user);

            // Get latest 5 audit entries for this account
            var recentActivities = await _context.AuditLogs
                .AsNoTracking()
                .Where(a => a.EntityId == id &&
                    (a.EntityName == "ApplicationUser" ||
                     a.EntityName == "AspNetUsers"))
                .OrderByDescending(a => a.CreatedAt)
                .ThenByDescending(a => a.Id)
                .Take(5)
                .Select(a => new UserAuditEntryViewModel
                {
                    CreatedAt = a.CreatedAt,
                    Action = a.Action,
                    EntityName = a.EntityName,
                    ChangedBy = a.User != null
                        ? (a.User.UserName ?? "Unknown")
                        : "System"
                })
                .ToListAsync();

            return new UserDetailsViewModel
            {
                Id = user.Id,
                FullName = user.FullName,
                Email = user.Email ?? "",
                Roles = roles.Count > 0
                    ? string.Join(", ", roles)
                    : "No Role",

                DepartmentName = user.Department?.Name ?? "—",
                SupplierName = user.Supplier?.CompanyName ?? "—",

                IsActive = user.IsActive,
                EmailConfirmed = user.EmailConfirmed,
                LockoutEnd = user.LockoutEnd,

                RecentActivities = recentActivities
            };
        }

        public async Task<EditUserViewModel?> GetUserForEditAsync(
            string id)
        {
            var user = await _userManager.FindByIdAsync(id);

            if (user == null)
                return null;

            var roles = await _userManager.GetRolesAsync(user);

            var departments = await _context.Departments
                .AsNoTracking()
                .Where(d => d.IsActive || d.Id == user.DepartmentId)
                .OrderBy(d => d.Name)
                .Select(d => new SelectListItem
                {
                    Value = d.Id.ToString(),
                    Text = d.Name
                })
                .ToListAsync();

            return new EditUserViewModel
            {
                Id = user.Id,
                FullName = user.FullName,
                Email = user.Email ?? "",
                DepartmentId = user.DepartmentId,
                IsActive = user.IsActive,
                CurrentRoles = string.Join(", ", roles),
                Departments = departments
            };
            // الميثود بتجيب معلومات المستخدم والـRoles الحالية والأقسام المتاحة. وبتسمح بظهور القسم القديم حتى لو أصبح غير نشط، عشان ما نفقد العلاقة الموجودة.
        }

        public async Task<string> UpdateUserAsync(
            EditUserViewModel model,
            string adminId)
        {
            if (string.IsNullOrWhiteSpace(model.Id))
                return "NotFound";

            var user = await _userManager.FindByIdAsync(model.Id);

            if (user == null)
                return "NotFound";

            // Supplier accounts are reviewed in Supplier Verification.
            if (user.SupplierId.HasValue)
                return "SupplierAccount";

            var newName = model.FullName.Trim();

            if (newName.Length < 2 || newName.Length > 150)
                return "InvalidName";

            if (model.DepartmentId.HasValue &&
                model.DepartmentId != user.DepartmentId)
            {
                var departmentExists = await _context.Departments
                    .AnyAsync(d =>
                        d.Id == model.DepartmentId.Value &&
                        d.IsActive);

                if (!departmentExists)
                    return "InvalidDepartment";
            }

            var roles = await _userManager.GetRolesAsync(user);

            if (roles.Contains(SystemRoles.RequestingDepartment) &&
                !model.DepartmentId.HasValue)
            {
                return "DepartmentRequired";
            }

            // Prevent an Admin from deactivating their own account.
            if (!model.IsActive && user.Id == adminId)
                return "SelfDeactivate";

            // Protect the last active Admin.
            if (user.IsActive && !model.IsActive &&
                roles.Contains(SystemRoles.Admin))
            {
                var adminRole = await _roleManager.FindByNameAsync(
                    SystemRoles.Admin);

                if (adminRole == null)
                    return "LastAdmin";

                var activeAdminCount = await (
                    from u in _context.Users
                    join ur in _context.UserRoles
                        on u.Id equals ur.UserId
                    where ur.RoleId == adminRole.Id && u.IsActive
                    select u.Id
                ).CountAsync();

                if (activeAdminCount <= 1)
                    return "LastAdmin";
            }

            // Save original values for Audit Log
            var oldValues = System.Text.Json.JsonSerializer.Serialize(
                new
                {
                    user.FullName,
                    user.DepartmentId,
                    user.IsActive
                });

            user.FullName = newName;
            user.DepartmentId = model.DepartmentId;
            user.IsActive = model.IsActive;

            var newValues = System.Text.Json.JsonSerializer.Serialize(
                new
                {
                    user.FullName,
                    user.DepartmentId,
                    user.IsActive
                });

            await using var transaction =
                await _context.Database.BeginTransactionAsync();

            var result = await _userManager.UpdateAsync(user);

            if (!result.Succeeded)
            {
                await transaction.RollbackAsync();
                return "UpdateFailed";
            }

            _context.AuditLogs.Add(new AuditLog
            {
                UserId = adminId,
                Action = "Updated",
                EntityName = "ApplicationUser",
                EntityId = user.Id,
                OldValues = oldValues,
                NewValues = newValues,
                CreatedAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            return "Success";
        }

        // =============================================
        // ADMIN - ROLES MANAGEMENT - INDEX
        // =============================================
        public async Task<List<RoleListViewModel>> GetRolesAsync()
        {
            // Get roles from Identity database
            var dbRoles = await _context.Roles
                .AsNoTracking()
                .ToListAsync();

            // Get role memberships and user names
            var memberships = await (
                from userRole in _context.UserRoles
                join user in _context.Users
                    on userRole.UserId equals user.Id
                join role in _context.Roles
                    on userRole.RoleId equals role.Id
                select new
                {
                    RoleName = role.Name,
                    UserName = user.FullName
                }
            ).AsNoTracking().ToListAsync();

            // Fixed system roles and descriptions
            var descriptions = new Dictionary<string, string>
            {
                [SystemRoles.Admin] =
                    "Manage users, departments and suppliers",

                [SystemRoles.RequestingDepartment] =
                    "Create and track purchase requests",

                [SystemRoles.SourcingOfficer] =
                    "Manage tenders and supplier invitations",

                [SystemRoles.Supplier] =
                    "Submit and track supplier bids",

                [SystemRoles.EvaluationCommittee] =
                    "Evaluate technical and financial offers",

                [SystemRoles.SourcingManager] =
                    "Review evaluation recommendations",

                [SystemRoles.TopCommittee] =
                    "Make final decisions on high-value tenders"
            };

            var result = new List<RoleListViewModel>();

            foreach (var roleName in SystemRoles.All)
            {
                var users = memberships
                    .Where(m => m.RoleName == roleName)
                    .Select(m => m.UserName)
                    .OrderBy(name => name)
                    .ToList();

                result.Add(new RoleListViewModel
                {
                    RoleName = roleName,
                    Purpose = descriptions[roleName],
                    UserCount = users.Count,
                    IsSeeded = dbRoles.Any(r => r.Name == roleName),
                    Users = users
                });
            }

            return result;
        }

        // =============================================
        // ADMIN - ROLE ASSIGNMENT OPTIONS
        
        public async Task<RoleMembershipViewModel>
            GetRoleMembershipOptionsAsync()
        {
            var model = new RoleMembershipViewModel();

            // Internal users only
            model.Users = await _context.Users
                .AsNoTracking()
                .Where(u => u.SupplierId == null)
                .OrderBy(u => u.FullName)
                .Select(u => new SelectListItem
                {
                    Value = u.Id,
                    Text = u.FullName + " (" + u.Email + ")"
                })
                .ToListAsync();

            // Only predefined internal roles
            var allowedRoles = SystemRoles.All
                .Where(r => r != SystemRoles.Supplier)
                .ToList();

            model.Roles = await _context.Roles
                .AsNoTracking()
                .Where(r => r.Name != null &&
                            allowedRoles.Contains(r.Name))
                .OrderBy(r => r.Name)
                .Select(r => new SelectListItem
                {
                    Value = r.Name!,
                    Text = r.Name!
                })
                .ToListAsync();

            return model;
        }

        // =============================================
        // ADMIN - ASSIGN ROLE
        // =============================================
        public async Task<string> AssignRoleAsync(
            string userId,
            string roleName,
            string adminId)
        {
            var user = await _userManager.FindByIdAsync(userId);

            if (user == null)
                return "UserNotFound";

            if (user.SupplierId != null)
                return "SupplierAccount";

            var allowedRoles = SystemRoles.All
                .Where(r => r != SystemRoles.Supplier);

            if (!allowedRoles.Contains(roleName) ||
                !await _roleManager.RoleExistsAsync(roleName))
                return "InvalidRole";

            if (await _userManager.IsInRoleAsync(user, roleName))
                return "AlreadyAssigned";

            if (roleName == SystemRoles.RequestingDepartment &&
                !user.DepartmentId.HasValue)
                return "DepartmentRequired";

            await using var transaction =
                await _context.Database.BeginTransactionAsync();

            var result = await _userManager.AddToRoleAsync(
                user, roleName);

            if (!result.Succeeded)
            {
                await transaction.RollbackAsync();
                return "Failed";
            }

            _context.AuditLogs.Add(new AuditLog
            {
                UserId = adminId,
                Action = "RoleAssigned",
                EntityName = "ApplicationUser",
                EntityId = user.Id,
                NewValues = System.Text.Json.JsonSerializer.Serialize(
                    new { Role = roleName }),
                CreatedAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();

            await _userManager.UpdateSecurityStampAsync(user);

            await transaction.CommitAsync();

            return "Success";
        }


        // =============================================
        // ADMIN - REMOVE ROLE
        // =============================================
        public async Task<string> RemoveRoleAsync(
            string userId,
            string roleName,
            string adminId)
        {
            var user = await _userManager.FindByIdAsync(userId);

            if (user == null)
                return "UserNotFound";

            if (user.SupplierId != null)
                return "SupplierAccount";

            var allowedRoles = SystemRoles.All
                .Where(r => r != SystemRoles.Supplier);

            if (!allowedRoles.Contains(roleName))
                return "InvalidRole";

            if (!await _userManager.IsInRoleAsync(user, roleName))
                return "NotAssigned";

            // Protect the current Admin account.
            if (roleName == SystemRoles.Admin &&
                user.Id == adminId)
                return "SelfAdminRemoval";

            // Protect the last active Admin.
            if (roleName == SystemRoles.Admin && user.IsActive)
            {
                var adminRole = await _roleManager
                    .FindByNameAsync(SystemRoles.Admin);

                if (adminRole == null)
                    return "LastAdmin";

                var activeAdmins = await (
                    from u in _context.Users
                    join ur in _context.UserRoles
                        on u.Id equals ur.UserId
                    where ur.RoleId == adminRole.Id && u.IsActive
                    select u.Id
                ).CountAsync();

                if (activeAdmins <= 1)
                    return "LastAdmin";
            }

            await using var transaction =
                await _context.Database.BeginTransactionAsync();

            var result = await _userManager.RemoveFromRoleAsync(
                user, roleName);

            if (!result.Succeeded)
            {
                await transaction.RollbackAsync();
                return "Failed";
            }

            _context.AuditLogs.Add(new AuditLog
            {
                UserId = adminId,
                Action = "RoleRemoved",
                EntityName = "ApplicationUser",
                EntityId = user.Id,
                OldValues = System.Text.Json.JsonSerializer.Serialize(
                    new { Role = roleName }),
                CreatedAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();

            // Invalidate existing authentication sessions
            // when the security stamp is revalidated.
            await _userManager.UpdateSecurityStampAsync(user);

            await transaction.CommitAsync();

            return "Success";
            // AddToRoleAsync() بتضيف المستخدم للدور داخل AspNetUserRoles.
            //RemoveFromRoleAsync() بتزيل ارتباط المستخدم بالدور، بدون حذف حسابه.
            // UpdateSecurityStampAsync() بتطلب إعادة التحقق من جلسات المستخدم الحالية وفق إعدادات Identity
            // لكنها لا تضمن إلغاء الصلاحيات فوراً في جميع الطلبات. لازم نتأكد لاحقاً من سياسة الجلسات مع عضو Authentication.
        }

        // =============================================
        // ADMIN - SUPPLIER MANAGEMENT - INDEX
        // =============================================
        public async Task<SupplierIndexViewModel> GetSuppliersAsync(
            string? search,
            string? statusFilter,
            int page = 1)
        {
            const int pageSize = 10;

            search = search?.Trim();
            statusFilter = statusFilter?.Trim();

            // Real supplier statistics
            var pendingCount = await _context.Suppliers
                .CountAsync(s => s.Status == SupplierStatus.Pending);

            var approvedCount = await _context.Suppliers
                .CountAsync(s => s.Status == SupplierStatus.Approved);

            var rejectedCount = await _context.Suppliers
                .CountAsync(s => s.Status == SupplierStatus.Rejected);

            var suspendedCount = await _context.Suppliers
                .CountAsync(s => s.Status == SupplierStatus.Suspended);

            var query = _context.Suppliers
                .AsNoTracking()
                .AsQueryable();

            // Search company or registration number
            if (!string.IsNullOrWhiteSpace(search))
            {
                query = query.Where(s =>
                    s.CompanyName.Contains(search) ||
                    s.RegistrationNumber.Contains(search));
            }

            // Filter by supplier status
            if (!string.IsNullOrWhiteSpace(statusFilter) &&
                statusFilter != "All")
            {
                if (Enum.TryParse<SupplierStatus>(
                    statusFilter, true, out var status) &&
                    Enum.IsDefined(status))
                {
                    query = query.Where(s => s.Status == status);
                }
                else
                {
                    statusFilter = "All";
                }
            }

            var totalRecords = await query.CountAsync();

            var totalPages = (int)Math.Ceiling(
                (double)totalRecords / pageSize);

            page = Math.Max(1, page);

            if (totalPages > 0)
                page = Math.Min(page, totalPages);

            var suppliers = await query
                .OrderByDescending(s => s.CreatedAt)
                .ThenByDescending(s => s.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(s => new SupplierListViewModel
                {
                    Id = s.Id,
                    CompanyName = s.CompanyName,
                    RegistrationNumber = s.RegistrationNumber,
                    Email = s.Email,
                    Status = s.Status.ToString(),
                    CreatedAt = s.CreatedAt,

                    ReviewedBy = s.ReviewedByUser != null
                        ? s.ReviewedByUser.FullName
                        : "",

                    DocumentCount = s.Documents.Count()
                })
                .ToListAsync();

            return new SupplierIndexViewModel
            {
                Suppliers = suppliers,
                Search = search,
                StatusFilter = statusFilter,
                PendingCount = pendingCount,
                ApprovedCount = approvedCount,
                RejectedCount = rejectedCount,
                SuspendedCount = suspendedCount,
                CurrentPage = page,
                TotalRecords = totalRecords,
                PageSize = pageSize
            };
        }

        // =============================================
        // ADMIN - SUPPLIER DETAILS
        // =============================================
        public async Task<SupplierDetailsViewModel?>
            GetSupplierDetailsAsync(int id)
        {
            // 1. Get supplier company information
            var supplier = await _context.Suppliers
                .AsNoTracking()
                .Where(s => s.Id == id)
                .Select(s => new SupplierDetailsViewModel
                {
                    Id = s.Id,
                    CompanyName = s.CompanyName,
                    RegistrationNumber = s.RegistrationNumber,
                    TaxNumber = s.TaxNumber,
                    Email = s.Email,
                    Phone = s.Phone,
                    Address = s.Address,

                    Status = s.Status.ToString(),
                    ReviewNotes = s.ReviewNotes,
                    ReviewedBy = s.ReviewedByUser != null
                        ? s.ReviewedByUser.FullName
                        : null,
                    ReviewedAt = s.ReviewedAt,
                    CreatedAt = s.CreatedAt
                })
                .FirstOrDefaultAsync();

            if (supplier == null)
            {
                return null;
            }

            // 2. Get login accounts linked to supplier
            supplier.Accounts = await _context.Users
                .AsNoTracking()
                .Where(u => u.SupplierId == id)
                .OrderBy(u => u.FullName)
                .Select(u => new SupplierAccountViewModel
                {
                    FullName = u.FullName,
                    Email = u.Email ?? "",
                    IsActive = u.IsActive
                })
                .ToListAsync();

            // 3. Get supplier legal documents
            var documents = await _context.Documents
                .AsNoTracking()
                .Where(d => d.SupplierId == id)
                .OrderByDescending(d => d.UploadedAt)
                .Select(d => new
                {
                    d.Id,
                    d.FileName,
                    d.DocumentType,
                    d.ExpiresAt,
                    d.VerificationStatus,
                    d.VerificationNotes
                })
                .ToListAsync();

            supplier.Documents = documents
                .Select(d => new SupplierDocumentViewModel
                {
                    Id = d.Id,
                    FileName = d.FileName,
                    DocumentType = d.DocumentType,
                    ExpiresAt = d.ExpiresAt,
                    VerificationStatus =
                        d.VerificationStatus?.ToString()
                        ?? "Not Reviewed",
                    VerificationNotes = d.VerificationNotes
                })
                .ToList();

            // 4. Get tender invitation history
            var invitations = await _context.TenderInvitations
                .AsNoTracking()
                .Where(i => i.SupplierId == id)
                .OrderByDescending(i => i.InvitedAt)
                .Select(i => new
                {
                    TenderId = i.TenderRound.TenderId,
                    TenderTitle = i.TenderRound.Tender.Title,
                    i.TenderRound.RoundNumber,
                    i.InvitedAt,
                    i.Status,
                    OfferSubmitted = i.Bids.Any(
                        b => b.SubmittedAt != null)
                })
                .ToListAsync();

            supplier.Invitations = invitations
                .Select(i => new SupplierInvitationViewModel
                {
                    TenderId = i.TenderId,
                    TenderTitle = i.TenderTitle,
                    RoundNumber = i.RoundNumber,
                    InvitedAt = i.InvitedAt,
                    Status = i.Status.ToString(),
                    OfferSubmitted = i.OfferSubmitted
                })
                .ToList();

            return supplier;
        }
        // أولاً: مراجعة وثيقة المورد
        public async Task<string> ReviewSupplierDocumentAsync(
            SupplierDocumentReviewViewModel model,
            string adminId)
        {
            if (!Enum.IsDefined(model.Status))
                return "InvalidStatus";

            if (model.Status == DocumentVerificationStatus.Rejected &&
                string.IsNullOrWhiteSpace(model.Notes))
                return "ReasonRequired";

            var document = await _context.Documents
                .FirstOrDefaultAsync(d =>
                    d.Id == model.DocumentId &&
                    d.SupplierId == model.SupplierId);

            if (document == null)
                return "NotFound";

            var supplier = await _context.Suppliers
                .FirstOrDefaultAsync(s => s.Id == model.SupplierId);

            if (supplier == null)
                return "NotFound";

            // Avoid invalidating an approved supplier's
            // documents without reviewing its approval.
            if (supplier.Status == SupplierStatus.Approved)
                return "SupplierApproved";

            if (model.Status == DocumentVerificationStatus.Verified &&
                document.ExpiresAt.HasValue &&
                document.ExpiresAt.Value.Date < DateTime.UtcNow.Date)
                return "DocumentExpired";

            var oldValues = JsonSerializer.Serialize(new
            {
                document.VerificationStatus,
                document.VerificationNotes
            });

            document.VerificationStatus = model.Status;
            document.VerificationNotes = model.Notes?.Trim();
            document.VerifiedByUserId = adminId;
            document.VerifiedAt = DateTime.UtcNow;

            var newValues = JsonSerializer.Serialize(new
            {
                document.VerificationStatus,
                document.VerificationNotes
            });

            _context.AuditLogs.Add(new AuditLog
            {
                UserId = adminId,
                Action = "DocumentReviewed",
                EntityName = "Document",
                EntityId = document.Id.ToString(),
                OldValues = oldValues,
                NewValues = newValues,
                CreatedAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();

            return "Success";
            // هاي الميثود بتحفظ حالة الوثيقة مع اسم المسؤول عن المراجعة وتاريخها، وبتمنع اعتماد الوثيقة المنتهية الصلاحية.
        }
        // ثانياً: اتخاذ قرار بشأن المورد

        public async Task<string> ReviewSupplierAsync(
            SupplierReviewDecisionViewModel model,
            string adminId)
        {
            if (model.Decision != SupplierStatus.Approved &&
                model.Decision != SupplierStatus.Rejected &&
                model.Decision != SupplierStatus.Suspended)
                return "InvalidDecision";

            if (model.Decision != SupplierStatus.Approved &&
                string.IsNullOrWhiteSpace(model.ReviewNotes))
                return "ReasonRequired";

            var supplier = await _context.Suppliers
                .FirstOrDefaultAsync(s => s.Id == model.SupplierId);

            if (supplier == null)
                return "NotFound";

            // Allowed status transitions
            var allowed =
                (supplier.Status == SupplierStatus.Pending &&
                 (model.Decision == SupplierStatus.Approved ||
                  model.Decision == SupplierStatus.Rejected))
                ||
                (supplier.Status == SupplierStatus.Approved &&
                 model.Decision == SupplierStatus.Suspended)
                ||
                (supplier.Status == SupplierStatus.Suspended &&
                 model.Decision == SupplierStatus.Approved);

            if (!allowed)
                return "InvalidTransition";

            if (model.Decision == SupplierStatus.Approved)
            {
                var documents = await _context.Documents
                    .AsNoTracking()
                    .Where(d => d.SupplierId == supplier.Id)
                    .ToListAsync();

                if (documents.Count == 0)
                    return "DocumentsRequired";

                var allVerified = documents.All(d =>
                    d.VerificationStatus ==
                        DocumentVerificationStatus.Verified &&
                    (!d.ExpiresAt.HasValue ||
                     d.ExpiresAt.Value.Date >= DateTime.UtcNow.Date));

                if (!allVerified)
                    return "DocumentsNotVerified";
            }

            var oldStatus = supplier.Status;

            await using var transaction =
                await _context.Database.BeginTransactionAsync();

            supplier.Status = model.Decision;
            supplier.ReviewNotes = model.ReviewNotes?.Trim();
            supplier.ReviewedByUserId = adminId;
            supplier.ReviewedAt = DateTime.UtcNow;

            // Accounts linked to this supplier
            var accounts = await _context.Users
                .Where(u => u.SupplierId == supplier.Id)
                .ToListAsync();

            // Rejected or suspended supplier accounts
            // must be disabled.
            if (model.Decision == SupplierStatus.Rejected ||
                model.Decision == SupplierStatus.Suspended)
            {
                foreach (var account in accounts)
                {
                    account.IsActive = false;
                    account.SecurityStamp = Guid.NewGuid().ToString();
                }
            }

            // Re-approving does not automatically reactivate
            // accounts that might have been disabled separately.

            foreach (var account in accounts)
            {
                _context.Notifications.Add(new Notification
                {
                    UserId = account.Id,
                    Title = "Supplier Verification Update",
                    Message = $"Your company registration status is now {model.Decision}. " +
                        (string.IsNullOrWhiteSpace(model.ReviewNotes)
                            ? ""
                            : $"Review notes: {model.ReviewNotes.Trim()}"),
                    Link = "/Supplier",
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow
                });
            }

            _context.AuditLogs.Add(new AuditLog
            {
                UserId = adminId,
                Action = "SupplierReviewed",
                EntityName = "Supplier",
                EntityId = supplier.Id.ToString(),
                OldValues = JsonSerializer.Serialize(new
                {
                    Status = oldStatus
                }),
                NewValues = JsonSerializer.Serialize(new
                {
                    Status = supplier.Status,
                    supplier.ReviewNotes,
                    supplier.ReviewedByUserId,
                    supplier.ReviewedAt
                }),
                CreatedAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            return "Success";
        }




    }

}