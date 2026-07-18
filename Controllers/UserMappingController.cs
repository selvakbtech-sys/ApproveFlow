namespace ApproveFlow.MVC.Controllers;

public class UserMappingController : BaseCtrl
{
    private readonly IUserMappingService _svc;
    public UserMappingController(IUserMappingService svc) { _svc = svc; }

    public async Task<IActionResult> Index(string? search, string? status)
    {
        if (DenyIfNoPerm("User Mapping") is { } r) return r;
        return View(await _svc.GetListAsync(search, status));
    }

    [HttpGet]
    public async Task<IActionResult> Create()
    {
        if (DenyIfNoPerm("User Mapping", Perm.Create) is { } r) return r;
        var vm = await _svc.BuildFormAsync(null);
        vm.IsCreate = true;
        return View("Form", vm);
    }

    // AJAX: returns whether the given userId already has a mapping (for client-side duplicate check).
    [HttpGet]
    public async Task<IActionResult> CheckUser(int userId)
    {
        if (DenyIfNoPerm("User Mapping") is { } r) return r;
        if (userId <= 0) return Json(new { exists = false, editUrl = (string?)null });
        var exists = await _svc.UserMappingExistsAsync(userId);
        var editUrl = exists ? Url.Action("Edit", "UserMapping", new { userId }) : null;
        return Json(new { exists, editUrl });
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int userId)
    {
        if (DenyIfNoPerm("User Mapping", Perm.Edit) is { } r) return r;
        var vm = await _svc.BuildFormAsync(userId);
        if (vm.UserId == 0) { Err("Mapping not found."); return RedirectToAction("Index"); }
        return View("Form", vm);
    }

    [HttpGet]
    public async Task<IActionResult> Details(int userId)
    {
        if (DenyIfNoPerm("User Mapping") is { } r) return r;
        var vm = await _svc.BuildFormAsync(userId);
        if (vm.UserId == 0) { Err("Mapping not found."); return RedirectToAction("Index"); }
        return View("Details", vm);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(UserMappingFormVM vm)
    {
        // Creating a new mapping needs Create; updating an existing one needs Edit.
        bool isEdit = await _svc.UserMappingExistsAsync(vm.UserId);
        if (DenyIfNoPerm("User Mapping", isEdit ? Perm.Edit : Perm.Create) is { } dr) return dr;

        if (vm.UserId <= 0)
            ModelState.AddModelError("UserId", "Please select a user.");
        if (!vm.SelectedRequestTypeIds.Any())
            ModelState.AddModelError("SelectedRequestTypeIds", "Please select at least one request type.");

        if (!ModelState.IsValid)
        {
            var rebuilt = await _svc.BuildFormAsync(vm.UserId > 0 ? vm.UserId : null);
            vm.Users        = rebuilt.Users;
            vm.RequestTypes = rebuilt.RequestTypes;
            vm.UserName     = rebuilt.UserName;
            return View("Form", vm);
        }

        var (ok, err) = await _svc.SaveAsync(vm, UserId);
        if (!ok)
        {
            Err(err!);
            var rebuilt = await _svc.BuildFormAsync(vm.UserId > 0 ? vm.UserId : null);
            vm.Users        = rebuilt.Users;
            vm.RequestTypes = rebuilt.RequestTypes;
            vm.UserName     = rebuilt.UserName;
            return View("Form", vm);
        }

        Ok("Mapping saved successfully.");
        return RedirectToAction("Index");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int userId)
    {
        if (DenyIfNoPerm("User Mapping", Perm.Delete) is { } r) return r;
        var (ok, err) = await _svc.DeleteAsync(userId, UserId);
        if (ok) Ok("Mapping deleted."); else Err(err!);
        return RedirectToAction("Index");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleStatus(int userId, bool isActive)
    {
        if (DenyIfNoPerm("User Mapping", Perm.Edit) is { } r) return r;
        var (ok, err) = await _svc.ToggleStatusAsync(userId, isActive);
        if (ok) Ok(isActive ? "Mapping activated." : "Mapping deactivated.");
        else Err(err!);
        return RedirectToAction("Index");
    }
}
