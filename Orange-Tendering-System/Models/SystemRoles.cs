namespace Orange_Tendering_System.Models
{
    // ORANGE TENDER MANAGEMENT SYSTEM
    // Owner: Ahmad — Authentication & Database (role seeding)
    // Purpose: The seven Identity role names used by [Authorize(Roles = ...)] on every workspace area.
    // Status: Names only. The roles are NOT seeded yet — until they exist in AspNetRoles and users are
    //         assigned to them, every workspace page correctly answers "Access denied".
    public static class SystemRoles
    {
        public const string Admin = "Admin";
        public const string RequestingDepartment = "Requesting Department";
        public const string SourcingOfficer = "Sourcing Officer";
        public const string Supplier = "Supplier";
        public const string EvaluationCommittee = "Evaluation Committee";
        public const string SourcingManager = "Sourcing Manager";
        public const string TopCommittee = "Top Committee";

        public static readonly string[] All = [Admin, RequestingDepartment, SourcingOfficer, Supplier, EvaluationCommittee, SourcingManager, TopCommittee];
    }
}
