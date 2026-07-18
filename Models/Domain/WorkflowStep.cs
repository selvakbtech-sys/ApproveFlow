namespace ApproveFlow.MVC.Models.Domain;

public class WorkflowStep
{
    [Key]
    public int StepId { get; set; }
    public int WorkflowId { get; set; }
    public int StepLevel { get; set; }
    // Role-based approver: the step is defined by a role; the concrete approver
    // user is resolved at submit time from active users holding this role.
    public int? ApproverRoleId { get; set; }
    // Legacy user-pinned approver, kept for backward compatibility with workflows
    // created before the role-based change.
    public int? ApproverUserId { get; set; }
    public bool IsAutoAssignDeptManager { get; set; }
    public virtual WorkflowDefinition Workflow { get; set; } = null!;
    public virtual Role? ApproverRole { get; set; }
    public virtual User? ApproverUser { get; set; }
}
