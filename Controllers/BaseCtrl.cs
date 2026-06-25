namespace ApproveFlow.MVC.Controllers;

[Authorize]
public abstract class BaseCtrl : Controller
{
    protected int UserId =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;
    protected string UserRole =>
        User.FindFirstValue(ClaimTypes.Role) ?? "";
    protected void Ok(string msg) => TempData["success"] = msg;
    protected void Err(string msg) => TempData["error"] = msg;
    protected bool HasPerm(string screen) =>
        UserRole == "Admin" ||
        User.Claims.Any(c => c.Type == "perm" && c.Value == screen);
    protected IActionResult? DenyIfNoPerm(string screen)
    {
        if (!HasPerm(screen)) { Err("You do not have permission to access that page."); return RedirectToAction("Index", "Dashboard"); }
        return null;
    }
}
