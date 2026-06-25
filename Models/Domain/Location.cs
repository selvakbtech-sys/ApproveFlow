namespace ApproveFlow.MVC.Models.Domain;

public class Location
{
    [Key]
    public int LocationId { get; set; }
    public string Code { get; set; } = "";
    public string LocationName { get; set; } = "";
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public virtual ICollection<UserLocation> UserLocations { get; set; } = new List<UserLocation>();
}
