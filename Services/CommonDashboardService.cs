namespace ApproveFlow.MVC.Services;

// ApproveFlow section of the Common Dashboard. Reads ONLY the ApproveFlow tables.
public interface ICommonDashboardService
{
    // scopeUserId: null counts every requestor (Admin), a value counts only that user's
    // requests. It must be the SAME scope the Requests grid applies to this user, otherwise
    // a KPI card and the grid it opens would disagree.
    Task<ApproveFlowSectionVM> GetApproveFlowAsync(int? scopeUserId, CancellationToken ct = default);
}

public class CommonDashboardService : ICommonDashboardService
{
    private const int RecentRequestCount = 8;

    private readonly AppDbContext _db;
    public CommonDashboardService(AppDbContext db) { _db = db; }

    public async Task<ApproveFlowSectionVM> GetApproveFlowAsync(int? scopeUserId, CancellationToken ct = default)
    {
        // ONE base query feeds the KPI cards AND the recent grid, carrying exactly the
        // filters the Requests grid applies (Deleted excluded, optional requestor scope).
        // Because both read from this same query, a card total can never disagree with the
        // grid it opens.
        var scoped = _db.Requests
            .AsNoTracking()
            .Where(r => r.Status != RequestStatus.Deleted);

        if (scopeUserId.HasValue)
            scoped = scoped.Where(r => r.RequestorId == scopeUserId.Value);

        var vm = new ApproveFlowSectionVM { IsGlobalScope = !scopeUserId.HasValue };

        foreach (var (label, filterStatus, accent) in CommonDashboardKpi.ApproveFlowCards)
        {
            // The card's own FilterStatus drives its count, and the view hands that same
            // value to /Requests?FilterStatus=… — one value, so the two cannot drift.
            var count = await scoped.CountAsync(r => r.Status == filterStatus, ct);

            vm.Kpis.Add(new CommonKpiCardVM
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
            .Select(r => new CommonRecentRequestVM
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
