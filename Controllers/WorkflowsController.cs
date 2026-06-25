namespace ApproveFlow.MVC.Controllers;

public class WorkflowsController : BaseCtrl
{
    private readonly AppDbContext _db;
    public WorkflowsController(AppDbContext db) { _db = db; }

    public async Task<IActionResult> Index(string active = "all")
    {
        if (DenyIfNoPerm("Workflow Definition") is { } rw) return rw;
        var q = _db.WorkflowDefinitions.Include(w => w.Location).Include(w => w.RequestType)
            .Include(w => w.Department).Include(w => w.Steps).ThenInclude(s => s.ApproverUser).ThenInclude(u => u!.Role).AsQueryable();
        if (active == "active") q = q.Where(w => w.IsActive);
        if (active == "inactive") q = q.Where(w => !w.IsActive);
        var items = await q.OrderBy(w => w.WorkflowName).Select(w => new WorkflowItemVM
        {
            WorkflowId = w.WorkflowId,
            WorkflowName = w.WorkflowName,
            IsActive = w.IsActive,
            Location = w.Location != null ? w.Location.LocationName : "Any",
            RequestType = w.RequestType != null ? w.RequestType.TypeName : "Any",
            Priority = w.Priority ?? "Any",
            Department = w.Department != null ? w.Department.DeptName : "Any",
            Steps = w.Steps.OrderBy(s => s.StepLevel).Select(s => new WfStepVM
            {
                StepLevel = s.StepLevel,
                ApproverName = "",
                ApproverRole = s.IsAutoAssignDeptManager ? "Manager (auto)" : (s.ApproverUser!.Role.RoleName)
            }).ToList(),
            CreatedAt = w.CreatedAt,
            UpdatedAt = w.UpdatedAt,
        }).ToListAsync();
        return View(new WorkflowListVM { Items = items, FilterActive = active });
    }

