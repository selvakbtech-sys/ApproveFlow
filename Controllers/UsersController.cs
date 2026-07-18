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

    [HttpGet] public async Task<IActionResult> Create()
    {
        if (DenyIfNoPerm("User Management", Perm.Create) is { } r) return r;
        return View("Form", await _svc.BuildFormAsync(null));
    }
    [HttpGet] public async Task<IActionResult> Edit(int id)
    {
        if (DenyIfNoPerm("User Management", Perm.Edit) is { } r) return r;
        var vm = await _svc.BuildFormAsync(id);
        vm.CanEditUsername = UserRole == "Admin";
        return View("Form", vm);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(UserFormVM vm)
    {
        // Creating a user needs Create; editing an existing one needs Edit.
        if (DenyIfNoPerm("User Management", vm.UserId == 0 ? Perm.Create : Perm.Edit) is { } r) return r;
        // Recompute server-side: new users always set username; existing users
        // may only have it changed by an Administrator.
        vm.CanEditUsername = vm.UserId == 0 || UserRole == "Admin";
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
        if (DenyIfNoPerm("User Management", Perm.Edit) is { } r) return r;
        var (ok, err) = await _svc.ToggleStatusAsync(id, status);
        if (ok) Ok($"User {status}."); else Err(err!);
        return RedirectToAction("Index");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        if (DenyIfNoPerm("User Management", Perm.Delete) is { } r) return r;
        var (ok, err) = await _svc.DeleteAsync(id);
        if (ok) Ok("User deleted."); else Err(err!);
        return RedirectToAction("Index");
    }

    [HttpGet]
    public IActionResult ResetPassword(int id, string name)
    {
        if (DenyIfNoPerm("User Management", Perm.Edit) is { } r) return r;
        return View(new ResetPasswordVM { UserId = id, UserName = name });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(ResetPasswordVM vm)
    {
        if (DenyIfNoPerm("User Management", Perm.Edit) is { } r) return r;
        if (!ModelState.IsValid) return View(vm);
        await _auth.ResetPasswordAsync(vm.UserId, vm.NewPassword);
        Ok($"Password reset for {vm.UserName}."); return RedirectToAction("Index");
    }
}
