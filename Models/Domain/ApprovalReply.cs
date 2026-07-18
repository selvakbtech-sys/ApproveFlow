namespace ApproveFlow.MVC.Models.Domain;

// Metadata for a post-approval reply an approver sends back through the WhatsApp
// conversation ("Approval recorded. Type a reply…"). Stores only metadata — the
// file itself lives under wwwroot/uploads/whatsapp/. A reply may carry a text
// message, an attachment, or both.
public class ApprovalReply
{
    [Key]
    public int ApprovalReplyId { get; set; }
    public int ApprovalId { get; set; }          // RequestApprover.ApproverId (the approved step)
    public int RequestId { get; set; }
    public string? ReplyMessage { get; set; }
    public string? AttachmentFileName { get; set; }
    public string? AttachmentUrl { get; set; }
    public string? WhatsAppMessageId { get; set; }
    public DateTimeOffset CreatedDate { get; set; }
    public int CreatedBy { get; set; }           // approver user id
    public virtual Request Request { get; set; } = null!;
}
