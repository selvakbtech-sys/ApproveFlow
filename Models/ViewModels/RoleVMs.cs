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

    // Every screen the Roles & Permissions matrix renders, in display order. A screen NOT
    // listed here can never be granted, because the matrix is the only place rows are
    // written — so any name guarded by DenyIfNoPerm/HasPerm/CanView must appear here.
    public static readonly string[] AllScreens =
    {
        // ApproveFlow
        "Dashboard", "Requests", "New Request", "User Management", "User Mapping",
        "Roles & Permissions", "Workflow Definition",
        "Locations", "Departments", "Request Types",
        // DWO — an independent module, permissioned separately from ApproveFlow. Listing the
        // screens here is what lets a role be granted DWO access at all; the DWO module
        // enforces them the same way ApproveFlow does.
        DwoScreens.Register, DwoScreens.ManagementDiscussion, DwoScreens.Reports,
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

// The DWO module's screen names as the Roles & Permissions matrix stores them. Named here so
// the matrix, the sidebar and the Common Dashboard's DWO section all gate on the same
// strings rather than three separately-typed literals that can drift apart.
public static class DwoScreens
{
    public const string Register             = "DWO Register";
    public const string ManagementDiscussion = "Management Discussion";
    public const string Reports              = "DWO Reports";

    // Holding ANY of these means the user has some DWO access — what the sidebar group
    // heading and the dashboard's DWO section key off.
    public static readonly string[] All = { Register, ManagementDiscussion, Reports };
}
