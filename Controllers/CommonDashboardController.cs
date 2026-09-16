namespace ApproveFlow.MVC.Controllers;

using ApproveFlow.MVC.Models.Dwo;

// The Common Dashboard — the landing page after login.
//
// It renders one section per module and performs NO calculation of its own: ApproveFlow
// figures come from ICommonDashboardService, DWO figures from IDwoDashboardService, and
// neither total is ever combined with the other.
//
// IDwoDashboardService is resolved OPTIONALLY: the DWO module ships its own implementation
// and registers it. Where it is absent the DWO section still renders — permission-gated,
// with working links — but shows no counts, rather than zeros that would misstate DWO data
// this application cannot see.
public class CommonDashboardController : BaseCtrl
{
    // Screen names as the Roles & Permissions matrix stores them.
    private const string RequestsScreen  = "Requests";
    private const string DashboardScreen = "Dashboard";

    private readonly ICommonDashboardService _svc;
    private readonly IDwoDashboardService? _dwo;

    // The DWO provider is looked up rather than required, so the application still starts —
    // and the ApproveFlow section still works — where the DWO module registers none.
    public CommonDashboardController(ICommonDashboardService svc, IServiceProvider sp)
    {
        _svc = svc;
        _dwo = sp.GetService<IDwoDashboardService>();
    }

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

        // DWO access = any of the DWO module's screens — the same names the Roles &
        // Permissions matrix stores and the sidebar gates on. There is no umbrella "DWO"
        // screen, so checking one would be ungrantable: no row by that name is ever written.
        vm.CanViewDwo            = DwoScreens.All.Any(HasPerm);
        vm.DwoProviderRegistered = _dwo is not null;

        if (_dwo is not null && vm.CanViewDwo)
        {
            // Identity is passed through; the DWO module applies its OWN permission model
            // and returns figures already scoped to this user.
            vm.Dwo = await _dwo.GetDashboardAsync(
                new DwoUserContext { UserId = UserId, Role = UserRole, IsAdmin = isAdmin }, ct);
        }

        ViewBag.IsAdmin  = isAdmin;
        ViewBag.UserRole = UserRole;
        return View(vm);
    }
}
