namespace ApproveFlow.MVC.Models.ViewModels;

public class LoginVM
{
    [Required(ErrorMessage = "Username is required")]
    [Display(Name = "Username")]
    public string Username { get; set; } = "";
    [Required]
    public string Password { get; set; } = "";
    public bool RememberMe { get; set; }
    public string? ReturnUrl { get; set; }
}

public class ChangePasswordVM
{
    [Required] public string CurrentPassword { get; set; } = "";
    [Required, MinLength(8)] public string NewPassword { get; set; } = "";
    [Required, Compare(nameof(NewPassword), ErrorMessage = "Passwords do not match")]
    public string ConfirmPassword { get; set; } = "";
    // Carried through the forced-password-change detour so a user who arrived from a deep
    // link (e.g. the WhatsApp pending-approval link) still lands on that page afterwards
    // instead of the Dashboard. Empty for a normal Change Password visit.
    public string? ReturnUrl { get; set; }
}
