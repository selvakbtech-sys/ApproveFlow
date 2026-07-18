namespace ApproveFlow.MVC.Controllers;

public class RolesController : BaseCtrl
{
    private readonly AppDbContext _db;
    public RolesController(AppDbContext db) { _db = db; }

    public async Task<IActionResult> Index()
    {
        if (DenyIfNoPerm("Roles & Permissions") is { } rr) return rr;
        var roles = await _db.Roles.Include(r => r.Permissions).Include(r => r.Users).ToListAsync();
        return View(new RoleListVM
        {
            Items = roles.Select(r => new RoleItemVM
            {
                RoleId = r.RoleId,
                RoleName = r.RoleName,
                Description = r.Description,
                BadgeColor = r.BadgeColor,
                IsActive = r.IsActive,
                UserCount = r.Users.Count,
                Permissions = r.Permissions.ToList(),
                CreatedAt = r.CreatedAt,
            }).ToList()
        });
    }

    [HttpGet]
    public IActionResult Create()
    {
        if (DenyIfNoPerm("Roles & Permissions", Perm.Create) is { } r) return r;
        var vm = new RoleFormVM();
        vm.Permissions = RoleFormVM.AllScreens.Select(s => new PermRowVM { ScreenName = s }).ToList();
        return View("Form", vm);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        if (DenyIfNoPerm("Roles & Permissions", Perm.Edit) is { } dr) return dr;
        var role = await _db.Roles.Include(r => r.Permissions).FirstOrDefaultAsync(r => r.RoleId == id);
        if (role == null) { Err("Not found."); return RedirectToAction("Index"); }
        var vm = new RoleFormVM
        {
            RoleId = role.RoleId,
            RoleName = role.RoleName,
            Description = role.Description,
            BadgeColor = role.BadgeColor,
            IsActive = role.IsActive,
            Permissions = RoleFormVM.AllScreens.Select(s =>
            {
                var p = role.Permissions.FirstOrDefault(x => x.ScreenName == s);
                return new PermRowVM
                {
                    ScreenName = s,
                    CanView = p?.CanView ?? false,
                    CanCreate = p?.CanCreate ?? false,
                    CanEdit = p?.CanEdit ?? false,
                    CanApprove = p?.CanApprove ?? false,
                    CanDelete = p?.CanDelete ?? false,
                };
            }).ToList()
        };
        return View("Form", vm);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(RoleFormVM vm)
    {
        if (DenyIfNoPerm("Roles & Permissions", vm.RoleId == 0 ? Perm.Create : Perm.Edit) is { } dr) return dr;
        if (!ModelState.IsValid) return View("Form", vm);
        if (vm.Permissions == null || vm.Permissions.Count == 0)
            vm.Permissions = RoleFormVM.AllScreens.Select(s => new PermRowVM { ScreenName = s }).ToList();

        if (vm.RoleId == 0)
        {
            var role = new Role { RoleName = vm.RoleName, Description = vm.Description, BadgeColor = vm.BadgeColor, IsActive = vm.IsActive };
            foreach (var p in vm.Permissions)
                role.Permissions.Add(new RolePermission
                {
                    ScreenName = p.ScreenName,
                    CanView = p.CanView,
                    CanCreate = p.IsReport ? false : p.CanCreate,
                    CanEdit = p.IsReport ? false : p.CanEdit,
                    CanApprove = p.IsReport ? false : p.CanApprove,
                    CanDelete = p.IsReport ? false : p.CanDelete,
                });
            _db.Roles.Add(role); await _db.SaveChangesAsync();
            Ok("Role created.");
        }
        else
        {
            var role = await _db.Roles.Include(r => r.Permissions).FirstOrDefaultAsync(r => r.RoleId == vm.RoleId);
            if (role == null) { Err("Not found."); return RedirectToAction("Index"); }

            var requestsPermOld = role.Permissions.FirstOrDefault(p => p.ScreenName == "Requests");
            var requestsPermNew = vm.Permissions?.FirstOrDefault(p => p.ScreenName == "Requests");
            bool wasApprover  = requestsPermOld?.CanApprove ?? false;
            bool willApprover = requestsPermNew?.CanApprove ?? false;
            if (wasApprover && !willApprover)
            {
                bool usedInWorkflow = await _db.WorkflowSteps
                    .AnyAsync(s => s.ApproverUser != null
                               && s.ApproverUser.RoleId == vm.RoleId
                               && s.Workflow.IsActive);
                if (usedInWorkflow)
                {
                    Err("This role is currently used in workflow approvals and Approve Rights cannot be removed.");
                    return View("Form", vm);
                }
            }

            role.RoleName = vm.RoleName; role.Description = vm.Description;
            role.BadgeColor = vm.BadgeColor; role.IsActive = vm.IsActive;
            _db.RolePermissions.RemoveRange(role.Permissions);
            foreach (var p in vm.Permissions)
                role.Permissions.Add(new RolePermission
                {
                    RoleId = role.RoleId,
                    ScreenName = p.ScreenName,
                    CanView = p.CanView,
                    CanCreate = p.IsReport ? false : p.CanCreate,
                    CanEdit = p.IsReport ? false : p.CanEdit,
                    CanApprove = p.IsReport ? false : p.CanApprove,
                    CanDelete = p.IsReport ? false : p.CanDelete,
                });
            await _db.SaveChangesAsync();
            Ok("Role updated.");
        }
        return RedirectToAction("Index");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        if (DenyIfNoPerm("Roles & Permissions", Perm.Delete) is { } dr) return dr;
        try { await _db.Database.ExecuteSqlRawAsync("EXEC dbo.sp_DeleteRole @RoleId={0}", id); Ok("Role deleted."); }
        catch (Exception ex) { Err(ex.Message); }
        return RedirectToAction("Index");
    }
}
