namespace ApproveFlow.MVC.Models.ViewModels;

public class RoleListVM
{
    public List<RoleItemVM> Items { get; set; } = new();
}

public class RoleItemVM
{
    public int RoleId { get; set; }
    public string RoleName { get; set; } = "";
    public string? Description { get; set; }
    public string BadgeColor { get; set; } = "";
    public bool IsActive { get; set; }
    public int UserCount { get; set; }
    public List<RolePermission> Permissions { get; set; } = new();
    public DateTimeOffset CreatedAt { get; set; }
}

public class RoleFormVM
{
    public int RoleId { get; set; }
    [Required, MaxLength(100)] public string RoleName { get; set; } = "";
    public string? Description { get; set; }
    [Required] public string BadgeColor { get; set; } = "badge-sky";
    public bool IsActive { get; set; } = true;
    public List<PermRowVM> Permissions { get; set; } = new();

    public static readonly string[] AllScreens =
    {
        "Dashboard", "Requests", "New Request", "User Management", "User Mapping",
        "Roles & Permissions", "Workflow Definition",
        "Locations", "Departments", "Request Types",
        // ── DWO module ──
        // Approve carries the module's role meaning: Approve on "DWO Register" makes a
        // user an HOD for their mapped departments. Management is not a matrix row — it is
        // the top approval level of the Workflow Definitions. "Discussion Point" gates the
        // requestor screens (My Discussions, raise/edit/submit/withdraw); "Sets" gates the
        // Sets screens. See DwoScreens.
        DwoScreens.Points, DwoScreens.Sets, DwoScreens.Register, DwoScreens.Reports, DwoScreens.Companies
    };
    public static readonly string[] ReportScreens = { "Dashboard" };
}

public class PermRowVM
{
    public string ScreenName { get; set; } = "";
    public bool CanView { get; set; }
    public bool CanCreate { get; set; }
    public bool CanEdit { get; set; }
    public bool CanApprove { get; set; }
    public bool CanDelete { get; set; }
    public bool IsReport => Array.IndexOf(RoleFormVM.ReportScreens, ScreenName) >= 0;
}
