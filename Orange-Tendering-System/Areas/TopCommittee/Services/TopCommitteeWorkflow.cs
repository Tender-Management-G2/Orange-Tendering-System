using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.Services;

namespace Orange_Tendering_System.Areas.TopCommittee.Services;

// Role aliases are limited to Top Committee requests. Identity memberships are never changed.
public static class TopCommitteeWorkflow
{
    public static TenderWorkflowService Create(IServiceProvider services) =>
        ActivatorUtilities.CreateInstance<TenderWorkflowService>(services, new ExecutiveUserManager(services));

    private sealed class ExecutiveUserManager : UserManager<ApplicationUser>
    {
        public ExecutiveUserManager(IServiceProvider services) : base(
            services.GetRequiredService<IUserStore<ApplicationUser>>(),
            services.GetRequiredService<IOptions<IdentityOptions>>(),
            services.GetRequiredService<IPasswordHasher<ApplicationUser>>(),
            services.GetServices<IUserValidator<ApplicationUser>>(),
            services.GetServices<IPasswordValidator<ApplicationUser>>(),
            services.GetRequiredService<ILookupNormalizer>(),
            services.GetRequiredService<IdentityErrorDescriber>(), services,
            services.GetRequiredService<ILogger<UserManager<ApplicationUser>>>()) { }

        public override async Task<bool> IsInRoleAsync(ApplicationUser user, string role)
        {
            if (await base.IsInRoleAsync(user, role)) return true;
            return role == SystemRoles.TopCommittee &&
                (await GetRolesAsync(user)).Any(r => TopExecutiveRoles.Allowed.Split(',').Contains(r));
        }
    }
}
