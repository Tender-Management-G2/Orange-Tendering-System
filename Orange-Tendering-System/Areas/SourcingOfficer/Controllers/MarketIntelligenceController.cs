// ORANGE TENDER MANAGEMENT SYSTEM
// Module: Sourcing Officer / Market Intelligence
// Redirects the old Orange Radar address to Orange Intelligence.
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orange_Tendering_System.Models;

namespace Orange_Tendering_System.Areas.SourcingOfficer.Controllers;

[Area("SourcingOfficer")]
[Authorize(Roles = SystemRoles.SourcingOfficer)]
public sealed class MarketIntelligenceController : Controller
{
    [HttpGet]
    public IActionResult Index() => RedirectToAction("Index", "Intelligence", new { area = "" });
}
