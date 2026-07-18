namespace ApproveFlow.MVC.Models.Domain;

public class WorkflowNotificationRole
{
    [Key]
    public int Id { get; set; }
    public int WorkflowId { get; set; }
    public int StepLevel { get; set; }
    public int RoleId { get; set; }
    public int CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public int? ModifiedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public virtual WorkflowDefinition Workflow { get; set; } = null!;
    public virtual Role Role { get; set; } = null!;
}
