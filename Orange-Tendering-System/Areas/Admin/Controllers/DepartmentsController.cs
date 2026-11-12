// ORANGE TENDER MANAGEMENT SYSTEM
// Owner: Rjoub
// Module: Administration
// Purpose: MVC endpoints for the Departments pages of the Admin workspace.
// Status: Scaffold only. GET actions return views; no POST actions, no database access, no business logic.
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orange_Tendering_System.Models;

namespace Orange_Tendering_System.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = SystemRoles.Admin)]
    public class DepartmentsController : Controller
    {
        // Department Management — Owner: Rjoub · Trello: Department Management (https://trello.com/c/KHRsEmzE)
        // GET /Admin/Departments. TODO: query the data in a service and pass a view model.
        public IActionResult Index()
        {
            return View();
        }

        // Create Department — Owner: Rjoub · Trello: Department Management (https://trello.com/c/KHRsEmzE)
        // GET /Admin/Departments/Create. TODO: query the data in a service and pass a view model.
        public IActionResult Create()
        {
            return View();
        }

        // Edit Department — Owner: Rjoub · Trello: Department Management (https://trello.com/c/KHRsEmzE)
        // GET /Admin/Departments/Edit/{id?}. TODO: load the record by id (NotFound when missing) and check access.
        public IActionResult Edit(int? id)
        {
            return View();
        }
    }
}
