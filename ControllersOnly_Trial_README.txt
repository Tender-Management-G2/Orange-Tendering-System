Orange Tendering System — Controller-only trial draft

This is an external planning file. It is intentionally outside the application project and is not compiled or wired into the running site. The existing ZIP/source project was not changed.

Important: the SourcingOfficer controllers in the project already perform their database work directly through ApplicationDbContext. Their source files are:
Orange-Tendering-System\Areas\SourcingOfficer\Controllers\*.cs

The Supplier portal currently delegates operations to Supplier*Service classes. Converting every Supplier page safely requires moving each service operation (offer lifecycle, invitation response, uploads/storage, profile ownership, and clarification messaging) into its matching controller while retaining the existing view models and routes. A one-file controller dump cannot be dropped into the project: it would duplicate controller class names and break compilation. Keep the existing controllers as-is while reviewing this draft plan.

Controller-only conversion map (all EF access via injected ApplicationDbContext):

SOURCING OFFICER
- PurchaseRequestsController: Index, Review GET/POST; load Department/Items, save decision + PurchaseRequestReview.
- TendersController: Index, Create GET/POST, Details, SelectSuppliers, InviteSupplier; create rounds/items and notifications.
- BidsController: Index, Details, Compliance GET/POST, ForwardToCommittee GET/POST; validate compliance and save report/committee round.
- ReportsController: Index, Details, SubmitToManager; load committee report, notify assigned manager, save AuditLog.
- ClarificationsController: Index, Details, Reply; scope threads to officer and persist messages/notifications.
- DashboardController: Index; query the counts and next actions.
- InvitationsController: currently a scaffold and can be removed if not used by the views.

SUPPLIER
- DashboardController: query the signed-in user's active, approved SupplierId and build dashboard view model.
- InvitationsController: list/details/respond; always constrain invitation by the signed-in user's SupplierId.
- OffersController: list/create/edit/details/history/submit/summary; verify invitation ownership, open bidding dates, item IDs and bid status on every POST.
- ProfileController: load/update company contact data for the authenticated SupplierId only.
- DocumentsController: list/download/upload; verify supplier ownership for every document/bid/message, validate file type/size, store file and metadata.
- ClarificationsController: list/details/create/reply; verify that invitation or bid belongs to the signed-in supplier.
- NotificationsController: list/mark read; constrain notification to the current user.

Use this constructor pattern in each controller:

private readonly ApplicationDbContext _context;
public ExampleController(ApplicationDbContext context) { _context = context; }

Use these namespaces:
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models;
using Orange_Tendering_System.Models.Entities;
using Orange_Tendering_System.ViewModels;

For Supplier actions, get the owner like this and return Challenge if it is missing:
var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
var supplierId = await _context.Users.Where(u => u.Id == userId && u.IsActive)
    .Select(u => u.SupplierId).SingleOrDefaultAsync();
if (supplierId == null) return Forbid();

Do not bind navigation properties from forms. Load the entity from EF, check ownership/status, then update only allowed fields. Keep [ValidateAntiForgeryToken] on every POST.

This file is not the converted implementation yet; it records the exact safe conversion boundary. The next deliverable should be a separate ControllersOnly_Trial folder containing one replacement .cs file per controller, copied views/view-model contracts, and a compile check before changing the original ZIP.
