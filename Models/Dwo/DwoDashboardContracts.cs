namespace ApproveFlow.MVC.Models.Dwo;

// ── DWO ⇄ Common Dashboard contract ────────────────────────────────────────────
// DWO is an INDEPENDENT module with its own data. The Common Dashboard never queries DWO
// tables and never derives a DWO number from ApproveFlow data; it asks the DWO module for
// its own already-scoped figures through this interface and renders them in a separate
// section.
//
// The DWO module supplies the implementation and registers it in Program.cs:
//     builder.Services.AddScoped<IDwoDashboardService, DwoDashboardService>();
//
// Where no implementation is registered the section still renders — permission-gated, with
// working links to the DWO screens — but its counts show "—" rather than 0. A zero would
// read as "no discussions", which is a statement about DWO data this application cannot
// make on the module's behalf.
public interface IDwoDashboardService
{
    // Every figure is produced by the DWO module itself, scoped to the caller described by
    // `ctx`. ApproveFlow passes identity through and applies no filtering of its own.
    Task<DwoDashboardVM> GetDashboardAsync(DwoUserContext ctx, CancellationToken ct = default);
}

// Who is asking. Handed to the DWO module so it can apply ITS OWN permission model —
// Admin sees every discussion, everyone else only what they may access.
public class DwoUserContext
{
    public int UserId { get; set; }
    public string Role { get; set; } = "";
    public bool IsAdmin { get; set; }

    // The DWO permission flags, read from the caller's claims exactly as DwoController does
    // for its own pages, so the dashboard scopes identically to the grids its cards open.
    public bool CanViewRegister { get; set; }   // "DWO Register" (view)
    public bool IsManagement    { get; set; }   // Admin or top Workflow Definition approver — global scope
    public bool IsHod           { get; set; }   // "DWO Register|Approve"
}

// The DWO half of the Common Dashboard. Populated exclusively from DWO data.
public class DwoDashboardVM
{
    // The DWO KPI cards. Keys come from DwoKpiKeys; a card the module does not return
    // renders with "—" instead of a count.
    public List<DwoKpiCardVM> Kpis { get; set; } = new();
    // Charts — every series is produced by the DWO module from the same scoped query as
    // the cards, and rendered in the DWO section only.
    public List<DwoSliceVM> ByStatus { get; set; } = new();          // every status with a count
    public List<DwoMonthPointVM> Trend { get; set; } = new();        // last 6 months: raised vs closed
    public List<DwoSliceVM> ByDepartment { get; set; } = new();      // busiest departments
    public List<DwoSliceVM> ByPriority { get; set; } = new();        // open discussions by priority
}

public class DwoSliceVM
{
    public string Label { get; set; } = "";
    public int Count { get; set; }
    public string Color { get; set; } = "#94a3b8";
    public string? Url { get; set; }
}

public class DwoMonthPointVM
{
    public string Label { get; set; } = "";
    public int Primary { get; set; }              // raised
    public int Secondary { get; set; }            // closed
}

// The DWO module's screen names as the Roles & Permissions matrix stores them.
//
// Declared HERE rather than read from RoleVMs.DwoScreens so the Common Dashboard depends on
// nothing outside its own files: a tree whose DwoScreens predates this dashboard has no
// such member, and referencing it fails the build. The strings are the matrix's, so the
// dashboard, the sidebar and the matrix still gate on identical values.
public static class DwoDashboardScreens
{
    public const string Register             = "DWO Register";
    public const string ManagementDiscussion = "Management Discussion";
    public const string Reports              = "DWO Reports";

    // Holding ANY of these means the user has some DWO access — what the dashboard's DWO
    // section keys off, the same rule the sidebar's DWO group uses.
    public static readonly string[] All = { Register, ManagementDiscussion, Reports };
}

// Canonical keys, display order, default labels and the DWO screen each card opens.
//
// The URLs are the DWO module's own routes, the same ones the sidebar links to
// (/DWO/Register, /DWO/MyDiscussions, /DWO/Reports). A card may override its Url; these are
// the fallbacks so the section still navigates correctly with no provider registered.
public static class DwoKpiKeys
{
    public const string DiscussionRegister   = "DiscussionRegister";
    public const string MyDiscussions        = "MyDiscussions";
    public const string ManagementDiscussion = "ManagementDiscussion";
    public const string PendingAction        = "PendingAction";
    public const string Closed               = "Closed";
    public const string Reports              = "Reports";

    public static readonly (string Key, string Label, string Accent, string Url)[] Ordered =
    {
        (DiscussionRegister,   "Discussion Register",   "#0f172a", "/DWO/Register"),
        (MyDiscussions,        "Discussion Point",      "#2563eb", "/DWO/MyDiscussions"),
        (ManagementDiscussion, "Management Discussion", "#0891b2", "/DWO/Register?FilterStatus=Management%20Discussion"),
        (PendingAction,        "Pending Action",        "#d97706", "/DWO/Register?FilterStatus=Submitted"),
        (Closed,               "Closed",                "#16a34a", "/DWO/Register?FilterStatus=Closed"),
        (Reports,              "Reports",               "#7c3aed", "/DWO/Reports"),
    };
}

public class DwoKpiCardVM
{
    // One of DwoKpiKeys.
    public string Key { get; set; } = "";
    // Optional label override; the dashboard falls back to the canonical label.
    public string? Label { get; set; }
    public int Count { get; set; }
    // The DWO screen this card opens WITH its filter already applied, supplied by the DWO
    // module because it owns its own routes. Empty falls back to DwoKpiKeys.Ordered.
    public string? Url { get; set; }
}

