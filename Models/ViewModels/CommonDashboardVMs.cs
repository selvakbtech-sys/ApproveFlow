namespace ApproveFlow.MVC.Models.ViewModels;

using ApproveFlow.MVC.Models.Dwo;

// ── Common Dashboard ───────────────────────────────────────────────────────────
// The landing page carries TWO independent sections. Nothing is added, averaged or
// reconciled across them: ApproveFlow is filled by IApproveFlowDashboardService from the
// ApproveFlow tables, DWO by IDwoDashboardService from the DWO module's own data source.
// They meet only in this carrier, and only so one page can render both rows.
public class CommonDashboardVM
{
    // Null when the user may not view the ApproveFlow module — the section is not rendered.
    public ApproveFlowDashboardVM? ApproveFlow { get; set; }
    // Null when no DWO module is wired into this deployment, or the user may not view it.
    // The section then renders as unavailable rather than as zeros.
    public DwoDashboardVM? Dwo { get; set; }
    // Distinguishes "DWO not installed here" from "you may not see DWO", so the section can
    // explain itself accurately.
    public bool DwoModuleRegistered { get; set; }
    public bool CanViewDwo { get; set; }
}

// The ApproveFlow half. Every figure comes from the ApproveFlow Requests table, scoped to
// what the logged-in user may see, and every card carries the FilterStatus it links to so
// the grid it opens is filtered identically.
public class ApproveFlowDashboardVM
{
    public List<ApproveFlowKpiCardVM> Kpis { get; set; } = new();
    public List<ApproveFlowRecentRequestVM> RecentRequests { get; set; } = new();
    // True when the counts span every requestor (Admin); false when scoped to the user.
    public bool IsGlobalScope { get; set; }
}

public class ApproveFlowKpiCardVM
{
    public string Label { get; set; } = "";
    // The value handed to /Requests?FilterStatus=… — the same value the count was made with.
    public string FilterStatus { get; set; } = "";
    public int Count { get; set; }
    public string Accent { get; set; } = "#0f172a";
}

public class ApproveFlowRecentRequestVM
{
    public int RequestId { get; set; }
    public string RequestNumber { get; set; } = "";
    public string DisplayRequestNumber => Helpers.RequestNumberHelper.Format(RequestNumber);
    public string Category { get; set; } = "";
    public string RequestedBy { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTimeOffset Date { get; set; }
}
