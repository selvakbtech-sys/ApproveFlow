namespace ApproveFlow.MVC.Models.Domain;

// Record-wise remark history for a discussion point. One append-only thread per point,
// split by the role that wrote it (Requester / HOD / Management). Nothing in the module
// ever updates or deletes a row here — DwoPoint.ReviewRemarks only mirrors the latest
// entry for the status banners.
public class DwoRemark
{
    [Key]
    public int RemarkId { get; set; }
    public int PointId { get; set; }
    [MaxLength(20)]
    public string RoleKind { get; set; } = "";          // DwoRoleKind
    public int? UserId { get; set; }
    [MaxLength(150)]
    public string UserName { get; set; } = "";
    [MaxLength(40)]
    public string Action { get; set; } = "";            // DwoRemarkAction
    [MaxLength(2000)]
    public string Remark { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }

    public virtual DwoPoint Point { get; set; } = null!;
    public virtual User? User { get; set; }
}
