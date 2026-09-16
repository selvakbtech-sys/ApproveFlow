namespace ApproveFlow.MVC.Models.Domain;

// A Set is the bundle an HOD submits to Management: several Submitted discussion points,
// a supporting document pack, an optional summary file and a requested appointment date.
// The DWO number lives here — a point gets its number by joining the Set.
public class DwoSet
{
    [Key]
    public int SetId { get; set; }
    [MaxLength(30)]
    public string SetNumber { get; set; } = "";          // DWO101, DWO102, …
    public int DepartmentId { get; set; }

    // ── Appointment with Management ──
    public DateOnly RequestedAppointmentDate { get; set; }
    [MaxLength(1000)]
    public string? AppointmentNotes { get; set; }
    [MaxLength(30)]
    public string AppointmentStatus { get; set; } = DwoAppointmentStatus.Requested;
    public DateOnly? ScheduledDate { get; set; }

    public int SubmittedByUserId { get; set; }
    public DateTimeOffset SubmittedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public virtual Department Department { get; set; } = null!;
    public virtual User SubmittedByUser { get; set; } = null!;
    public virtual ICollection<DwoPoint> Points { get; set; } = new List<DwoPoint>();
    public virtual ICollection<DwoAttachment> Attachments { get; set; } = new List<DwoAttachment>();
    public virtual ICollection<DwoHistory> History { get; set; } = new List<DwoHistory>();
}
