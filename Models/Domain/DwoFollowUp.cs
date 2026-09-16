namespace ApproveFlow.MVC.Models.Domain;

// A follow-up Management schedules on a point. Completing it sends the point back to
// Management Discussion; the row is kept for the audit trail, so a point can carry more
// than one over its life. The open one is the newest with CompletedAt == null.
public class DwoFollowUp
{
    [Key]
    public int FollowUpId { get; set; }
    public int PointId { get; set; }
    public int OwnerUserId { get; set; }
    public DateOnly DueDate { get; set; }
    [MaxLength(2000)]
    public string? Notes { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }
    public int? CompletedByUserId { get; set; }
    [MaxLength(2000)]
    public string? CompletionNote { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public virtual DwoPoint Point { get; set; } = null!;
    public virtual User OwnerUser { get; set; } = null!;
    public virtual User? CompletedByUser { get; set; }

    [NotMapped]
    public bool IsOverdue =>
        CompletedAt == null && DueDate < DateOnly.FromDateTime(IstClock.Now.DateTime);
}
