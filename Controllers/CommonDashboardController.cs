namespace ApproveFlow.MVC.Controllers;

// The Common Dashboard — the landing page after login.
//
// It renders one section per module and performs NO calculation of its own: each section's
// figures come from that module's own service, and no total is ever combined across
// sections. Today that is the ApproveFlow section; the DWO section plugs in the same way
// (see the seam at the bottom of Index).
public class CommonDashboardController : BaseCtrl
{
    // Screen names as the Roles & Permissions matrix stores them.
    private const string RequestsScreen  = "Requests";
    private const string DashboardScreen = "Dashboard";

    private readonly ICommonDashboardService _svc;

    public CommonDashboardController(ICommonDashboardService svc) { _svc = svc; }

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        bool isAdmin = UserRole == "Admin";

        // Either screen grants the ApproveFlow section: a user who can reach the Requests
        // grid can see a summary of it, and so can one granted the Dashboard screen.
        bool canViewApproveFlow = HasPerm(DashboardScreen) || HasPerm(RequestsScreen);

        var vm = new CommonDashboardVM();

        if (canViewApproveFlow)
        {
            // Same scope the Requests grid gives this user: Admin sees every requestor,
            // everyone else only their own requests. Per the dashboard rules, a
            // user-specific dashboard shows only the logged-in user's data; the Admin
            // dashboard may show global data.
            int? scopeUserId = isAdmin ? null : UserId;
            vm.ApproveFlow = await _svc.GetApproveFlowAsync(scopeUserId, ct);
        }

        // ── DWO section seam ───────────────────────────────────────────────────────
        // The DWO module owns its own data and routes, so its figures must come from the
        // DWO module itself rather than be derived here. Nothing is rendered for it until
        // that is wired in — a fabricated 0 would read as "no discussions", a statement
        // about DWO data this controller is in no position to make.

        ViewBag.IsAdmin  = isAdmin;
        ViewBag.UserRole = UserRole;
        return View(vm);
    }
}
