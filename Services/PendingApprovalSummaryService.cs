namespace ApproveFlow.MVC.Services;

// One request-type row of a pending-approval summary, e.g. "Cash Approval : 10".
public record PendingApprovalTypeCount(string TypeName, int Count);

// Everything the WhatsApp pending reminder needs about one approver.
public record PendingApprovalSummary(int ApproverUserId, string ApproverName, List<PendingApprovalTypeCount> ByType)
{
    public int Total => ByType.Sum(t => t.Count);
}

public interface IPendingApprovalSummaryService
{
    Task<List<PendingApprovalSummary>> GetAllAsync();
    Task<PendingApprovalSummary?> GetForApproverAsync(int approverUserId);
}

// Read-only source of the "pending approvals grouped by request type" figures used by the
// daily WhatsApp reminder. Deliberately has no dependency other than the DbContext so it can
// run from the background reminder service as well as from a request, and so it can never
// form a cycle with IWhatsAppService / IRequestService.
//
// The scope is IDENTICAL to what the existing Pending Approval page shows for
// /Requests/Approval?status=Pending — the approver's own row is still "pending" and the
// parent request is in one of the pending statuses (mapped through ApprovalStatusFilter, the
// same single source of truth the grid and the KPI cards use). That is what keeps the number
// in the reminder equal to the number of rows the link opens.
public class PendingApprovalSummaryService : IPendingApprovalSummaryService
{
    private readonly AppDbContext _db;

    public PendingApprovalSummaryService(AppDbContext db) { _db = db; }

    public Task<List<PendingApprovalSummary>> GetAllAsync() => BuildAsync(null);

    public async Task<PendingApprovalSummary?> GetForApproverAsync(int approverUserId)
        => (await BuildAsync(approverUserId)).FirstOrDefault();

    private async Task<List<PendingApprovalSummary>> BuildAsync(int? approverUserId)
    {
        var pendingStatuses = ApprovalStatusFilter.Map(ApprovalStatusFilter.Pending)!;

        var q = _db.RequestApprovers.AsNoTracking()
            .Where(a => a.Status == "pending" && pendingStatuses.Contains(a.Request.Status));

        if (approverUserId.HasValue)
            q = q.Where(a => a.ApproverUserId == approverUserId.Value);

        // Distinct on the request as well, so a request can never be counted twice for the
        // same approver (mirrors the grid's Distinct() on the parent request).
        var rows = await q
            .Select(a => new
            {
                a.ApproverUserId,
                a.ApproverUser.FullName,
                a.RequestId,
                a.Request.RequestType.TypeName
            })
            .Distinct()
            .ToListAsync();

        return rows
            .GroupBy(r => new { r.ApproverUserId, r.FullName })
            .Select(g => new PendingApprovalSummary(
                g.Key.ApproverUserId,
                string.IsNullOrWhiteSpace(g.Key.FullName) ? "Approver" : g.Key.FullName,
                g.GroupBy(x => x.TypeName)
                 .Select(t => new PendingApprovalTypeCount(
                     string.IsNullOrWhiteSpace(t.Key) ? "Others" : t.Key, t.Count()))
                 .OrderByDescending(t => t.Count).ThenBy(t => t.TypeName)
                 .ToList()))
            .ToList();
    }
}
