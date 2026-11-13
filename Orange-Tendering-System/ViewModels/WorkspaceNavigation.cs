using System.Security.Claims;
using Orange_Tendering_System.Models;

namespace Orange_Tendering_System.ViewModels
{
    // ORANGE TENDER MANAGEMENT SYSTEM
    // Owner: Tariq — Shared Layout
    // Purpose: The sidebar / supplier navbar menu of every workspace, in one place.
    //          Every entry must point to an existing GET action (see docs/ROUTE_MAP.md).
    // Status: Navigation only. Showing a menu never grants access; each controller has its own [Authorize].

    // Area = null means "the workspace's own area"; Area = "" means a shared page outside any area.
    public record NavItem(string Label, string Controller, string Action, string Icon, string? Area = null);

    public record Workspace(string Area, string Label, string Role, IReadOnlyList<NavItem> Items);

    public static class WorkspaceNavigation
    {
        // Shown under every internal role's menu.
        public static readonly IReadOnlyList<NavItem> SharedItems =
        [
            new("My Tasks", "Tasks", "Index", "check", ""),
            new("Clarifications", "Clarifications", "Index", "chat", ""),
            new("Notifications", "Notifications", "Index", "bell", "")
        ];

        public static readonly IReadOnlyList<Workspace> Workspaces =
        [
            new("Admin", "Admin", SystemRoles.Admin,
            [
                new("Dashboard", "Dashboard", "Index", "home"),
                new("Users", "Users", "Index", "users"),
                new("Roles", "Roles", "Index", "shield"),
                new("Departments", "Departments", "Index", "building"),
                new("Suppliers", "Suppliers", "Index", "layers"),
                new("Settings", "Settings", "Index", "settings"),
                new("Audit Logs", "AuditLogs", "Index", "list"),
                new("Analytics & Exports", "Analytics", "Index", "chart", "")
            ]),
            new("Department", "Department", SystemRoles.RequestingDepartment,
            [
                new("Dashboard", "Dashboard", "Index", "home"),
                new("New Request", "PurchaseRequests", "Create", "plus"),
                new("My Requests", "PurchaseRequests", "Index", "request")
            ]),
            new("SourcingOfficer", "Sourcing Officer", SystemRoles.SourcingOfficer,
            [
                new("Dashboard", "Dashboard", "Index", "home"),
                new("Requests", "PurchaseRequests", "Index", "request"),
                new("Tenders", "Tenders", "Index", "tender"),
                new("Received Bids", "Bids", "Index", "layers"),
                new("Reports", "Reports", "Index", "report"),
                  new("Supplier Clarifications", "Clarifications", "Index", "chat")
            ]),
            new("Supplier", "Supplier Portal", SystemRoles.Supplier,
            [
                new("Dashboard", "Dashboard", "Index", "home"),
                new("Invitations", "Invitations", "Index", "mail"),
                new("My Offers", "Offers", "Index", "layers"),
                new("Documents", "Documents", "Index", "folder"),
                new("Profile", "Profile", "Index", "user"),
                new("Notifications", "Notifications", "Index", "bell"),
                new("Clarifications", "Clarifications", "Index", "chat")
            ]),
            new("EvaluationCommittee", "Evaluation Committee", SystemRoles.EvaluationCommittee,
            [
                new("Dashboard", "Dashboard", "Index", "home"),
                new("Assigned Evaluations", "Evaluations", "Index", "request"),
                new("Criteria & Weights", "Criteria", "Index", "scale"),
                new("Technical Evaluation", "Evaluations", "Technical", "settings"),
                new("Financial Evaluation", "Evaluations", "Financial", "chart"),
                new("Scores", "Evaluations", "Scores", "vote"),
                new("AI Analysis", "Evaluations", "AiAnalysis", "ai"),
                new("Unified Report", "Reports", "Unified", "report"),
                new("History", "Evaluations", "History", "history")
            ]),
            new("SourcingManager", "Sourcing Manager", SystemRoles.SourcingManager,
            [
                new("Dashboard", "Dashboard", "Index", "home"),
                new("Pending Reports", "Reports", "Index", "request"),
                new("Report Review", "Reports", "Review", "check"),
                new("Forwarding", "Reports", "Forward", "send"),
                new("Decision History", "Reports", "History", "history"),
                new("Analytics & Exports", "Analytics", "Index", "chart", "")
            ]),
            new("TopCommittee", "Top Committee", SystemRoles.TopCommittee,
            [
                new("Dashboard", "Dashboard", "Index", "home"),
                new("Evaluation Summary", "Decisions", "ReviewEvaluation", "report"),
                new("Voting", "Decisions", "Decision", "vote"),
                new("Decision History", "Decisions", "History", "history"),
                new("Management Statistics", "Statistics", "Index", "chart"),
                new("Analytics & Exports", "Analytics", "Index", "chart", "")
            ])
        ];

        public static Workspace? ForArea(string? area) =>
            Workspaces.FirstOrDefault(w => string.Equals(w.Area, area, StringComparison.OrdinalIgnoreCase));

