namespace ApproveFlow.MVC.Services;

// ApproveFlow section of the Common Dashboard. Reads ONLY the ApproveFlow tables and knows
// nothing about DWO — the two modules are wired through separate services by design.
public interface IApproveFlowDashboardService
{
    // scopeUserId: null counts every requestor (Admin), a value counts only that user's
    // requests. It must be the SAME scope the Requests grid applies to this user, otherwise
    // a KPI card and the grid it opens would disagree.
    Task<ApproveFlowDashboardVM> GetAsync(int? scopeUserId, CancellationToken ct = default);
}

public class ApproveFlowDashboardService : IApproveFlowDashboardService
{
    private const int RecentRequestCount = 8;

    private readonly AppDbContext _db;
    public ApproveFlowDashboardService(AppDbContext db) { _db = db; }

    public async Task<ApproveFlowDashboardVM> GetAsync(int? scopeUserId, CancellationToken ct = default)
    {
        // One base query for the KPI cards AND the recent grid, built with exactly the filters
        // RequestService.GetListAsync applies (Deleted excluded, optional requestor scope), so
        // every card total equals the grid it opens.
        var scoped = _db.Requests
            .AsNoTracking()
            .Where(r => r.Status != RequestStatus.Deleted);
        if (scopeUserId.HasValue)
            scoped = scoped.Where(r => r.RequestorId == scopeUserId.Value);

        var vm = new ApproveFlowDashboardVM { IsGlobalScope = !scopeUserId.HasValue };

        foreach (var (label, filterStatus, accent) in ApproveFlowKpi.Cards)
        {
            // The card's own FilterStatus drives the count through the shared map, so the
            // card and /Requests?FilterStatus=<same value> can never diverge.
            var statuses = DashboardStatusFilter.Map(filterStatus);
            var count = statuses is null
                ? await scoped.CountAsync(r => r.Status == filterStatus, ct)
                : await scoped.CountAsync(r => statuses.Contains(r.Status), ct);

            vm.Kpis.Add(new ApproveFlowKpiCardVM
            {
                Label        = label,
                FilterStatus = filterStatus,
                Count        = count,
                Accent       = accent,
            });
        }

        vm.RecentRequests = await scoped
            .OrderByDescending(r => r.RequestId)
            .Take(RecentRequestCount)
            .Select(r => new ApproveFlowRecentRequestVM
            {
                RequestId     = r.RequestId,
                RequestNumber = r.RequestNumber,
                Category      = r.RequestType.TypeName,
                RequestedBy   = r.Requestor.FullName,
                Status        = r.Status,
                Date          = r.CreatedAt,
            })
            .ToListAsync(ct);

        return vm;
    }
}
