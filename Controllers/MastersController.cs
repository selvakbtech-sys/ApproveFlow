namespace ApproveFlow.MVC.Controllers;

public class MastersController : BaseCtrl
{
    private readonly AppDbContext _db;
    public MastersController(AppDbContext db) { _db = db; }

    // Maps the master "type" route value to its Roles & Permissions screen name.
    private static string? ScreenFor(string? type) => type switch
    {
        "locations"    => "Locations",
        "departments"  => "Departments",
        "requesttypes" => "Request Types",
        "companies"    => DwoScreens.Companies,
        _              => null
    };

    // Resolves the screen for a type and enforces the given action permission.
    // Returns a 403 result when denied (or NotFound for an unknown type); null when allowed.
    private IActionResult? Guard(string? type, string action)
    {
        var screen = ScreenFor(type);
        if (screen == null) return NotFound();
        return DenyIfNoPerm(screen, action);
    }

    public async Task<IActionResult> Index(string type)
    {
        if (Guard(type, Perm.View) is { } g) return g;
        ViewBag.Type = type;
        return type switch
        {
            "locations" => View("LocationList", await _db.Locations.OrderBy(l => l.Code).ToListAsync()),
            "departments" => View("DepartmentList", await _db.Departments.OrderBy(d => d.Code).ToListAsync()),
            "requesttypes" => View("RequestTypeList", await _db.RequestTypes.OrderBy(t => t.Code).ToListAsync()),
            "companies" => View("CompanyList", await _db.Companies.OrderBy(c => c.Code).ToListAsync()),
            _ => NotFound()
        };
    }

    [HttpGet] public IActionResult Create(string type)
    {
        if (Guard(type, Perm.Create) is { } g) return g;
        return View("Form", new MasterFormVM { EntityType = type });
    }

