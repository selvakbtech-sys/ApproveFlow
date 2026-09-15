namespace ApproveFlow.MVC.Controllers;

using ApproveFlow.MVC.Models.Dwo;

// The Common Dashboard — the landing page after login. It renders two INDEPENDENT sections
// side by side and performs no calculation of its own: ApproveFlow figures come from
// IApproveFlowDashboardService, DWO figures from IDwoDashboardService, and neither total is
// ever combined with the other.
//
// IDwoDashboardService is resolved OPTIONALLY: the DWO module ships its own implementation
// and registers it. Where it is absent the DWO section renders as unavailable — it is never
// filled with zeros, which would misstate DWO data this application cannot see.
public class CommonDashboardController : BaseCtrl
{
    // Screen names used for section visibility (same claim vocabulary as the rest of the app).
    private const string ApproveFlowScreen = "Requests";
    private const string ApproveFlowDashboardScreen = "Dashboard";
    private const string DwoScreen = "DWO";

    private readonly IApproveFlowDashboardService _approveFlow;
    private readonly IDwoDashboardService? _dwo;

    // The DWO service is looked up rather than required, so this application still starts —
    // and the ApproveFlow section still works — in a deployment without the DWO module.
    public CommonDashboardController(IApproveFlowDashboardService approveFlow, IServiceProvider sp)
    {
        _approveFlow = approveFlow;
        _dwo = sp.GetService<IDwoDashboardService>();
    }

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        bool isAdmin = UserRole == "Admin";
        bool canViewApproveFlow = HasPerm(ApproveFlowDashboardScreen) || HasPerm(ApproveFlowScreen);
        bool canViewDwo = HasPerm(DwoScreen);

        var vm = new CommonDashboardVM
        {
            DwoModuleRegistered = _dwo is not null,
            CanViewDwo          = canViewDwo,
        };

        if (canViewApproveFlow)
        {
            // Same scope the Requests grid gives this user: Admin sees every requestor,
            // everyone else only their own requests (RequestsController.ScopeUserId).
            int? scopeUserId = isAdmin ? null : UserId;
            vm.ApproveFlow = await _approveFlow.GetAsync(scopeUserId, ct);
        }

        if (_dwo is not null && canViewDwo)
        {
            // Identity is passed through; the DWO module applies its OWN permission model.
            vm.Dwo = await _dwo.GetDashboardAsync(
                new DwoUserContext { UserId = UserId, Role = UserRole, IsAdmin = isAdmin }, ct);
        }

        ViewBag.IsAdmin = isAdmin;
        ViewBag.UserRole = UserRole;
        return View(vm);
    }
}
