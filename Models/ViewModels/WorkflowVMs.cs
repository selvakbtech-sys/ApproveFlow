namespace ApproveFlow.MVC.Models.ViewModels;

public class WorkflowListVM
{
    public List<WorkflowItemVM> Items { get; set; } = new();
    public string FilterActive { get; set; } = "all";
}

public class WorkflowItemVM
{
    public int WorkflowId { get; set; }
    public string WorkflowName { get; set; } = "";
    public string Location { get; set; } = "Any";
    public string RequestType { get; set; } = "Any";
    public string Priority { get; set; } = "Any";
    public string Department { get; set; } = "Any";
    public bool IsActive { get; set; }
    public List<WfStepVM> Steps { get; set; } = new();
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public class WfStepVM
{
    public int StepLevel { get; set; }
    public string ApproverName { get; set; } = "";
    public string ApproverRole { get; set; } = "";
}

public class WorkflowFormVM
{
    public int WorkflowId { get; set; }
    [Required, MaxLength(200)] public string WorkflowName { get; set; } = "";
    public int? LocationId { get; set; }
    public int? RequestTypeId { get; set; }
    public string? Priority { get; set; }
    public int? DepartmentId { get; set; }
    public bool IsActive { get; set; } = true;
    [MinLength(1, ErrorMessage = "At least one approval step is required")]
    public List<WfStepFormVM> Steps { get; set; } = new();
    public List<Location> Locations { get; set; } = new();
    public List<RequestType> RequestTypes { get; set; } = new();
    public List<Department> Departments { get; set; } = new();
    // Roles that carry the "Approve Request" permission — the source for the
    // Approval Steps role dropdown. Driven by Roles & Permissions, not by users
    // or the ApprovalSteps table, so removing a role's approve right hides it here.
    public List<Role> ApproverRoles { get; set; } = new();
    public List<Role> AllRoles { get; set; } = new();
    public static readonly string[] Priorities = { "Low", "Medium", "High", "Critical" };
}

public class WfStepFormVM
{
    public int StepLevel { get; set; }
    public int? ApproverUserId { get; set; }
    public int? ApproverRoleId { get; set; }
    public string ApproverRole { get; set; } = "";
    public bool IsAutoAssignDeptManager { get; set; }
    public bool IsWhatsAppEnabled { get; set; } = true;
    public List<int> NotificationRoleIds { get; set; } = new();
}
