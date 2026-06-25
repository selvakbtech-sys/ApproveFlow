namespace ApproveFlow.MVC.Controllers;

public class UsersController : BaseCtrl
{
    private readonly IUserService _svc;
    private readonly IAuthService _auth;
    public UsersController(IUserService svc, IAuthService auth) { _svc = svc; _auth = auth; }

    public async Task<IActionResult> Index(string? role, string? status)
    {
        if (DenyIfNoPerm("User Management") is { } ru) return ru;
        return View(await _svc.GetListAsync(role, status));
    }

    [HttpGet] public async Task<IActionResult> Create() => View("Form", await _svc.BuildFormAsync(null));
    [HttpGet] public async Task<IActionResult> Edit(int id) => View("Form", await _svc.BuildFormAsync(id));

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(UserFormVM vm)
    {
        if (!ModelState.IsValid)
        {
            var rebuilt = await _svc.BuildFormAsync(vm.UserId == 0 ? null : vm.UserId);
            vm.Roles = rebuilt.Roles; vm.Locations = rebuilt.Locations; vm.Departments = rebuilt.Departments;
            return View("Form", vm);
        }
        var (ok, err) = await _svc.SaveAsync(vm);
        if (!ok) { Err(err!); var r2 = await _svc.BuildFormAsync(vm.UserId == 0 ? null : vm.UserId); vm.Roles = r2.Roles; vm.Locations = r2.Locations; vm.Departments = r2.Departments; return View("Form", vm); }
        Ok(vm.UserId == 0 ? "User created." : "User updated.");
        return RedirectToAction("Index");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleStatus(int id, string status)
    {
        var (ok, err) = await _svc.ToggleStatusAsync(id, status);
        if (ok) Ok($"User {status}."); else Err(err!);
        return RedirectToAction("Index");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var (ok, err) = await _svc.DeleteAsync(id);
        if (ok) Ok("User deleted."); else Err(err!);
        return RedirectToAction("Index");
    }

    [HttpGet]
    public IActionResult ResetPassword(int id, string name)
        => View(new ResetPasswordVM { UserId = id, UserName = name });

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(ResetPasswordVM vm)
    {
        if (!ModelState.IsValid) return View(vm);
        await _auth.ResetPasswordAsync(vm.UserId, vm.NewPassword);
        Ok($"Password reset for {vm.UserName}."); return RedirectToAction("Index");
    }
}
