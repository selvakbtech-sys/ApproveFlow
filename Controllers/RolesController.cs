namespace ApproveFlow.MVC.Controllers;

public class RolesController : BaseCtrl
{
    private readonly AppDbContext _db;
    public RolesController(AppDbContext db) { _db = db; }

    // The screen that edits this matrix. Now that the matrix binds for EVERY role — the
    // "Admin" short-circuit in BaseCtrl.HasPerm is gone — a save that cleared View or Edit
    // here for the last role holding them would leave nobody able to open this page and undo
    // it. That is the one unrecoverable edit, so it is refused below.
    private const string RolesScreen = "Roles & Permissions";

    // Returns the error to show when the proposed save would leave NO active role, held by at
    // least one active user, able to both open and edit Roles & Permissions; null when the
    // screen stays reachable. A role with no active user cannot be the holder — moving the
    // rights to an unused role locks everyone out just as effectively as clearing them.
    private async Task<string?> RolesScreenLockoutErrorAsync(RoleFormVM vm)
    {
        var proposed = vm.Permissions?.FirstOrDefault(p => p.ScreenName == RolesScreen);
        bool keepsRights = vm.IsActive && (proposed?.CanView ?? false) && (proposed?.CanEdit ?? false);

        // A brand-new role (RoleId 0) has no users yet, so it can never be the holder.
        bool thisRoleHolds = keepsRights && vm.RoleId != 0 && await _db.Users
            .AnyAsync(u => u.RoleId == vm.RoleId && u.Status.ToLower() == "active");
        if (thisRoleHolds) return null;

        bool anotherRoleHolds = await _db.Roles.AnyAsync(r =>
            r.RoleId != vm.RoleId &&
            r.IsActive &&
            r.Users.Any(u => u.Status.ToLower() == "active") &&
            r.Permissions.Any(p => p.ScreenName == RolesScreen && p.CanView && p.CanEdit));
        if (anotherRoleHolds) return null;

        return "This is the last active role that can open and edit Roles & Permissions. " +
               "Saving this would leave no one able to change permissions. Grant View and Edit " +
               "on Roles & Permissions to another active role first.";
    }

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

        if (await RolesScreenLockoutErrorAsync(vm) is { } lockout) { Err(lockout); return View("Form", vm); }

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
                // A role is "used in an active workflow" when an active workflow step targets it —
                // either as a ROLE-BASED step (ApproverRoleId, how workflows are configured today)
                // or as a LEGACY user-pinned step whose user holds this role. The original check
                // only covered the legacy user-pinned case, so role-based steps slipped through and
                // the block never fired for current workflows. Both cases are covered here so the
                // existing rule enforces as intended.
                bool usedInWorkflow = await _db.WorkflowSteps
                    .AnyAsync(s => s.Workflow.IsActive
                               && (s.ApproverRoleId == vm.RoleId
                                   || (s.ApproverUser != null && s.ApproverUser.RoleId == vm.RoleId)));
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
            await RefreshOwnClaimsAsync(role.RoleId);
            Ok("Role updated.");
        }
        return RedirectToAction("Index");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        if (DenyIfNoPerm("Roles & Permissions", Perm.Delete) is { } dr) return dr;

        // A role that is still assigned to one or more users must NOT be deleted — the user(s)
        // have to be reassigned to another role first. This server-side guard runs before
        // sp_DeleteRole so the rule cannot be bypassed from the UI, matching the in-use
        // validation the Masters delete actions already use.
        if (await _db.Users.AnyAsync(u => u.RoleId == id))
        {
            Err("Cannot delete: this role is assigned to one or more users. Reassign them to another role first.");
            return RedirectToAction("Index");
        }

        try { await _db.Database.ExecuteSqlRawAsync("EXEC dbo.sp_DeleteRole @RoleId={0}", id); Ok("Role deleted."); }
        catch (Exception ex) { Err(ex.Message); }
        return RedirectToAction("Index");
    }

    // Permission claims are minted at sign-in, so a session keeps whatever rights it was
    // issued until the user signs out. That was harmless while the Admin role bypassed the
    // matrix; now that the boxes bind, an administrator editing their OWN role would keep the
    // old menu and access until they signed out — the save would look like it had not worked.
    // Re-issuing the cookie here makes the change they just saved take effect on the next page.
    // Other signed-in users holding that role pick it up at their next sign-in, as before.
    private async Task RefreshOwnClaimsAsync(int savedRoleId)
    {
        // AsNoTracking so the rows come back from the database rather than from the change
        // tracker, which still holds the permission entities this save just deleted and
        // re-added; claims must reflect what was actually persisted.
        var me = await _db.Users
            .AsNoTracking()
            .Include(u => u.Role).ThenInclude(r => r.Permissions)
            .FirstOrDefaultAsync(u => u.UserId == UserId);
        if (me == null || me.RoleId != savedRoleId) return;

        // Keep the session's existing lifetime — re-signing must not silently downgrade a
        // "Remember me" cookie to a browser-session one.
        var current = await HttpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            Helpers.UserClaimsFactory.BuildPrincipal(me),
            current?.Properties ?? new AuthenticationProperties());
    }
}
