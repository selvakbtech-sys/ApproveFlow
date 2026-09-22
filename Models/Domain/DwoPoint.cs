namespace ApproveFlow.MVC.Models.Domain;

// A DWO discussion point — the record a Team Member raises, an HOD reviews and bundles
// into a Set, and Management closes, holds, follows up or reassigns.
public class DwoPoint
{
    [Key]
    public int PointId { get; set; }
    [MaxLength(30)]
    public string PointNumber { get; set; } = "";        // HR-1024 — department code + running number
    [MaxLength(60)]
    public string? TaskId { get; set; }                  // optional external task reference
    [MaxLength(250)]
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    [MaxLength(60)]
    public string Category { get; set; } = "General";
    [MaxLength(20)]
    public string Priority { get; set; } = "Medium";

    public int CompanyId { get; set; }
    public int LocationId { get; set; }                  // Site / cost centre
    public int DepartmentId { get; set; }

    // Null only for a point created by a reassignment and not yet claimed by anyone in the
    // target department — the first user there to edit or submit it claims it.
    public int? RaisedByUserId { get; set; }
    [MaxLength(30)]
    public string Status { get; set; } = DwoStatus.Draft;

    public int? SetId { get; set; }
    public int? OriginPointId { get; set; }              // the point this one was reassigned from

    public int? ReviewedByUserId { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    [MaxLength(2000)]
    public string? ReviewRemarks { get; set; }
    // While on Hold: the status to return to on Resume (Submitted for an HOD hold, which
    // keeps the point in the HOD queue; Management Discussion for a Management hold).
    [MaxLength(30)]
    public string? HeldFromStatus { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public virtual Company Company { get; set; } = null!;
    public virtual Location Location { get; set; } = null!;
    public virtual Department Department { get; set; } = null!;
    public virtual User? RaisedByUser { get; set; }
    public virtual User? ReviewedByUser { get; set; }
    public virtual DwoSet? Set { get; set; }
    public virtual DwoPoint? OriginPoint { get; set; }
    public virtual ICollection<DwoAttachment> Attachments { get; set; } = new List<DwoAttachment>();
    public virtual ICollection<DwoReferenceLink> ReferenceLinks { get; set; } = new List<DwoReferenceLink>();
    public virtual ICollection<DwoFollowUp> FollowUps { get; set; } = new List<DwoFollowUp>();
    public virtual ICollection<DwoHistory> History { get; set; } = new List<DwoHistory>();
    public virtual ICollection<DwoRemark> Remarks { get; set; } = new List<DwoRemark>();
}
