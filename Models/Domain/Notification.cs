namespace ApproveFlow.MVC.Models.Domain;

public class Notification
{
    [Key]
    public int NotificationId { get; set; }
    public int UserId { get; set; }
    public int? RequestId { get; set; }
    public string NotificationType { get; set; } = "";
    public string Message { get; set; } = "";
    public bool IsRead { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ReadAt { get; set; }
    public virtual User User { get; set; } = null!;
    public virtual Request? Request { get; set; }
}
