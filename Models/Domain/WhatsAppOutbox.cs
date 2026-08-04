namespace ApproveFlow.MVC.Models.Domain;

// Durable resend queue for outbound WhatsApp messages that failed at the TRANSPORT layer
// (e.g. a TLS reset to graph.facebook.com). The exact HTTP body is stored so the background
// WhatsAppOutboxService can re-POST it verbatim once connectivity recovers, without blocking
// the user action that originally triggered the send. Only connection-phase failures — which
// never reached Meta — are queued here; a real API rejection (non-2xx) is never re-queued.
public class WhatsAppOutbox
{
    [Key]
    public int Id { get; set; }

    public DateTimeOffset CreatedDate { get; set; }

    // Sender endpoint captured at enqueue time. The bearer token is deliberately NOT stored;
    // the resender reads it fresh from configuration on every attempt.
    public string PhoneNumberId { get; set; } = "";
    public string ApiVersion { get; set; } = "";

    public string ToNumber { get; set; } = "";

    // The exact JSON body to POST to the Graph API, as originally serialized in the send path.
    public string PayloadJson { get; set; } = "";

    // Context carried so the delivered message is written to WhatsAppMessageHistory on success,
    // identically to a first-attempt send.
    public string MessageType { get; set; } = "";
    public int? RequestId { get; set; }
    public int? WorkflowId { get; set; }
    public int? WorkflowLevel { get; set; }
    public int? CreatedBy { get; set; }
    public string MessageText { get; set; } = "";

    // "Pending" (holding for resend), "Sent" (delivered), "Abandoned" (gave up after max attempts
    // or a real API rejection).
    public string Status { get; set; } = "Pending";

    public int Attempts { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public string? LastError { get; set; }
}
