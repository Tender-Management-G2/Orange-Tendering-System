// ORANGE TENDER MANAGEMENT SYSTEM
// Owner: Rjoub
// Module: Administration
// Purpose: MVC endpoints for the Users pages of the Admin workspace.
// Status: Scaffold only. GET actions return views; no POST actions, no database access, no business logic.
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Orange_Tendering_System.Models;

namespace Orange_Tendering_System.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = SystemRoles.Admin)]
    public class UsersController : Controller
    {
        // User Management — Owner: Rjoub · Trello: User Management (https://trello.com/c/UYxdWeUv)
        // GET /Admin/Users. TODO: query the data in a service and pass a view model.
        public IActionResult Index()
        {
            return View();
        }

        // Create User — Owner: Rjoub · Trello: User Management (https://trello.com/c/UYxdWeUv)
        // GET /Admin/Users/Create. TODO: query the data in a service and pass a view model.
        public IActionResult Create()
        {
            return View();
        }

        // Edit User — Owner: Rjoub · Trello: User Management (https://trello.com/c/UYxdWeUv)
        // GET /Admin/Users/Edit/{id?}. TODO: load the record by id (NotFound when missing) and check access.
        public IActionResult Edit(int? id)
        {
            return View();
        }

        // User Details — Owner: Rjoub · Trello: User Management (https://trello.com/c/UYxdWeUv)
        // GET /Admin/Users/Details/{id?}. TODO: load the record by id (NotFound when missing) and check access.
        public IActionResult Details(int? id)
        {
            return View();
        }
    }
}
