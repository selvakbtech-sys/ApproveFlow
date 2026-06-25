namespace ApproveFlow.MVC.Models.Domain;

public class RolePermission
{
    [Key]
    public int PermissionId { get; set; }
    public int RoleId { get; set; }
    public string ScreenName { get; set; } = "";
    public bool CanView { get; set; }
    public bool CanCreate { get; set; }
    public bool CanEdit { get; set; }
    public bool CanApprove { get; set; }
    public bool CanDelete { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public virtual Role Role { get; set; } = null!;
}
