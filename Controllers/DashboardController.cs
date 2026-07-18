namespace ApproveFlow.MVC.Controllers;

public class DashboardController : BaseCtrl
{
    private readonly IDashboardService _svc;
    private readonly AppDbContext _db;
    public DashboardController(IDashboardService svc, AppDbContext db) { _svc = svc; _db = db; }

    public async Task<IActionResult> Index()
    {
        if (DenyIfNoPerm("Dashboard") is { } rp) return rp;
        bool isAdmin = UserRole == "Admin";
        bool isApprover = !isAdmin && await _db.RequestApprovers.AnyAsync(a => a.ApproverUserId == UserId);
        ViewBag.IsAdmin = isAdmin;
        ViewBag.IsApprover = isApprover;
        ViewBag.UserRole = UserRole;
        return View(await _svc.GetAsync(UserId, isAdmin));
    }

    [HttpGet]
    public async Task<IActionResult> TabData(string tab)
    {
        if (DenyIfNoPerm("Dashboard") is { } rp) return rp;
        return tab switch
        {
            "approvals" => PartialView("_ApprovalsTab", await _svc.GetMyApprovalsTabAsync(UserId)),
            "territory" => PartialView("_TerritoryTab", await _svc.GetMyTerritoryTabAsync(UserId)),
            _           => PartialView("_RequestsTab",  await _svc.GetMyRequestsTabAsync(UserId)),
        };
    }

    [HttpGet]
    public async Task<IActionResult> GetSummaries()
    {
        if (UserRole != "Admin") return Forbid();
        var result = new
        {
            byType       = await _svc.GetRequestTypeSummaryAsync(),
            byPriority   = await _svc.GetPrioritySummaryAsync(),
            byLocation   = await _svc.GetLocationSummaryAsync(),
            byDepartment = await _svc.GetDepartmentSummaryAsync(),
        };
        return Json(result);
    }
}
