namespace ApproveFlow.MVC.Controllers;

// Lightweight, mobile-first landing page for approvers arriving from the WhatsApp Pending
// Approval Reminder (/Mobile/PendingApprovals). It is READ-ONLY: every card links into the
// EXISTING request detail page, where Approve / Reject / Clarification keep working exactly as
// before. No dashboard, KPI, chart, report, master or admin surface is reachable from here.
//
// The feed comes from IRequestService.GetMyApprovalsAsync — the same method behind the desktop
// Approval Requests grid, so the approver scope and status mapping are shared and the two
// screens can never show different sets. Chips only vary filter fields that already exist.
public class MobileController : BaseCtrl
{
    // Small page: fast first paint on a phone, then lazy-load on demand. Comfortably covers
    // the "page beyond 100 records" requirement.
    private const int PageSize = 20;

    private readonly IRequestService _svc;
    private readonly IPendingApprovalSummaryService _pending;

    public MobileController(IRequestService svc, IPendingApprovalSummaryService pending)
    { _svc = svc; _pending = pending; }

    public async Task<IActionResult> PendingApprovals(string? chip, string? q)
    {
        if (DenyIfNoPerm("Requests") is { } deny) return deny;

        var selected = Normalise(chip);
        var summary = await _pending.GetForApproverAsync(UserId);

        return View(new MobilePendingVM
        {
            ApproverName = User.FindFirstValue(ClaimTypes.Name) ?? "Approver",
            TotalPending = summary?.Total ?? 0,
            Chip = selected,
            SearchTerm = q,
            List = await LoadAsync(selected, q, 1),
        });
    }

    // Card feed for instant search, chip switching and lazy loading. Renders the SAME partial
    // the first page uses, so there is exactly one card template.
    public async Task<IActionResult> Cards(string? chip, string? q, int page = 1)
    {
        if (DenyIfNoPerm("Requests") is { } deny) return deny;
        return PartialView("_PendingCards", await LoadAsync(Normalise(chip), q, Math.Max(1, page)));
    }

    // Unknown/absent chip falls back to Pending — the state the WhatsApp link arrives in.
    private static string Normalise(string? chip) =>
        MobileChip.Items.Any(c => c.Key == chip) ? chip! : MobileChip.Pending;

    // Maps a chip onto the existing RequestListVM filters. "All" drops the status token so the
    // approver sees everything assigned to them; every other chip stays inside Pending.
    private Task<RequestListVM> LoadAsync(string chip, string? q, int page)
    {
        var f = new RequestListVM { SearchTerm = q };
        var today = IstClock.Now.Date;

        switch (chip)
        {
            case MobileChip.High:
                f.FilterPriority = "High";
                break;
            case MobileChip.Today:
                f.FilterDateFrom = f.FilterDateTo = today.ToString("yyyy-MM-dd");
                break;
            case MobileChip.Week:
                // Week starts Monday.
                f.FilterDateFrom = today.AddDays(-(((int)today.DayOfWeek + 6) % 7)).ToString("yyyy-MM-dd");
                break;
        }

        var status = chip == MobileChip.All ? null : ApprovalStatusFilter.Pending;
        // includeLookups:false — the mobile page has no dropdowns to fill.
        return _svc.GetMyApprovalsAsync(UserId, f, page, PageSize, status, includeLookups: false);
    }
}
