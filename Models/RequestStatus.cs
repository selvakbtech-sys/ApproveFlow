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
    public const string Revoked             = "Revoked";        // requestor withdrew the request
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

// ── Common Dashboard → Requests grid (ApproveFlow section only) ────────────────
// The ApproveFlow KPI cards on the Common Dashboard link to /Requests?FilterStatus=<value>.
// Most cards carry a concrete request status, which the grid already matches exactly. The
// "Submitted" card is the one card that is not a single status — it means "left Draft", i.e.
// every live status a submitted request can be in. Mapping it HERE, in the single place both
// the KPI count and the grid query consult, is what keeps the card and the grid total equal.
//
// Anything that is not a recognised multi-status token (every existing dropdown value) maps
// to null and keeps the grid's original exact-match behaviour.
public static class DashboardStatusFilter
{
    public const string Submitted = "Submitted";

    // Statuses a submitted request can hold — everything except Draft (and Deleted, which the
    // grid excludes on its own).
    public static readonly string[] SubmittedStatuses =
    {
        RequestStatus.Open,
        RequestStatus.PendingApproval,
        RequestStatus.PartialApproved,
        RequestStatus.ClarificationNeeded,
        RequestStatus.Approved,
        RequestStatus.Rejected,
        RequestStatus.Revoked,
    };

    // Multi-status expansion for a FilterStatus value, or null when the value is a plain
    // status that must keep matching exactly.
    public static string[]? Map(string? filterStatus) =>
        string.Equals(filterStatus, Submitted, StringComparison.OrdinalIgnoreCase)
            ? SubmittedStatuses
            : null;
}