    [HttpGet]
    public async Task<IActionResult> Edit(string type, int id)
    {
        if (Guard(type, Perm.Edit) is { } g) return g;
        var vm = await LoadVmAsync(type, id);
        if (vm == null) { Err("Not found."); return RedirectToAction("Index", new { type }); }
        return View("Form", vm);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(MasterFormVM vm)
    {
        if (Guard(vm.EntityType, vm.Id == 0 ? Perm.Create : Perm.Edit) is { } g) return g;
        if (!ModelState.IsValid) return View("Form", vm);
        // Track a newly created Department/Location so, after save, its Id can be cascaded to
        // users who currently hold ALL active departments/locations ("Select All" cohort).
        Department? newDept = null;
        Location? newLoc = null;
        if (vm.Id == 0)
        {
            switch (vm.EntityType)
            {
                case "locations":
                    newLoc = new Location { Code = vm.Code.ToUpper(), LocationName = vm.Name, Description = vm.Description, IsActive = vm.IsActive };
                    _db.Locations.Add(newLoc); break;
                case "departments":
                    newDept = new Department { Code = vm.Code.ToUpper(), DeptName = vm.Name, Description = vm.Description, IsActive = vm.IsActive };
                    _db.Departments.Add(newDept); break;
                case "companies":
                    _db.Companies.Add(new Company { Code = vm.Code.ToUpper(), CompanyName = vm.Name, Description = vm.Description, IsActive = vm.IsActive }); break;
                default:
                    _db.RequestTypes.Add(new RequestType { Code = vm.Code.ToUpper(), TypeName = vm.Name, Description = vm.Description, IsActive = vm.IsActive }); break;
            }
        }
        else
        {
            switch (vm.EntityType)
            {
                case "locations":
                    var l = await _db.Locations.FindAsync(vm.Id); if (l != null) { l.Code = vm.Code.ToUpper(); l.LocationName = vm.Name; l.Description = vm.Description; l.IsActive = vm.IsActive; }
                    break;
                case "departments":
                    var d = await _db.Departments.FindAsync(vm.Id); if (d != null) { d.Code = vm.Code.ToUpper(); d.DeptName = vm.Name; d.Description = vm.Description; d.IsActive = vm.IsActive; }
                    break;
                case "companies":
                    var co = await _db.Companies.FindAsync(vm.Id); if (co != null) { co.Code = vm.Code.ToUpper(); co.CompanyName = vm.Name; co.Description = vm.Description; co.IsActive = vm.IsActive; }
                    break;
                default:
                    var t = await _db.RequestTypes.FindAsync(vm.Id); if (t != null) { t.Code = vm.Code.ToUpper(); t.TypeName = vm.Name; t.Description = vm.Description; t.IsActive = vm.IsActive; }
                    break;
            }
        }
        await _db.SaveChangesAsync();

        // Sticky "ALL": a newly created ACTIVE Department/Location is auto-added to every user
        // who currently holds ALL other active departments/locations (i.e. previously had
        // "Select All"), so their selection stays complete. Users with a partial selection are
        // never touched. No effect for inactive masters or for edits.
        if (newDept is { IsActive: true })
            await CascadeNewDepartmentToAllSelectedUsersAsync(newDept.DepartmentId);
        if (newLoc is { IsActive: true })
            await CascadeNewLocationToAllSelectedUsersAsync(newLoc.LocationId);

        Ok(vm.Id == 0 ? "Created successfully." : "Updated successfully.");
        return RedirectToAction("Index", new { type = vm.EntityType });
    }

    // Adds the newly created department to every user whose current department selection covers
    // ALL previously-active departments (the "Select All" cohort). A user with no departments,
    // or only a subset, is left unchanged.
    private async Task CascadeNewDepartmentToAllSelectedUsersAsync(int newDeptId)
    {
        var priorActiveIds = await _db.Departments
            .Where(d => d.IsActive && d.DepartmentId != newDeptId)
            .Select(d => d.DepartmentId).ToListAsync();
        if (priorActiveIds.Count == 0) return; // no baseline of "all" existed → nobody qualifies

        var byUser = (await _db.UserDepartments.AsNoTracking()
                .Select(ud => new { ud.UserId, ud.DepartmentId }).ToListAsync())
            .GroupBy(x => x.UserId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.DepartmentId).ToHashSet());

        bool added = false;
        foreach (var (uid, deptSet) in byUser)
            if (!deptSet.Contains(newDeptId) && priorActiveIds.All(deptSet.Contains))
            {
                _db.UserDepartments.Add(new UserDepartment { UserId = uid, DepartmentId = newDeptId });
                added = true;
            }
        if (added) await _db.SaveChangesAsync();
    }

    // Location counterpart of CascadeNewDepartmentToAllSelectedUsersAsync.
    private async Task CascadeNewLocationToAllSelectedUsersAsync(int newLocId)
    {
        var priorActiveIds = await _db.Locations
            .Where(l => l.IsActive && l.LocationId != newLocId)
            .Select(l => l.LocationId).ToListAsync();
        if (priorActiveIds.Count == 0) return;

        var byUser = (await _db.UserLocations.AsNoTracking()
                .Select(ul => new { ul.UserId, ul.LocationId }).ToListAsync())
            .GroupBy(x => x.UserId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.LocationId).ToHashSet());

