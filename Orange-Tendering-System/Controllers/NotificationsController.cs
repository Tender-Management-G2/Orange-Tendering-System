// ORANGE TENDER MANAGEMENT SYSTEM
// Owner: ALL TEAM
// Module: Clarifications, Notifications & Audit Trail
// Purpose: MVC endpoints for the Notifications pages of the shared signed-in pages.
// Status: Scaffold only. GET actions return views; no POST actions, no database access, no business logic.
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orange_Tendering_System.Services;

namespace Orange_Tendering_System.Controllers
{
    [Authorize]
    [AutoValidateAntiforgeryToken]
    public class NotificationsController(TenderWorkflowService workflow) : Controller
    {
        // Notifications — Owner: ALL TEAM · Trello: Clarifications, Notifications & Audit Trail (https://trello.com/c/N1OFBdNM)
        // GET /Notifications. TODO: query the data in a service and pass a view model.
        public async Task<IActionResult> Index() => View(await workflow.Notifications(User));
        [HttpPost] public async Task<IActionResult> MarkRead() { await workflow.ReadNotifications(User); return RedirectToAction(nameof(Index)); }
    }
}
