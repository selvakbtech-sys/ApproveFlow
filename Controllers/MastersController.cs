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
        if (vm.Id == 0)
        {
            switch (vm.EntityType)
            {
                case "locations":
                    _db.Locations.Add(new Location { Code = vm.Code.ToUpper(), LocationName = vm.Name, Description = vm.Description, IsActive = vm.IsActive }); break;
                case "departments":
                    _db.Departments.Add(new Department { Code = vm.Code.ToUpper(), DeptName = vm.Name, Description = vm.Description, IsActive = vm.IsActive }); break;
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
                default:
                    var t = await _db.RequestTypes.FindAsync(vm.Id); if (t != null) { t.Code = vm.Code.ToUpper(); t.TypeName = vm.Name; t.Description = vm.Description; t.IsActive = vm.IsActive; }
                    break;
            }
        }
        await _db.SaveChangesAsync();
        Ok(vm.Id == 0 ? "Created successfully." : "Updated successfully.");
        return RedirectToAction("Index", new { type = vm.EntityType });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string type, int id)
    {
        if (Guard(type, Perm.Delete) is { } g) return g;
        var sp = type switch
        {
            "locations" => "EXEC dbo.sp_DeleteLocation @LocationId={0}",
            "departments" => "EXEC dbo.sp_DeleteDepartment @DepartmentId={0}",
            "requesttypes" => "EXEC dbo.sp_DeleteRequestType @RequestTypeId={0}",
            _ => null
        };
        if (sp == null) return NotFound();
        try { await _db.Database.ExecuteSqlRawAsync(sp, id); Ok("Deleted."); }
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
        _ => null
    };
}
