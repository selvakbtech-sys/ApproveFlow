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
        var user = await _auth.ValidateAsync(vm.Username, vm.Password);
        if (user == null)
        {
            ModelState.AddModelError("", "Invalid username or password.");
            return View(vm);
        }
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.UserId.ToString()),
            new("Username",                user.Username),
            new(ClaimTypes.Name,           user.FullName),
            new(ClaimTypes.Role,           user.Role.RoleName),
            new("Avatar", user.AvatarInitials ?? user.FullName[..1].ToUpper()),
        };
        // Email is optional now; only add the claim when present.
        if (!string.IsNullOrWhiteSpace(user.Email))
            claims.Add(new Claim(ClaimTypes.Email, user.Email));
        // Permission claims. "perm" = screen-level View (drives menu visibility and
        // page access). "permx" = granular per-action rights ("<Screen>|<Action>"),
        // enforced on every controller action. Both are read back in BaseCtrl.HasPerm.
        if (user.Role?.Permissions != null)
            foreach (var p in user.Role.Permissions)
            {
                if (p.CanView)    { claims.Add(new Claim("perm", p.ScreenName));
                                    claims.Add(new Claim("permx", $"{p.ScreenName}|View")); }
                if (p.CanCreate)  claims.Add(new Claim("permx", $"{p.ScreenName}|Create"));
                if (p.CanEdit)    claims.Add(new Claim("permx", $"{p.ScreenName}|Edit"));
                if (p.CanDelete)  claims.Add(new Claim("permx", $"{p.ScreenName}|Delete"));
                if (p.CanApprove) claims.Add(new Claim("permx", $"{p.ScreenName}|Approve"));
            }
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
        var authProps = new AuthenticationProperties { IsPersistent = vm.RememberMe };
        // "Remember me" grants a 14-day persistent session that survives browser restarts.
        // Without it, ExpiresUtc is left unset so the cookie follows the global 5-minute
        // ExpireTimeSpan (sliding) and expires on browser close. In both cases the
        // client-side idle timeout still signs the user out after 5 minutes of inactivity
        // while a tab is open.
        if (vm.RememberMe)
            authProps.ExpiresUtc = DateTimeOffset.UtcNow.AddDays(14);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, authProps);
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