        bool added = false;
        foreach (var (uid, locSet) in byUser)
            if (!locSet.Contains(newLocId) && priorActiveIds.All(locSet.Contains))
            {
                _db.UserLocations.Add(new UserLocation { UserId = uid, LocationId = newLocId });
                added = true;
            }
        if (added) await _db.SaveChangesAsync();
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string type, int id)
    {
        if (Guard(type, Perm.Delete) is { } g) return g;
        try
        {
            switch (type)
            {
                case "locations":
                    if (await _db.Requests.AnyAsync(r => r.LocationId == id)
                        || await _db.WorkflowDefinitions.AnyAsync(w => w.LocationId == id)
                        || await _db.UserLocations.AnyAsync(u => u.LocationId == id))
                    { Err("Cannot delete: this location is in use. Deactivate it instead."); break; }
                    var l = await _db.Locations.FindAsync(id);
                    if (l == null) { Err("Not found."); break; }
                    _db.Locations.Remove(l); await _db.SaveChangesAsync(); Ok("Deleted.");
                    break;
                case "departments":
                    if (await _db.Requests.AnyAsync(r => r.DepartmentId == id)
                        || await _db.WorkflowDefinitions.AnyAsync(w => w.DepartmentId == id)
                        || await _db.UserDepartments.AnyAsync(u => u.DepartmentId == id))
                    { Err("Cannot delete: this department is in use. Deactivate it instead."); break; }
                    var d = await _db.Departments.FindAsync(id);
                    if (d == null) { Err("Not found."); break; }
                    _db.Departments.Remove(d); await _db.SaveChangesAsync(); Ok("Deleted.");
                    break;
                case "requesttypes":
                    if (await _db.Requests.AnyAsync(r => r.RequestTypeId == id)
                        || await _db.WorkflowDefinitions.AnyAsync(w => w.RequestTypeId == id)
                        || await _db.UserRequestTypeMappings.AnyAsync(u => u.RequestTypeId == id))
                    { Err("Cannot delete: this request type is in use. Deactivate it instead."); break; }
                    var t = await _db.RequestTypes.FindAsync(id);
                    if (t == null) { Err("Not found."); break; }
                    _db.RequestTypes.Remove(t); await _db.SaveChangesAsync(); Ok("Deleted.");
                    break;
                case "companies":
                    if (await _db.DwoPoints.AnyAsync(p => p.CompanyId == id))
                    { Err("Cannot delete: this company is in use. Deactivate it instead."); break; }
                    var co = await _db.Companies.FindAsync(id);
                    if (co == null) { Err("Not found."); break; }
                    _db.Companies.Remove(co); await _db.SaveChangesAsync(); Ok("Deleted.");
                    break;
                default:
                    return NotFound();
            }
        }
        catch (Exception ex) { Err(ex.Message); }
        return RedirectToAction("Index", new { type });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Toggle(string type, int id)
    {
        if (Guard(type, Perm.Edit) is { } g) return g;
        switch (type)
        {
            case "locations":
                var l = await _db.Locations.FindAsync(id); if (l != null) l.IsActive = !l.IsActive; break;
            case "departments":
                var d = await _db.Departments.FindAsync(id); if (d != null) d.IsActive = !d.IsActive; break;
            case "companies":
                var co = await _db.Companies.FindAsync(id); if (co != null) co.IsActive = !co.IsActive; break;
            default:
                var t = await _db.RequestTypes.FindAsync(id); if (t != null) t.IsActive = !t.IsActive; break;
        }
        await _db.SaveChangesAsync();
        return RedirectToAction("Index", new { type });
    }

    private async Task<MasterFormVM?> LoadVmAsync(string type, int id) => type switch
    {
        "locations" => await _db.Locations.Where(l => l.LocationId == id)
            .Select(l => new MasterFormVM { Id = l.LocationId, Code = l.Code, Name = l.LocationName, Description = l.Description, IsActive = l.IsActive, EntityType = type }).FirstOrDefaultAsync(),
        "departments" => await _db.Departments.Where(d => d.DepartmentId == id)
            .Select(d => new MasterFormVM { Id = d.DepartmentId, Code = d.Code, Name = d.DeptName, Description = d.Description, IsActive = d.IsActive, EntityType = type }).FirstOrDefaultAsync(),
        "requesttypes" => await _db.RequestTypes.Where(t => t.RequestTypeId == id)
            .Select(t => new MasterFormVM { Id = t.RequestTypeId, Code = t.Code, Name = t.TypeName, Description = t.Description, IsActive = t.IsActive, EntityType = type }).FirstOrDefaultAsync(),
        "companies" => await _db.Companies.Where(c => c.CompanyId == id)
            .Select(c => new MasterFormVM { Id = c.CompanyId, Code = c.Code, Name = c.CompanyName, Description = c.Description, IsActive = c.IsActive, EntityType = type }).FirstOrDefaultAsync(),
        _ => null
    };
}
