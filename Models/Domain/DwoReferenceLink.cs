namespace ApproveFlow.MVC.Models.Domain;

// One reference URL on a discussion point. Rows are replaced wholesale whenever the point
// is saved, so there is no update path.
public class DwoReferenceLink
{
    [Key]
    public int LinkId { get; set; }
    public int PointId { get; set; }
    [MaxLength(1000)]
    public string Url { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }

    public virtual DwoPoint Point { get; set; } = null!;
}
