namespace Orange_Tendering_System.ViewModels;

public static class AnalyticsMetricAppearance
{
    public static (string Color, string Icon) For(string label, string fallbackIcon) => label switch
    {
        "Purchase requests" or "Total requests" => ("orange", "request"),
        "Total tenders" or "Tenders created" => ("orange", "tender"),
        "Open tenders" or "Open for bidding" => ("blue", "unlock"),
        "Closed to bidding" => ("slate", "lock"),
        "Approved" or "Approved suppliers" => ("green", "check"),
        "Approval rate" => ("green", "trend"),
        "Under Review" => ("amber", "history"),
        "Rejected" => ("red", "dismiss"),
        "Awarded" or "Awarded tenders" or "Awards in period" => ("amber", "award"),
        "Registered suppliers" => ("violet", "users"),
        "Supplier participation" => ("teal", "users"),
        "Tenders with no offers" => ("violet", "inbox"),
        "Overdue evaluations" => ("red", "alert"),
        "Suspended" => ("slate", "pause"),
        "Documents awaiting verification" => ("teal", "search"),
        "Expired documents" => ("red", "dismiss"),
        "Documents expiring within 60 days" => ("amber", "calendar"),
        "Submitted offers" => ("blue", "send"),
        "Compliant offers" => ("green", "shield"),
        "Excluded offers" => ("red", "dismiss"),
        "Pending compliance review" => ("amber", "history"),
        "Submitted evaluation reports" => ("violet", "report"),
        "Manager approvals" => ("green", "check"),
        "Manager returns / rejections" => ("red", "return"),
        "Top Committee approvals" => ("violet", "vote"),
        "Final contract files" => ("teal", "folder"),
        "Audit entries" => ("slate", "list"),
        "Suppliers with multiple awards" => ("blue", "layers"),
        "Repeat award rate" => ("teal", "rounds"),
        _ => ("orange", fallbackIcon)
    };
}
