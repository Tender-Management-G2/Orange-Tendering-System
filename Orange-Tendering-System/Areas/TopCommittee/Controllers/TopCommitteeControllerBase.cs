using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Orange_Tendering_System.Areas.TopCommittee.Services;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models.Entities;

namespace Orange_Tendering_System.Areas.TopCommittee.Controllers;

public abstract class TopCommitteeControllerBase : Controller
{
    protected readonly ApplicationDbContext Db;
    protected readonly UserManager<ApplicationUser> Users;
    protected readonly TopCommitteeService Committee;
    protected TopCommitteeControllerBase(ApplicationDbContext db, UserManager<ApplicationUser> users)
    {
        var options = db.GetService<IDbContextOptions>() as DbContextOptions<ApplicationDbContext>
            ?? throw new InvalidOperationException("The configured project database options are unavailable.");
        Db = new ApplicationDbContext(TopCommitteeCompatibility.Options(options));
        Users = users;
        Committee = new(Db, users);
    }
    protected async Task<ApplicationUser?> ActiveUser()
    { var user = await Users.GetUserAsync(User); return user is { IsActive: true } ? user : null; }
    protected override void Dispose(bool disposing)
    {
        if (disposing) Db.Dispose();
        base.Dispose(disposing);
    }
}
