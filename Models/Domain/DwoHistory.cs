namespace ApproveFlow.MVC.Models.Domain;

// Immutable audit trail. One row per action on a point or a Set; nothing in the module
// ever updates or deletes a row here. ActorName is denormalised so entries written on
// behalf of the system (a reassignment clone) still read correctly.
public class DwoHistory
{
    [Key]
    public int HistoryId { get; set; }
    public int? PointId { get; set; }
    public int? SetId { get; set; }
    public int? ActorUserId { get; set; }
    [MaxLength(150)]
    public string ActorName { get; set; } = "";
    [MaxLength(120)]
    public string Action { get; set; } = "";
    [MaxLength(2000)]
    public string? Note { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public virtual DwoPoint? Point { get; set; }
    public virtual DwoSet? Set { get; set; }
    public virtual User? ActorUser { get; set; }
}
