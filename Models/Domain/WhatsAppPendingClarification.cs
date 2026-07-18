namespace ApproveFlow.MVC.Models.Domain;

public class WhatsAppPendingClarification
{
    [Key]
    public int Id { get; set; }
    public string Phone { get; set; } = "";
    public int RequestId { get; set; }
    public int ApproverId { get; set; }
    // Which workflow action the approver's next text reply belongs to:
    // "approved" | "rejected" | "clarification". Lets one staging table drive all
    // three reply flows instead of being clarification-only.
    [MaxLength(20)]
    public string Action { get; set; } = "clarification";
    public DateTimeOffset CreatedAt { get; set; }

    // ── Deferred-approve session fields (Action = "approved" only) ──────────────
    // A deferred approve is an independent session PER REQUEST: the approver taps
    // Approve, the session opens for WhatsApp:ApprovalReplyWindowSeconds, and the
    // approval executes on reply or on timeout. Any number may be open at once for
    // the same approver, so a reply is matched by message id — never by phone alone.
    // Unused by the Reject / Clarification flows, which are unchanged.

    // wamid of the optional-remark prompt we send. A swipe-reply to it arrives with
    // this value as context.id — the authoritative link back to THIS request.
    // 200 matches WhatsAppMessageHistory.MessageId, the other store of a wamid.
    [MaxLength(200)]
    public string? WhatsAppMessageId { get; set; }

    // wamid of the interactive approval card the Approve button belongs to, so a reply
    // to the ORIGINAL card (not just our prompt) also resolves to THIS request.
    [MaxLength(200)]
    public string? ParentMessageId { get; set; }

    // The optional remark captured from the in-window reply (null on auto-approve).
    public string? ApprovalRemark { get; set; }

    // "WaitingForReply" → "Completed" (approval executed) | "Expired" (window elapsed
    // and the approval could not execute — e.g. the step was actioned elsewhere).
    // The WaitingForReply → Completed transition is the atomic exactly-once claim.
    [MaxLength(20)]
    public string Status { get; set; } = "WaitingForReply";

    public DateTimeOffset? ExpiresOn { get; set; }
    public DateTimeOffset? CompletedOn { get; set; }
}
