// ORANGE TENDER MANAGEMENT SYSTEM
// Owner: Tariq — Public Site (Index, About, Team, HowItWorks, Contact)
//        UNASSIGNED — Privacy (policy text not supplied)
//        ALL TEAM — Error, HttpStatus (shared error pages)
// Module: Public Site & Shared Layout
// Purpose: Public website pages, converted from the supplied layout ZIP.
// Status: Static pages. Team content comes from wwwroot/data/team.json. No business logic.
using Microsoft.AspNetCore.Mvc;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.ViewModels;
using System.Diagnostics;
using System.Text.Json;

namespace Orange_Tendering_System.Controllers
{
    public class HomeController(IWebHostEnvironment environment) : Controller
    {
        // Home / Landing Page — Owner: Tariq (Trello: Home / Landing Page)
        public IActionResult Index()
        {
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
