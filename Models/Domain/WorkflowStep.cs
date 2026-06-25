namespace ApproveFlow.MVC.Models.Domain;

public class WorkflowStep
{
    [Key]
    public int StepId { get; set; }
    public int WorkflowId { get; set; }
    public int StepLevel { get; set; }
    public int? ApproverUserId { get; set; }
    public bool IsAutoAssignDeptManager { get; set; }
    public virtual WorkflowDefinition Workflow { get; set; } = null!;
    public virtual User? ApproverUser { get; set; }
}
