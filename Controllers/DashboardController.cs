namespace ApproveFlow.MVC.Controllers;

public class DashboardController : BaseCtrl
{
    private readonly IDashboardService _svc;
    private readonly AppDbContext _db;
    private readonly IDwoService _dwo;
    private readonly ILogger<DashboardController> _log;
    public DashboardController(IDashboardService svc, AppDbContext db, IDwoService dwo, ILogger<DashboardController> log)
    { _svc = svc; _db = db; _dwo = dwo; _log = log; }

    public async Task<IActionResult> Index()
    {
        if (DenyIfNoPerm("Dashboard") is { } rp) return rp;
        bool isAdmin = UserRole == "Admin";
        bool isApprover = !isAdmin && await _db.RequestApprovers.AnyAsync(a => a.ApproverUserId == UserId);
        ViewBag.IsAdmin = isAdmin;
        ViewBag.IsApprover = isApprover;
        ViewBag.UserRole = UserRole;
        ViewBag.DwoSummary = await TryGetDwoSummaryAsync();
        return View(await _svc.GetAsync(UserId, isAdmin));
    }

    // ── DWO slice (additive) ──
    // Queried only when the user can actually see the DWO module. This is the ONE place
    // the dashboard depends on an optional module, so a failure here must never take the
    // landing page down with it — most obviously when the DWO tables have not been
    // migrated onto this database yet. On any failure the card is simply omitted and the
    // dashboard renders exactly as it did before the module existed.
    private async Task<DwoDashboardSummaryVM?> TryGetDwoSummaryAsync()
    {
        if (!HasPerm(DwoScreens.Register)) return null;
        try
        {
            return await _dwo.GetSummaryAsync(await _dwo.BuildActorAsync(new DwoActor
            {
                UserId       = UserId,
                UserName     = User.FindFirstValue(ClaimTypes.Name) ?? "",
                IsManagement = HasPerm(DwoScreens.Management, Perm.Approve),
                IsHod        = HasPerm(DwoScreens.Register, Perm.Approve),
            }));
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Dashboard: the DWO summary could not be loaded — the card was omitted. "
                              + "If this says \"Invalid object name\", run the AddDwoModule migration on this database.");
            return null;
        }
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
