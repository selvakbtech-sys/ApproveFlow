namespace ApproveFlow.MVC.Controllers;

[AllowAnonymous]
[Route("")]
public class AuthController : Controller
{
    private readonly IAuthService _auth;
    public AuthController(IAuthService auth) { _auth = auth; }

    [HttpGet("/login")]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToAction("Index", "Dashboard");
        return View(new LoginVM { ReturnUrl = returnUrl });
    }

    [HttpPost("/login"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginVM vm)
    {
        if (!ModelState.IsValid) return View(vm);
        var user = await _auth.ValidateAsync(vm.Email, vm.Password);
        if (user == null)
        {
            ModelState.AddModelError("", "Invalid email or password.");
            return View(vm);
        }
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.UserId.ToString()),
            new(ClaimTypes.Email,          user.Email),
            new(ClaimTypes.Name,           user.FullName),
            new(ClaimTypes.Role,           user.Role.RoleName),
            new("Avatar", user.AvatarInitials ?? user.FullName[..1].ToUpper()),
        };
        if (user.Role?.Permissions != null)
            foreach (var p in user.Role.Permissions.Where(p => p.CanView))
                claims.Add(new Claim("perm", p.ScreenName));
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal,
            new AuthenticationProperties
            {
                IsPersistent = vm.RememberMe,
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8)
            });
        if (user.MustChangePassword)
            return RedirectToAction("ChangePassword");
        return LocalRedirect(vm.ReturnUrl ?? "/");
    }

    [Authorize, HttpGet("/logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Login");
    }

    [Authorize, HttpGet("/change-password")]
    public IActionResult ChangePassword() => View(new ChangePasswordVM());

    [Authorize, HttpPost("/change-password"), ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(ChangePasswordVM vm)
    {
        if (!ModelState.IsValid) return View(vm);
        var uid = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        if (!await _auth.ChangePasswordAsync(uid, vm.CurrentPassword, vm.NewPassword))
        {
            ModelState.AddModelError("", "Current password is incorrect.");
            return View(vm);
        }
        TempData["success"] = "Password changed successfully.";
        return RedirectToAction("Index", "Dashboard");
    }
}
