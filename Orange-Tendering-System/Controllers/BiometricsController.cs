// ORANGE TENDER MANAGEMENT SYSTEM
// Owner: Ahmad
// Module: Authentication & Biometrics
// Purpose: MVC endpoints for the Biometrics pages of the shared signed-in pages.
// Status: Scaffold only. GET actions return views; no POST actions, no database access, no business logic.
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Orange_Tendering_System.Controllers
{
    [Authorize]
    public class BiometricsController : Controller
    {
        // Fingerprint Registration & Login — Owner: Ahmad · Trello: Fingerprint Registration & Login
        // GET /Biometrics/Fingerprint. TODO: query the data in a service and pass a view model.
        public IActionResult Fingerprint()
        {
            return View();
        }

        // Face Registration & Recognition Login — Owner: Ahmad · Trello: Face Registration & Recognition Login
        // GET /Biometrics/Face. TODO: query the data in a service and pass a view model.
        public IActionResult Face()
        {
            return View();
        }
    }
}
