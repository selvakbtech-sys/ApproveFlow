namespace ApproveFlow.MVC.Models.Domain;

public class Role
{
    [Key]
    public int RoleId { get; set; }
    public string RoleName { get; set; } = "";
    public string? Description { get; set; }
    public string BadgeColor { get; set; } = "badge-sky";
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public virtual ICollection<RolePermission> Permissions { get; set; } = new List<RolePermission>();
    public virtual ICollection<User> Users { get; set; } = new List<User>();
}
