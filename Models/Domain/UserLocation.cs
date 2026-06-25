namespace ApproveFlow.MVC.Models.Domain;

public class UserLocation
{
    [Key]
    public int UserLocationId { get; set; }
    public int UserId { get; set; }
    public int LocationId { get; set; }
    public virtual User User { get; set; } = null!;
    public virtual Location Location { get; set; } = null!;
}
