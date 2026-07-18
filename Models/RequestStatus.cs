namespace ApproveFlow.MVC.Models;

// Central definition of the request Status values used across the app. Introduced so
// the Dashboard → Approval-Requests navigation maps statuses through one place instead
// of scattering literals. (Existing code still uses string literals; new/refactored
// code should prefer these constants.)
public static class RequestStatus
{
    public const string Draft               = "Draft";
    public const string Open                = "Open";              // legacy alias of PendingApproval
    public const string PendingApproval     = "Pending Approval";
    public const string PartialApproved     = "Partial Approved";
    public const string Approved            = "Approved";
    public const string Rejected            = "Rejected";
    public const string ClarificationNeeded = "Clarification Needed";
    public const string Deleted             = "Deleted";
}

// Maps the friendly status token carried on the Approval-Requests URL
// (/Requests/Approval?status=Pending|Approved|Rejected|Clarification) to the concrete
// request statuses it represents. Returns null for "All" (no status filter). This is the
// single source of truth shared by the dashboard KPI links and the grid query, so the
// KPI counts and the grid totals can never diverge.
public static class ApprovalStatusFilter
{
    // Canonical tokens the UI emits (used for the dropdown + URL).
    public const string Pending       = "Pending";
    public const string Approved      = "Approved";
    public const string Rejected      = "Rejected";
    public const string Clarification = "Clarification";

    public static string[]? Map(string? token) => (token ?? "").Trim().ToLowerInvariant() switch
    {
        "pending"       => new[] { RequestStatus.Open, RequestStatus.PendingApproval, RequestStatus.PartialApproved },
        "approved"      => new[] { RequestStatus.Approved },
        "rejected"      => new[] { RequestStatus.Rejected },
        "clarification" => new[] { RequestStatus.ClarificationNeeded },
        _               => null, // All / empty → no status filter
    };
}
