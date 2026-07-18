namespace ApproveFlow.MVC.Models.Domain;

public class WorkflowDefinition
{
    [Key]
    public int WorkflowId { get; set; }
    public string WorkflowName { get; set; } = "";
    public int? LocationId { get; set; }
    public int? RequestTypeId { get; set; }
    public string? Priority { get; set; }
    public int? DepartmentId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public virtual Location? Location { get; set; }
    public virtual RequestType? RequestType { get; set; }
    public virtual Department? Department { get; set; }
    public virtual ICollection<WorkflowStep> Steps { get; set; } = new List<WorkflowStep>();
    public virtual ICollection<WorkflowNotificationRole> NotificationRoles { get; set; } = new List<WorkflowNotificationRole>();
}
