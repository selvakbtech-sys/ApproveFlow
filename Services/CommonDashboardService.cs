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
    private const int TrendMonths = 6;
    private const int TopTypes = 6;

    private static readonly string[] Palette =
        { "#2563eb", "#0891b2", "#16a34a", "#d97706", "#7c3aed", "#dc2626", "#64748b" };

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

        // ── Trend: requests raised per month vs. approved, last six months (IST) ──
        var now = IstClock.Now;
        var firstMonth = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, now.Offset).AddMonths(-(TrendMonths - 1));
        var monthly = await scoped
            .Where(r => r.CreatedAt >= firstMonth)
            .GroupBy(r => new { r.CreatedAt.Year, r.CreatedAt.Month, r.Status })
            .Select(g => new { g.Key.Year, g.Key.Month, g.Key.Status, N = g.Count() })
            .ToListAsync(ct);
        for (int i = 0; i < TrendMonths; i++)
        {
            var m = firstMonth.AddMonths(i);
            var rows = monthly.Where(x => x.Year == m.Year && x.Month == m.Month).ToList();
            vm.Trend.Add(new CommonMonthPointVM
            {
                Label     = m.ToString("MMM yy"),
                Primary   = rows.Sum(x => x.N),
                Secondary = rows.Where(x => x.Status == RequestStatus.Approved).Sum(x => x.N),
            });
        }

        // ── By request type: the busiest types, all time ──
        var byType = await scoped
            .GroupBy(r => r.RequestType.TypeName)
            .Select(g => new { Label = g.Key, N = g.Count() })
            .OrderByDescending(x => x.N).ThenBy(x => x.Label)
            .Take(TopTypes)
            .ToListAsync(ct);
        vm.ByType = byType.Select((x, i) => new CommonSliceVM
        {
            Label = x.Label, Count = x.N, Color = Palette[i % Palette.Length],
        }).ToList();

        // ── By priority: what is still waiting on a decision ──
        var openStatuses = new[] { RequestStatus.PendingApproval, RequestStatus.Open, RequestStatus.PartialApproved, RequestStatus.ClarificationNeeded };
        var byPriority = await scoped
            .Where(r => openStatuses.Contains(r.Status))
            .GroupBy(r => r.Priority)
            .Select(g => new { Label = g.Key, N = g.Count() })
            .ToListAsync(ct);
        // Same four values RequestFormVM.Priorities offers on the request form.
        foreach (var (label, color) in new[] { ("Critical", "#7c3aed"), ("High", "#dc2626"), ("Medium", "#d97706"), ("Low", "#16a34a") })
            vm.ByPriority.Add(new CommonSliceVM
            {
                Label = label, Color = color,
                Count = byPriority.Where(x => string.Equals(x.Label, label, StringComparison.OrdinalIgnoreCase)).Sum(x => x.N),
            });

        return vm;
    }
}
