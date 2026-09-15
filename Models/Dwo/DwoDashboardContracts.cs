namespace ApproveFlow.MVC.Models.Dwo;

// ── DWO ⇄ Common Dashboard contract ────────────────────────────────────────────
// DWO is an INDEPENDENT module with its own data source. The Common Dashboard never
// queries DWO tables and never derives a DWO number from ApproveFlow data; it asks the
// DWO module for its own already-scoped figures through this interface and renders them
// in a separate section.
//
// The DWO module supplies the implementation and registers it in DI:
//     builder.Services.AddScoped<IDwoDashboardService, DwoDashboardService>();
//
// When no implementation is registered (this deployment does not carry the DWO module)
// the Common Dashboard renders the DWO section as UNAVAILABLE. It does NOT invent zero
// counts — a zero would read as "no discussions", which is a statement about DWO data
// this application is in no position to make.
public interface IDwoDashboardService
{
    // Every figure and every target URL is produced by the DWO module itself, scoped to
    // the caller described by `ctx`. ApproveFlow passes identity through and applies no
    // filtering of its own.
    Task<DwoDashboardVM> GetDashboardAsync(DwoUserContext ctx, CancellationToken ct = default);
}

// Who is asking. Handed to the DWO module so it can apply ITS OWN permission model —
// Super Admin / Admin see every discussion, everyone else only what they may access.
public class DwoUserContext
{
    public int UserId { get; set; }
    public string Role { get; set; } = "";
    public bool IsAdmin { get; set; }
}

// The DWO half of the Common Dashboard. Populated exclusively from DWO data.
public class DwoDashboardVM
{
    // The six DWO KPI cards, in display order. Keys come from DwoKpiKeys.
    public List<DwoKpiCardVM> Kpis { get; set; } = new();
    // "DWO Recent Discussions" grid — rendered in its own table, never merged with the
    // ApproveFlow Recent Requests grid.
    public List<DwoRecentDiscussionVM> RecentDiscussions { get; set; } = new();
    // Where the "View all" link on the recent-discussions grid points.
    public string? DiscussionsUrl { get; set; }
}

// Canonical keys for the six DWO cards. The dashboard orders and labels cards from these;
// a card the DWO module does not return is simply not rendered.
public static class DwoKpiKeys
{
    public const string DiscussionRegister    = "DiscussionRegister";
    public const string MyDiscussions         = "MyDiscussions";
    public const string ManagementDiscussion  = "ManagementDiscussion";
    public const string PendingAction         = "PendingAction";
    public const string Closed                = "Closed";
    public const string Reports               = "Reports";

    // Display order and default labels used by the Common Dashboard.
    public static readonly (string Key, string Label, string Accent)[] Ordered =
    {
        (DiscussionRegister,   "Discussion Register",   "#0f172a"),
        (MyDiscussions,        "My Discussions",        "#2563eb"),
        (ManagementDiscussion, "Management Discussion", "#0891b2"),
        (PendingAction,        "Pending Action",        "#d97706"),
        (Closed,               "Closed",                "#16a34a"),
        (Reports,              "Reports",               "#7c3aed"),
    };
}

public class DwoKpiCardVM
{
    // One of DwoKpiKeys.
    public string Key { get; set; } = "";
    // Optional label override; the dashboard falls back to the canonical label.
    public string? Label { get; set; }
    public int Count { get; set; }
    // The DWO screen this card opens, WITH its filter already applied — supplied by the
    // DWO module because it owns its own routes. Empty renders a non-navigating card.
    public string? Url { get; set; }
    // Optional one-line caption under the label.
    public string? Hint { get; set; }
}

public class DwoRecentDiscussionVM
{
    public string DiscussionNo { get; set; } = "";
    public string Project { get; set; } = "";
    public string Department { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTimeOffset? DueDate { get; set; }
    // Link to the discussion detail screen in the DWO module.
    public string? Url { get; set; }
}
