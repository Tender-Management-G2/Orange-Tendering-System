// ORANGE TENDER MANAGEMENT SYSTEM
// Owner: Tariq — Public Site (Index, About, Team, HowItWorks, Contact)
//        UNASSIGNED — Privacy (policy text not supplied)
//        ALL TEAM — Error, HttpStatus (shared error pages)
// Module: Public Site & Shared Layout
// Purpose: Public website pages, converted from the supplied layout ZIP.
// Status: Static pages. Team content comes from wwwroot/data/team.json. No business logic.
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.ViewModels;
using System.Diagnostics;
using System.Text.Json;

namespace Orange_Tendering_System.Controllers
{
    public class HomeController(IWebHostEnvironment environment, ApplicationDbContext db) : Controller
    {
        // Home / Landing Page — Owner: Tariq (Trello: Home / Landing Page)
        public async Task<IActionResult> Index()
        {
            // Public, non-confidential aggregates only (counts); no names, prices or documents ever leave the database here.
            var statuses = await db.Tenders.AsNoTracking().GroupBy(t => t.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync();
            int Count(params TenderStatus[] set) => statuses.Where(x => set.Contains(x.Key)).Sum(x => x.Count);
            var since = DateTime.UtcNow.AddMonths(-5);
            var first = new DateTime(since.Year, since.Month, 1);
            var created = await db.Tenders.AsNoTracking().Where(t => t.CreatedAt >= first).Select(t => t.CreatedAt).ToListAsync();
            ViewBag.Stats = new PublicStats(
                Count(TenderStatus.Bidding, TenderStatus.Evaluation, TenderStatus.OfficerReview, TenderStatus.ManagerReview, TenderStatus.TopCommitteeReview),
                await db.Suppliers.AsNoTracking().CountAsync(x => x.Status == SupplierStatus.Approved),
                await db.PurchaseRequests.AsNoTracking().CountAsync(r => r.Status == PurchaseRequestStatus.Accepted || r.Status == PurchaseRequestStatus.Approved || r.Status == PurchaseRequestStatus.ConvertedToTender),
                Count(TenderStatus.Bidding), Count(TenderStatus.Evaluation, TenderStatus.OfficerReview),
                Count(TenderStatus.ManagerReview, TenderStatus.TopCommitteeReview), Count(TenderStatus.Awarded, TenderStatus.Rejected, TenderStatus.Cancelled),
                Enumerable.Range(0, 6).Select(i => first.AddMonths(i)).Select(m => new KeyValuePair<string, int>(m.ToString("MMM"), created.Count(d => d.Year == m.Year && d.Month == m.Month))).ToList());
            return View();
        }

        // Owner: Tariq — no dedicated Trello card
        [HttpGet("/about")]
        public IActionResult About()
        {
            return View();
        }

        // Owner: Tariq (Trello: Team Page — Doing). Edit wwwroot/data/team.json, not this action.
        [HttpGet("/team")]
        public async Task<IActionResult> Team(CancellationToken cancellationToken)
        {
            await using var file = System.IO.File.OpenRead(Path.Combine(environment.WebRootPath, "data", "team.json"));
            var members = await JsonSerializer.DeserializeAsync<List<TeamMemberViewModel>>(file,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, cancellationToken) ?? [];
            return View(members);
        }

        // Owner: Tariq — no dedicated Trello card
        [HttpGet("/how-it-works")]
        public IActionResult HowItWorks()
        {
            return View();
        }

        // Owner: Tariq — no dedicated Trello card. The form posts to ContactController.
        [HttpGet("/contact")]
        public IActionResult Contact()
        {
            return View();
        }

        // Owner: UNASSIGNED — privacy policy text has not been supplied
        public IActionResult Privacy()
        {
            return View();
        }

        // Owner: ALL TEAM — unhandled exceptions (production only, see Program.cs)
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }

        // Owner: ALL TEAM — 404 and other empty error responses (UseStatusCodePagesWithReExecute)
        [HttpGet("/status/{code:int}")]
        public IActionResult HttpStatus(int code)
        {
            return View(code);
        }
    }
}

public sealed record PublicStats(int ActiveTenders, int ApprovedSuppliers, int CompletedRequests, int Bidding, int Evaluation, int Approvals, int Closed, List<KeyValuePair<string, int>> Monthly);
