namespace ApproveFlow.MVC.Models.Domain;

public class User
{
    [Key]
    public int UserId { get; set; }
    public string FullName { get; set; } = "";
    public string Username { get; set; } = "";
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public int RoleId { get; set; }
    public string? AvatarInitials { get; set; }
    public string Status { get; set; } = "active";
    public bool MustChangePassword { get; set; }
    public string? WhatsAppNumber { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public virtual Role Role { get; set; } = null!;
    public virtual ICollection<UserLocation> UserLocations { get; set; } = new List<UserLocation>();
    public virtual ICollection<UserDepartment> UserDepartments { get; set; } = new List<UserDepartment>();
    public virtual ICollection<Request> SubmittedRequests { get; set; } = new List<Request>();
    public virtual ICollection<RequestApprover> ApprovalAssignments { get; set; } = new List<RequestApprover>();
}