        // For shared pages outside an area: the first workspace the signed-in user has a role for.
        public static Workspace? ForUser(ClaimsPrincipal user) =>
            Workspaces.FirstOrDefault(w => user.IsInRole(w.Role));

        public static Workspace? Current(string? area, ClaimsPrincipal user) => ForArea(area) ?? ForUser(user);

        public static string LayoutFor(ClaimsPrincipal user) =>
            user.IsInRole(SystemRoles.Supplier) ? "_SupplierLayout" : "_WorkspaceLayout";

        // Inline SVG content (24x24, stroke icons) taken from the supplied layout.
        public static readonly IReadOnlyDictionary<string, string> Icons = new Dictionary<string, string>
        {
            ["home"] = "<path d=\"m3 11 9-8 9 8M5 10v11h5v-7h4v7h5V10\"/>",
            ["request"] = "<path d=\"M5 3h9l5 5v13H5zM14 3v6h5M8 13h8M8 17h8\"/>",
            ["plus"] = "<path d=\"M12 5v14M5 12h14\"/>",
            ["tender"] = "<path d=\"m6 6 5-4 8 9-5 4zM3 15l6 7M7 13l-4 4M13 14l-6 5M15 21h7\"/>",
            ["rounds"] = "<path d=\"M4 12a8 8 0 0 1 14-5l2 2M20 4v5h-5M20 12a8 8 0 0 1-14 5l-2-2M4 20v-5h5\"/>",
            ["mail"] = "<rect x=\"2\" y=\"4\" width=\"20\" height=\"16\" rx=\"2\"/><path d=\"m2 5 10 8L22 5\"/>",
            ["layers"] = "<path d=\"m2 7 10-5 10 5-10 5zM2 12l10 5 10-5M2 17l10 5 10-5\"/>",
            ["check"] = "<rect x=\"5\" y=\"5\" width=\"14\" height=\"17\" rx=\"2\"/><path d=\"M9 3h6v5H9zM8 14l3 3 5-6\"/>",
            ["scale"] = "<path d=\"M12 2v19M7 22h10M3 6h18M5 6l-4 9h8zM19 6l-4 9h8z\"/>",
            ["chart"] = "<path d=\"M4 20h16M7 17v-7h3v7M14 17V4h3v13\"/>",
            ["chat"] = "<path d=\"M4 4h16v13H9l-5 4z\"/><path d=\"M8 10h.01M12 10h.01M16 10h.01\"/>",
            ["bell"] = "<path d=\"M5 16h14l-2-3V9a5 5 0 0 0-10 0v4zM10 20h4M12 2v2\"/>",
            ["users"] = "<circle cx=\"9\" cy=\"7\" r=\"4\"/><path d=\"M2 21v-3c0-7 14-7 14 0v3M17 4c5 0 5 7 0 7M18 14c4 0 4 4 4 7\"/>",
            ["user"] = "<circle cx=\"12\" cy=\"7\" r=\"4\"/><path d=\"M3 22v-3c0-10 18-10 18 0v3\"/>",
            ["shield"] = "<path d=\"m12 2 9 4v6c0 5-4 9-9 10-5-1-9-5-9-10V6z\"/>",
            ["building"] = "<path d=\"M3 21h18M5 21V7l7-4 7 4v14M9 9h1M14 9h1M9 13h1M14 13h1M9 17h1M14 17h1\"/>",
            ["settings"] = "<path d=\"m9 3-1 3-3 1-2 4 2 2-1 4 4 2 3-1 3 3 4-2v-3l3-2-1-4-3-1-1-4z\"/><circle cx=\"12\" cy=\"12\" r=\"3\"/>",
            ["history"] = "<circle cx=\"12\" cy=\"12\" r=\"9\"/><path d=\"M12 7v5l3 2\"/>",
            ["list"] = "<path d=\"M8 6h13M8 12h13M8 18h13M3 6h.01M3 12h.01M3 18h.01\"/>",
            ["ai"] = "<rect x=\"5\" y=\"5\" width=\"14\" height=\"14\" rx=\"2\"/><path d=\"M9 1v4M15 1v4M9 19v4M15 19v4M1 9h4M1 15h4M19 9h4M19 15h4\"/>",
            ["send"] = "<path d=\"M22 2 11 13M22 2l-7 20-4-9-9-4z\"/>",
            ["vote"] = "<path d=\"M8 4H5v18h14V4h-3M9 2h6v5H9zM8 14l3 3 5-6\"/>",
            ["folder"] = "<path d=\"M3 6h7l2 3h9v11H3z\"/>",
            ["compare"] = "<path d=\"M8 3v18M16 3v18M3 8h5M16 16h5\"/>",
            ["report"] = "<path d=\"M12 3v9h9\"/><path d=\"M8 4a9 9 0 1 0 12 12\"/>"
        };

        public static string Icon(string name) => Icons.TryGetValue(name, out var svg) ? svg : Icons["request"];
    }
}
