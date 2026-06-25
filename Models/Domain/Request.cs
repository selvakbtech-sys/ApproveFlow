namespace ApproveFlow.MVC.Models.Domain;

public class Request
{
    [Key]
    public int RequestId { get; set; }
    public string RequestNumber { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string? Notes { get; set; }
    public int RequestTypeId { get; set; }
    public string Priority { get; set; } = "Medium";
    [Column(TypeName = "decimal(18,2)")]
    public decimal? Amount { get; set; }
    public int LocationId { get; set; }
    public int DepartmentId { get; set; }
    public int RequestorId { get; set; }
    public int? WorkflowId { get; set; }
    public string Status { get; set; } = "Draft";
    public int CurrentStepLevel { get; set; } = 1;
    public DateTimeOffset? SubmittedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public virtual RequestType RequestType { get; set; } = null!;
    public virtual Location Location { get; set; } = null!;
    public virtual Department Department { get; set; } = null!;
    public virtual User Requestor { get; set; } = null!;
    public virtual WorkflowDefinition? Workflow { get; set; }
    public virtual ICollection<RequestApprover> Approvers { get; set; } = new List<RequestApprover>();
    public virtual ICollection<RequestActionLog> ActionLogs { get; set; } = new List<RequestActionLog>();
    public virtual ICollection<RequestDocument> Documents { get; set; } = new List<RequestDocument>();
}
