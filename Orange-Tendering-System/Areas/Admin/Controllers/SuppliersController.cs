// ORANGE TENDER MANAGEMENT SYSTEM
// Owner: Rjoub
// Module: Administration
// Purpose: MVC endpoints for the Suppliers pages of the Admin workspace.
// Status: Scaffold only. GET actions return views; no POST actions, no database access, no business logic.
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orange_Tendering_System.Models;

namespace Orange_Tendering_System.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = SystemRoles.Admin)]
    public class SuppliersController : Controller
    {
        // Supplier Administration — Owner: Rjoub · Trello: Supplier Administration (https://trello.com/c/zvMwn4LD)
        // GET /Admin/Suppliers. TODO: query the data in a service and pass a view model.
        public IActionResult Index()
        {
            return View();
        }

        // Supplier Registration Review — Owner: Rjoub · Trello: Supplier Administration (https://trello.com/c/zvMwn4LD)
        // GET /Admin/Suppliers/Review/{id?}. TODO: load the record by id (NotFound when missing) and check access.
        public IActionResult Review(int? id)
        {
            return View();
        }

        // Supplier Details — Owner: Rjoub · Trello: Supplier Administration (https://trello.com/c/zvMwn4LD)
        // GET /Admin/Suppliers/Details/{id?}. TODO: load the record by id (NotFound when missing) and check access.
        public IActionResult Details(int? id)
        {
            return View();
        }
    }
}
