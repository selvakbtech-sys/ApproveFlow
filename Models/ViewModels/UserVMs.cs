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
    public string Username { get; set; } = "";
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

    [Required(ErrorMessage = "Username is required")]
    [StringLength(30, MinimumLength = 4, ErrorMessage = "Username must be 4 to 30 characters")]
    [RegularExpression(@"^[A-Za-z0-9_.]+$",
        ErrorMessage = "Username may contain only letters, numbers, underscore (_) and period (.).")]
    [Display(Name = "Username")]
    public string Username { get; set; } = "";

    // Optional — used for notifications only, no longer used for login.
    [EmailAddress, MaxLength(256)] public string? Email { get; set; }

    // Whether the Username field may be edited (Administrator only, on edit).
    public bool CanEditUsername { get; set; } = true;

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
