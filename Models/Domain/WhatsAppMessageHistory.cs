namespace ApproveFlow.MVC.Models.Domain;

// Durable log of every WhatsApp message exchanged for a request — both outbound
// notifications (Approve / Reject / Clarification, etc.) and inbound approver replies.
// Written centrally: outbound from WhatsAppService.PostToWhatsAppAsync, inbound from
// WhatsAppService.LogInboundMessageAsync (called by the webhook).
public class WhatsAppMessageHistory
{
    [Key]
    public int Id { get; set; }

    // Nullable: most messages belong to a request, but broadcast messages
    // (e.g. aging reminders that span many requests) may not.
    public int? RequestId { get; set; }
    public int? WorkflowId { get; set; }

    // Approval level this message relates to (1 = L1, 2 = L2, ...). Null for
    // messages not tied to a specific level (e.g. aging reminders).
    public int? WorkflowLevel { get; set; }

    // UserId of the approver who acted / the recipient this row is attributed to.
    public int? CreatedBy { get; set; }

    // WhatsApp/Meta message id (wamid...) returned by the Graph API for outbound,
    // or supplied by the webhook for inbound. Null when the send failed.
    public string? MessageId { get; set; }

    public string MobileNumber { get; set; } = "";

    // "Outbound" (app → WhatsApp) or "Inbound" (WhatsApp → app).
    public string Direction { get; set; } = "";

    // Semantic type: workflow action ("approved" / "rejected" / "clarification"),
    // "template:<name>", or "text".
    public string MessageType { get; set; } = "";

    public string MessageText { get; set; } = "";

    // "Sent" / "Failed" for outbound, "Received" for inbound.
    public string Status { get; set; } = "";

    public DateTimeOffset CreatedDate { get; set; }
}
