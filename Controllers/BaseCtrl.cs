namespace ApproveFlow.MVC.Controllers;

[Authorize]
public abstract class BaseCtrl : Controller
{
    // Canonical action names — must match the granular claim values emitted at login
    // (see AuthController) and the RolePermission flags (CanView/CanCreate/…).
    // Nested in Perm so they don't collide with controller action methods named
    // Create/Edit/Delete or the built-in Controller.View() method.
    protected static class Perm
    {
        public const string View    = "View";
        public const string Create  = "Create";
        public const string Edit    = "Edit";
        public const string Delete  = "Delete";
        public const string Approve = "Approve";
    }

    protected int UserId =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;
    protected string UserRole =>
        User.FindFirstValue(ClaimTypes.Role) ?? "";
    protected void Ok(string msg) => TempData["success"] = msg;
    protected void Err(string msg) => TempData["error"] = msg;

    // View-level permission (screen visibility). Admin sees everything.
    protected bool HasPerm(string screen) =>
        UserRole == "Admin" ||
        User.Claims.Any(c => c.Type == "perm" && c.Value == screen);

    // Granular permission for a specific action on a screen. Admin can do everything.
    // Backed by "permx" claims of the form "<ScreenName>|<Action>" issued at login.
    protected bool HasPerm(string screen, string action) =>
        UserRole == "Admin" ||
        User.Claims.Any(c => c.Type == "permx" && c.Value == $"{screen}|{action}");

    // Guard for a page/screen View permission. Returns an HTTP 403 result when the
    // user is not allowed; null when allowed. Usage:
    //   if (DenyIfNoPerm("Requests") is { } r) return r;
    protected IActionResult? DenyIfNoPerm(string screen) =>
        HasPerm(screen) ? null : Deny();

    // Guard for a specific action (Create/Edit/Delete/Approve/View) on a screen.
    protected IActionResult? DenyIfNoPerm(string screen, string action) =>
        HasPerm(screen, action) ? null : Deny();

    // Standard authorization failure: HTTP 403 + the AccessDenied page which shows
    // "You do not have permission to perform this action."
    protected IActionResult Deny() =>
        new ViewResult
        {
            ViewName   = "AccessDenied",
            ViewData   = ViewData,
            TempData   = TempData,
            StatusCode = StatusCodes.Status403Forbidden,
        };
}
