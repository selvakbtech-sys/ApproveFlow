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
    // Screen name as the Roles & Permissions matrix stores it.
    private const string RequestsScreen = "Requests";

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

        // The section is gated on the SAME screen its links lead to. Every card and every
        // row opens a RequestsController action, and each of those guards on "Requests" —
        // so granting the section on the Dashboard screen as well would show a user counts
        // they may see but rows they may not, and every click would land on Access Denied.
        bool canViewApproveFlow = HasPerm(RequestsScreen);

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
        // The names come from this dashboard's own DwoDashboardScreens, so the build does
        // not depend on a DwoScreens shape that varies between trees.
        vm.CanViewDwo            = DwoDashboardScreens.All.Any(HasPerm);
        vm.DwoProviderRegistered = _dwo is not null;

        if (_dwo is not null && vm.CanViewDwo)
        {
            // Identity is passed through; the DWO module applies its OWN permission model
            // and returns figures already scoped to this user.
            // The flags mirror DwoController.ActorAsync, so the figures are scoped exactly as
            // the DWO grids the cards open.
            vm.Dwo = await _dwo.GetDashboardAsync(new DwoUserContext
            {
                UserId          = UserId,
                Role            = UserRole,
                IsAdmin         = isAdmin,
                CanViewRegister = HasPerm(DwoDashboardScreens.Register),
                IsManagement    = HasPerm(DwoDashboardScreens.ManagementDiscussion, Perm.Approve),
                IsHod           = HasPerm(DwoDashboardScreens.Register, Perm.Approve),
            }, ct);
        }

        ViewBag.IsAdmin  = isAdmin;
        ViewBag.UserRole = UserRole;
        return View(vm);
    }
}
