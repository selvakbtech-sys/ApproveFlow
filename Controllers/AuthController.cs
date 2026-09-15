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
        // An already-signed-in user carrying a returnUrl (e.g. the WhatsApp pending-approval
        // deep link opened in a browser that still holds a valid session) goes straight to the
        // page they asked for — only a bare /login falls back to the Dashboard.
        if (User.Identity?.IsAuthenticated == true)
            return GoBackOrDashboard(returnUrl);
        return View(new LoginVM { ReturnUrl = returnUrl });
    }

    // Single exit point for every authentication step: return the user to the page they
    // originally asked for, or the Dashboard when there was none. Url.IsLocalUrl is what makes
    // this safe — an absolute or off-site url is ignored rather than followed, so a crafted
    // ?returnUrl= cannot turn login into an open redirect.
    private IActionResult GoBackOrDashboard(string? returnUrl) =>
        !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? LocalRedirect(returnUrl)
            : RedirectToAction("Index", "Dashboard");

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
        // Identity + permission claims, built in one place (UserClaimsFactory) so that a
        // role edit can re-issue exactly the same set without a fresh sign-in.
        var principal = Helpers.UserClaimsFactory.BuildPrincipal(user);
        var authProps = new AuthenticationProperties { IsPersistent = vm.RememberMe };
        // "Remember me" grants a 14-day persistent session that survives browser restarts.
        // Without it, ExpiresUtc is left unset so the cookie follows the global 5-minute
        // ExpireTimeSpan (sliding) and expires on browser close. In both cases the
        // client-side idle timeout still signs the user out after 5 minutes of inactivity
        // while a tab is open.
        if (vm.RememberMe)
            authProps.ExpiresUtc = DateTimeOffset.UtcNow.AddDays(14);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, authProps);
        // A forced password change must not swallow the destination — hand the ReturnUrl on so
        // the user still reaches their deep link after changing it.
        if (user.MustChangePassword)
            return RedirectToAction("ChangePassword", new { returnUrl = vm.ReturnUrl });
        return GoBackOrDashboard(vm.ReturnUrl);
    }

    [Authorize, HttpGet("/logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Login");
    }

    [Authorize, HttpGet("/change-password")]
    public IActionResult ChangePassword(string? returnUrl = null) =>
        View(new ChangePasswordVM { ReturnUrl = returnUrl });

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
        return GoBackOrDashboard(vm.ReturnUrl);
    }
}
