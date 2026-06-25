namespace ApproveFlow.MVC.Models.Domain;

public class RequestActionLog
{
    [Key]
    public int ActionLogId { get; set; }
    public int RequestId { get; set; }
    public int ActorUserId { get; set; }
    public string Action { get; set; } = "";
    public string? Comment { get; set; }
    public int? StepLevel { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public virtual Request Request { get; set; } = null!;
    public virtual User ActorUser { get; set; } = null!;
}
