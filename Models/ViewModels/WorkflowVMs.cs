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
    public List<User> Approvers { get; set; } = new();
    public static readonly string[] Priorities = { "Low", "Medium", "High", "Critical" };
}

public class WfStepFormVM
{
    public int StepLevel { get; set; }
    public int? ApproverUserId { get; set; }
    public string ApproverRole { get; set; } = "";
    public bool IsAutoAssignDeptManager { get; set; }
}