    [HttpGet] public async Task<IActionResult> Create() => View("Form", await BuildFormAsync(null));
    [HttpGet] public async Task<IActionResult> Edit(int id) => View("Form", await BuildFormAsync(id));

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(WorkflowFormVM vm)
    {
        if (!ModelState.IsValid) { await PopulateAsync(vm); return View("Form", vm); }
        if (vm.Steps == null || vm.Steps.Count == 0)
        {
            ModelState.AddModelError("", "At least one approval step is required.");
            await PopulateAsync(vm); return View("Form", vm);
        }

        foreach (var s in vm.Steps)
        {
            s.IsAutoAssignDeptManager = false;
            if (!string.IsNullOrEmpty(s.ApproverRole))
            {
                var user = await _db.Users.Include(u => u.Role)
                    .Where(u => u.Role.RoleName == s.ApproverRole && u.Status == "active")
                    .FirstOrDefaultAsync();
                s.ApproverUserId = user?.UserId;
            }
        }

        if (vm.WorkflowId == 0)
        {
            var duplicate = await _db.WorkflowDefinitions.AnyAsync(w =>
                w.LocationId    == vm.LocationId &&
                w.DepartmentId  == vm.DepartmentId &&
                w.RequestTypeId == vm.RequestTypeId &&
                w.Priority      == (string.IsNullOrEmpty(vm.Priority) ? null : vm.Priority) &&
                w.IsActive);
            if (duplicate)
            {
                ModelState.AddModelError("",
                    "An active workflow already exists for this combination of Location, Department, Request Type and Priority. " +
                    "Please deactivate the existing one first or choose different criteria.");
                await PopulateAsync(vm);
                return View("Form", vm);
            }

            var wf = new WorkflowDefinition
            {
                WorkflowName  = vm.WorkflowName,
                LocationId    = vm.LocationId,
                RequestTypeId = vm.RequestTypeId,
                Priority      = string.IsNullOrEmpty(vm.Priority) ? null : vm.Priority,
                DepartmentId  = vm.DepartmentId,
                IsActive      = vm.IsActive,
            };
            foreach (var s in vm.Steps.OrderBy(x => x.StepLevel))
                wf.Steps.Add(new WorkflowStep { StepLevel = s.StepLevel, ApproverUserId = s.ApproverUserId, IsAutoAssignDeptManager = false });
            _db.WorkflowDefinitions.Add(wf); await _db.SaveChangesAsync(); Ok("Workflow created.");
        }
        else
        {
            var wf = await _db.WorkflowDefinitions.Include(w => w.Steps).FirstOrDefaultAsync(w => w.WorkflowId == vm.WorkflowId);
            if (wf == null) { Err("Not found."); return RedirectToAction("Index"); }

            var dupEdit = await _db.WorkflowDefinitions.AnyAsync(w =>
                w.WorkflowId   != vm.WorkflowId &&
                w.LocationId    == vm.LocationId &&
                w.DepartmentId  == vm.DepartmentId &&
                w.RequestTypeId == vm.RequestTypeId &&
                w.Priority      == (string.IsNullOrEmpty(vm.Priority) ? null : vm.Priority) &&
                w.IsActive);
            if (dupEdit)
            {
                ModelState.AddModelError("",
                    "Another active workflow already exists for this combination. Deactivate it first or change the criteria.");
                await PopulateAsync(vm);
                return View("Form", vm);
            }

            wf.WorkflowName = vm.WorkflowName; wf.LocationId = vm.LocationId; wf.RequestTypeId = vm.RequestTypeId;
            wf.Priority = string.IsNullOrEmpty(vm.Priority) ? null : vm.Priority;
            wf.DepartmentId = vm.DepartmentId; wf.IsActive = vm.IsActive;
            _db.WorkflowSteps.RemoveRange(wf.Steps);
            foreach (var s in vm.Steps.OrderBy(x => x.StepLevel))
                wf.Steps.Add(new WorkflowStep { StepLevel = s.StepLevel, ApproverUserId = s.ApproverUserId, IsAutoAssignDeptManager = false });
            await _db.SaveChangesAsync(); Ok("Workflow updated.");
        }
        return RedirectToAction("Index");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleActive(int id)
    {
        var wf = await _db.WorkflowDefinitions.FindAsync(id);
        if (wf != null) { wf.IsActive = !wf.IsActive; await _db.SaveChangesAsync(); }
        return RedirectToAction("Index");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        try
        {
            bool inUse = await _db.Requests.AnyAsync(r => r.WorkflowId == id);
            if (inUse)
            {
                Err("Cannot delete: Workflow is referenced by existing Requests. Deactivate it instead.");
                return RedirectToAction("Index");
            }
            var steps = _db.WorkflowSteps.Where(s => s.WorkflowId == id);
            _db.WorkflowSteps.RemoveRange(steps);
            var wf = await _db.WorkflowDefinitions.FindAsync(id);
            if (wf != null) _db.WorkflowDefinitions.Remove(wf);
            await _db.SaveChangesAsync();
            Ok("Workflow deleted.");
        }
        catch (Exception ex) { Err(ex.Message); }
        return RedirectToAction("Index");
    }

    private async Task<WorkflowFormVM> BuildFormAsync(int? id)
    {
        var vm = new WorkflowFormVM { Steps = new List<WfStepFormVM> { new() { StepLevel = 1 } } };
        await PopulateAsync(vm);
        if (id == null) return vm;
        var wf = await _db.WorkflowDefinitions.Include(w => w.Steps).FirstOrDefaultAsync(w => w.WorkflowId == id);
        if (wf == null) return vm;
        vm.WorkflowId = wf.WorkflowId; vm.WorkflowName = wf.WorkflowName;
        vm.LocationId = wf.LocationId; vm.RequestTypeId = wf.RequestTypeId;
        vm.Priority = wf.Priority; vm.DepartmentId = wf.DepartmentId; vm.IsActive = wf.IsActive;
        vm.Steps = wf.Steps.OrderBy(s => s.StepLevel)
            .Select(s => new WfStepFormVM
            {
                StepLevel = s.StepLevel,
                ApproverUserId = s.ApproverUserId,
                ApproverRole = s.ApproverUser != null ? s.ApproverUser.Role.RoleName : "",
                IsAutoAssignDeptManager = s.IsAutoAssignDeptManager
            }).ToList();
        return vm;
    }

    private async Task PopulateAsync(WorkflowFormVM vm)
    {
        vm.Locations    = await _db.Locations.Where(l => l.IsActive).OrderBy(l => l.LocationName).ToListAsync();
        vm.RequestTypes = await _db.RequestTypes.Where(t => t.IsActive).OrderBy(t => t.TypeName).ToListAsync();
        vm.Departments  = await _db.Departments.Where(d => d.IsActive).OrderBy(d => d.DeptName).ToListAsync();
        var approverRoleIds = await _db.RolePermissions
            .Where(p => p.ScreenName == "Requests" && p.CanApprove)
            .Select(p => p.RoleId)
            .ToListAsync();
        vm.Approvers = await _db.Users.Include(u => u.Role)
            .Where(u => u.Status == "active" && approverRoleIds.Contains(u.RoleId))
            .OrderBy(u => u.FullName).ToListAsync();
    }
}
