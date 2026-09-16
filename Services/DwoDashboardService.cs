namespace ApproveFlow.MVC.Services;

using ApproveFlow.MVC.Models.Dwo;

// Supplies the DWO half of the Common Dashboard from the DWO tables.
//
// The Common Dashboard never queries these tables itself — it asks through
// IDwoDashboardService and renders whatever comes back. Everything here is therefore scoped
// by the DWO module's own rules, not ApproveFlow's.
//
// Register it in Program.cs beside the other services:
//     builder.Services.AddScoped<IDwoDashboardService, DwoDashboardService>();
public class DwoDashboardService : IDwoDashboardService
{
    private const int RecentDiscussionCount = 8;

    // Status values as the Discussions table stores them. They live here, in one place, so a
    // card count and the DWO screen it opens can be kept in step by editing a single line.
    private static readonly string[] PendingStatuses = { "Pending", "Open", "In Progress" };
    private const string ClosedStatus = "Closed";

    private readonly AppDbContext _db;
    public DwoDashboardService(AppDbContext db) { _db = db; }

    public async Task<DwoDashboardVM> GetDashboardAsync(DwoUserContext ctx, CancellationToken ct = default)
    {
        // ONE base query feeds every card AND the recent grid, so no card total can disagree
        // with the list it opens. Admin sees every discussion; everyone else sees the ones
        // they raised.
        var scoped = _db.Discussions.AsNoTracking();
        if (!ctx.IsAdmin)
            scoped = scoped.Where(d => d.CreatedBy == ctx.UserId);

        var vm = new DwoDashboardVM();

        vm.Kpis.Add(new DwoKpiCardVM
        {
            Key   = DwoKpiKeys.DiscussionRegister,
            Count = await scoped.CountAsync(ct),
        });

        // Counted against the same scope as every other card, so an Admin's "My Discussions"
        // is their own register entry rather than a second total of everyone's.
        vm.Kpis.Add(new DwoKpiCardVM
        {
            Key   = DwoKpiKeys.MyDiscussions,
            Count = await scoped.CountAsync(d => d.CreatedBy == ctx.UserId, ct),
        });

        vm.Kpis.Add(new DwoKpiCardVM
        {
            Key   = DwoKpiKeys.PendingAction,
            Count = await scoped.CountAsync(d => PendingStatuses.Contains(d.Status), ct),
        });

        vm.Kpis.Add(new DwoKpiCardVM
        {
            Key   = DwoKpiKeys.Closed,
            Count = await scoped.CountAsync(d => d.Status == ClosedStatus, ct),
        });

        // ManagementDiscussion and Reports are deliberately NOT returned. The Discussions
        // table carries nothing that identifies a management discussion, and Reports is a
        // destination rather than a quantity. A card this method omits renders as "—" with
        // its link intact — which is accurate — where a 0 would assert there are none.
        // Add a card here the moment a column can answer it.

        vm.RecentDiscussions = await scoped
            .OrderByDescending(d => d.DueDate)
            .Take(RecentDiscussionCount)
            .Select(d => new DwoRecentDiscussionVM
            {
                DiscussionNo = d.DiscussionNo,
                Project      = d.Project,
                Department   = d.Department,
                Status       = d.Status,
                DueDate      = d.DueDate,
                // Left null: the dashboard renders the number as plain text rather than a
                // link. Set it once the detail route is known, e.g. $"/DWO/Detail/{d.Id}".
                Url          = null,
            })
            .ToListAsync(ct);

        return vm;
    }
}
