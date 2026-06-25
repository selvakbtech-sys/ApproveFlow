namespace ApproveFlow.MVC.Models.Domain;

public class WhatsAppPendingClarification
{
    [Key]
    public int Id { get; set; }
    public string Phone { get; set; } = "";
    public int RequestId { get; set; }
    public int ApproverId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
