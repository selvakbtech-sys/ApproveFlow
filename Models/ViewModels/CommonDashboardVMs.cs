namespace ApproveFlow.MVC.Models.ViewModels;

// ── Common Dashboard ───────────────────────────────────────────────────────────
// The landing page after login. It carries one section per module. Nothing is added,
// averaged or reconciled across sections: each is filled from its own module's data and
// they meet only in this carrier, so one page can render them together.
//
// This file is self-contained on purpose — the card definitions live here rather than in
// RequestStatus.cs, so adding the Common Dashboard changes no existing file.
public class CommonDashboardVM
{
    // Null when the user may not view the ApproveFlow module — the section is not rendered.
    public ApproveFlowSectionVM? ApproveFlow { get; set; }

    // True when the user holds any DWO screen — the DWO section is rendered.
    public bool CanViewDwo { get; set; }
    // Null when the DWO module supplies no dashboard provider in this deployment. The
    // section still renders (gated and navigable) but its counts show "—" rather than 0.
    public ApproveFlow.MVC.Models.Dwo.DwoDashboardVM? Dwo { get; set; }
    // Distinguishes "no DWO provider wired in" from "the module returned no rows", so the
    // section can explain itself accurately instead of looking empty.
    public bool DwoProviderRegistered { get; set; }
}

// The ApproveFlow half. Every figure comes from the Requests table, scoped to what the
// logged-in user may see, and every card carries the FilterStatus it links to so the grid
// it opens is filtered identically.
public class ApproveFlowSectionVM
{
    public List<CommonKpiCardVM> Kpis { get; set; } = new();
    // Charts. Every series is built from the SAME scoped query as the KPI cards, so the
    // donut totals equal the cards and the trend never counts a request the grid hides.
    public List<CommonMonthPointVM> Trend { get; set; } = new();      // last 6 months: raised vs approved
    public List<CommonSliceVM> ByType { get; set; } = new();          // top request types, all time
    public List<CommonSliceVM> ByPriority { get; set; } = new();      // open (not yet decided) requests
    // True when the counts span every requestor (Admin); false when scoped to one user.
    public bool IsGlobalScope { get; set; }

    // "My Approvals" — requests assigned to the logged-in approver. Same datasource as the
    // Dashboard's My Approvals tab and the /Requests/Approval grid
    // (DashboardService.MyApprovalScope), so the cards and the grid always agree.
    public MyApprovalSummaryVM? MyApprovals { get; set; }

    // "My Requests" / "My Territory" — the SAME figures the Dashboard's tabs of those names
    // show (DashboardService.GetMyRequestsTabAsync / GetMyTerritoryTabAsync), so the Common
    // Dashboard KPI cards always equal the Dashboard cards and the grids they open.
    // MyTerritory is null for the View Only role, which has no territory tab.
    public MyRequestsTabVM? MyRequests { get; set; }
    public MyTerritoryTabVM? MyTerritory { get; set; }
}

// One slice of a distribution chart (donut / horizontal bars).
public class CommonSliceVM
{
    public string Label { get; set; } = "";
    public int Count { get; set; }
    public string Color { get; set; } = "#94a3b8";
    public string? Url { get; set; }
}

// One month of a two-series trend chart.
public class CommonMonthPointVM
{
    public string Label { get; set; } = "";       // "Apr 26"
    public int Primary { get; set; }              // raised
    public int Secondary { get; set; }            // approved / closed
}

public class CommonKpiCardVM
{
    public string Label { get; set; } = "";
    // The value handed to /Requests?FilterStatus=… — the SAME value the count was made
    // with, which is what keeps the card and the grid it opens in agreement.
    public string FilterStatus { get; set; } = "";
    public int Count { get; set; }
    public string Accent { get; set; } = "#0f172a";
}

// The six ApproveFlow KPI cards, in display order.
//
// Every card is a SINGLE concrete status that the Requests grid already matches exactly.
// No card is a multi-status bucket, so none of them needs RequestService to learn a new
// filter token — the existing exact-match behaviour is enough for each card total to equal
// the grid it opens.
public static class CommonDashboardKpi
{
    public static readonly (string Label, string FilterStatus, string Accent)[] ApproveFlowCards =
    {
        ("Draft",             RequestStatus.Draft,               "#475569"),
        ("Pending Approval",  RequestStatus.PendingApproval,     "#d97706"),
        ("Partial Approved",  RequestStatus.PartialApproved,     "#0891b2"),
        ("Clarification",     RequestStatus.ClarificationNeeded, "#7c3aed"),
        ("Approved",          RequestStatus.Approved,            "#16a34a"),
        ("Rejected",          RequestStatus.Rejected,            "#dc2626"),
    };
}
