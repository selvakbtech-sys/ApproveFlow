namespace ApproveFlow.MVC.Models.Domain;

public class RequestApprover
{
    [Key]
    public int ApproverId { get; set; }
    public int RequestId { get; set; }
    public int StepLevel { get; set; }
    public int ApproverUserId { get; set; }
    public string Status { get; set; } = "waiting";
    public string? ActionText { get; set; }
    public string? Comment { get; set; }
    public DateTimeOffset? ActionAt { get; set; }
    public virtual Request Request { get; set; } = null!;
    public virtual User ApproverUser { get; set; } = null!;
}
