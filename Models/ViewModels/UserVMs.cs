namespace ApproveFlow.MVC.Models.ViewModels;

public class UserListVM
{
    public List<UserItemVM> Items { get; set; } = new();
    public string? FilterRole { get; set; }
    public string? FilterStatus { get; set; }
    public List<Role> Roles { get; set; } = new();
}

public class UserItemVM
{
    public int UserId { get; set; }
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string RoleName { get; set; } = "";
    public string BadgeColor { get; set; } = "";
    public List<string> Locations { get; set; } = new();
    public List<string> Departments { get; set; } = new();
    public string Status { get; set; } = "";
    public string? AvatarInitials { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public class UserFormVM
{
    public int UserId { get; set; }
    [Required, MaxLength(200)] public string FullName { get; set; } = "";
    [Required, EmailAddress]   public string Email { get; set; } = "";
    [MaxLength(20)] public string? WhatsAppNumber { get; set; }
    [MinLength(8, ErrorMessage = "Min 8 characters")]
    public string? Password { get; set; }
    [Required(ErrorMessage = "Please select a role")]
    public int RoleId { get; set; }
    public List<int> LocationIds { get; set; } = new();
    public List<int> DepartmentIds { get; set; } = new();
    public List<Role> Roles { get; set; } = new();
    public List<Location> Locations { get; set; } = new();
    public List<Department> Departments { get; set; } = new();
}

public class ResetPasswordVM
{
    public int UserId { get; set; }
    public string UserName { get; set; } = "";
    [Required, MinLength(8)] public string NewPassword { get; set; } = "";
    [Required, Compare(nameof(NewPassword), ErrorMessage = "Passwords do not match")]
    public string ConfirmPassword { get; set; } = "";
}
