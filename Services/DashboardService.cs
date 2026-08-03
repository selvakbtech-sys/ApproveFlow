namespace ApproveFlow.MVC.Services;

public interface IDashboardService
{
    Task<DashboardVM> GetAsync(int userId, bool isAdmin);
    Task<MyRequestsTabVM> GetMyRequestsTabAsync(int userId);
    Task<MyApprovalsTabVM> GetMyApprovalsTabAsync(int userId);
    Task<MyTerritoryTabVM> GetMyTerritoryTabAsync(int userId);

    // ── My Approvals — reusable building blocks (all scoped to the logged-in approver) ──
    Task<MyApprovalSummaryVM> GetMyApprovalSummaryAsync(int userId);
    Task<List<DashboardSummaryDto>> GetMyApprovalByRequestTypeAsync(int userId);
    Task<List<DashboardSummaryDto>> GetMyApprovalByPriorityAsync(int userId);
    Task<List<DashboardSummaryDto>> GetMyApprovalByLocationAsync(int userId);
    Task<List<DashboardSummaryDto>> GetMyApprovalByDepartmentAsync(int userId);
    Task<List<ApprovalQueueItemVM>> GetRecentApprovalQueueAsync(int userId);
    Task<List<DashboardSummaryDto>> GetRequestTypeSummaryAsync();
    Task<List<DashboardSummaryDto>> GetPrioritySummaryAsync();
    Task<List<DashboardSummaryDto>> GetLocationSummaryAsync();
    Task<List<DashboardSummaryDto>> GetDepartmentSummaryAsync();
}

public class DashboardService : IDashboardService
{
    private readonly AppDbContext _db;
    public DashboardService(AppDbContext db) { _db = db; }

    public async Task<DashboardVM> GetAsync(int userId, bool isAdmin)
    {
        bool isApprover = !isAdmin && await _db.RequestApprovers.AnyAsync(a => a.ApproverUserId == userId);

        var myPendingApproval = await _db.Requests.CountAsync(r => r.RequestorId == userId && (r.Status == "Open" || r.Status == "Pending Approval"));
        var myPartial = await _db.Requests.CountAsync(r => r.RequestorId == userId && r.Status == "Partial Approved");
        var vm = new DashboardVM
        {
            MyPendingApprovals = await _db.RequestApprovers.CountAsync(a => a.ApproverUserId == userId && a.Status == "pending"),
            MyTotal = await _db.Requests.CountAsync(r => r.RequestorId == userId && r.Status != "Deleted"),
            MyDraft = await _db.Requests.CountAsync(r => r.RequestorId == userId && r.Status == "Draft"),
            MyOpen = myPendingApproval,
            MyPartialApproved = myPartial,
            MyPending = myPendingApproval + myPartial,
            MyApproved = await _db.Requests.CountAsync(r => r.RequestorId == userId && r.Status == "Approved"),
            MyRejected = await _db.Requests.CountAsync(r => r.RequestorId == userId && r.Status == "Rejected"),
            MyClarification = await _db.Requests.CountAsync(r => r.RequestorId == userId && r.Status == "Clarification Needed"),
        };

        if (isApprover)
        {
            vm.AssignedToMeTotal = await _db.RequestApprovers.CountAsync(a =>
                a.ApproverUserId == userId &&
                (a.Status == "pending" || a.Status == "approved" || a.Status == "rejected" || a.Status == "clarification"));
            vm.AssignedToMePending = await _db.RequestApprovers.CountAsync(a => a.ApproverUserId == userId && a.Status == "pending");
            vm.ApprovedByMe = await _db.RequestApprovers.CountAsync(a => a.ApproverUserId == userId && a.Status == "approved");
            vm.RejectedByMe = await _db.RequestApprovers.CountAsync(a => a.ApproverUserId == userId && a.Status == "rejected");
            vm.ClarificationByMe = await _db.RequestApprovers.CountAsync(a => a.ApproverUserId == userId && a.Status == "clarification");
        }

        if (isAdmin)
        {
            var adminPendingApproval = await _db.Requests.CountAsync(r => r.Status == "Open" || r.Status == "Pending Approval");
            var adminPartial = await _db.Requests.CountAsync(r => r.Status == "Partial Approved");
            vm.Total = await _db.Requests.CountAsync(r => r.Status != "Deleted");
            vm.Draft = await _db.Requests.CountAsync(r => r.Status == "Draft");
            vm.Open = adminPendingApproval;
            vm.PartialApproved = adminPartial;
            vm.PendingApproval = adminPendingApproval + adminPartial;
            vm.Approved = await _db.Requests.CountAsync(r => r.Status == "Approved");
            vm.Rejected = await _db.Requests.CountAsync(r => r.Status == "Rejected");
            vm.ClarificationNeeded = await _db.Requests.CountAsync(r => r.Status == "Clarification Needed");
            vm.TotalAmountApproved = await _db.Requests.Where(r => r.Status == "Approved" && r.Amount != null).SumAsync(r => r.Amount ?? 0);
            vm.TotalAmountPending = await _db.Requests.Where(r => (r.Status == "Open" || r.Status == "Pending Approval" || r.Status == "Partial Approved") && r.Amount != null).SumAsync(r => r.Amount ?? 0);
        }

        var activityQuery = _db.RequestActionLogs
            .Include(l => l.Request)
            .Include(l => l.ActorUser)
            .AsQueryable();
        if (!isAdmin)
            activityQuery = activityQuery.Where(l => l.Request.RequestorId == userId);
        vm.ActivityFeed = await activityQuery
            .OrderByDescending(l => l.CreatedAt)
            .Take(15)
            .Select(l => new RecentActivityVM
            {
                RequestId = l.RequestId,
                RequestNumber = l.Request.RequestNumber,
                Title = l.Request.Title,
                ActorName = l.ActorUser.FullName,
                Action = l.Action,
                Comment = l.Comment,
                CreatedAt = l.CreatedAt,
            }).ToListAsync();

        if (isAdmin)
        {
            vm.RecentRequests = await _db.Requests
                .Include(r => r.Requestor).Include(r => r.Location)
                .Include(r => r.Department).Include(r => r.RequestType).Include(r => r.Documents)
                .OrderByDescending(r => r.UpdatedAt).Take(8)
                .Select(r => MapItem(r)).ToListAsync();
        }

        vm.MyRecentRequests = await _db.Requests
            .Include(r => r.Requestor).Include(r => r.Location)
            .Include(r => r.Department).Include(r => r.RequestType).Include(r => r.Documents)
            .Where(r => r.RequestorId == userId)
            .OrderByDescending(r => r.UpdatedAt).Take(6)
            .Select(r => MapItem(r)).ToListAsync();

        var urgentQuery = _db.Requests
            .Include(r => r.Requestor).Include(r => r.Location)
            .Include(r => r.Department).Include(r => r.RequestType).Include(r => r.Documents)
            .Where(r => (r.Status == "Open" || r.Status == "Pending Approval" || r.Status == "Partial Approved") && (r.Priority == "Critical" || r.Priority == "High"));
        if (!isAdmin)
            urgentQuery = urgentQuery.Where(r => r.RequestorId == userId);
        vm.UrgentPending = await urgentQuery
            .OrderBy(r => r.Priority == "Critical" ? 0 : 1).ThenBy(r => r.UpdatedAt).Take(6)
            .Select(r => MapItem(r)).ToListAsync();

        var clarificationQuery = _db.Requests
            .Include(r => r.Requestor).Include(r => r.Location)
            .Include(r => r.Department).Include(r => r.RequestType).Include(r => r.Documents)
            .Where(r => r.Status == "Clarification Needed");
        if (!isAdmin)
            clarificationQuery = clarificationQuery.Where(r => r.RequestorId == userId);
        vm.ClarificationItemsTotal = await clarificationQuery.CountAsync();
        vm.ClarificationItems = await clarificationQuery
            .OrderByDescending(r => r.UpdatedAt).Take(10)
            .Select(r => MapItem(r)).ToListAsync();

        vm.MyPendingApprovalRequests = await _db.RequestApprovers
            .Include(a => a.Request).ThenInclude(r => r.Requestor)
            .Include(a => a.Request).ThenInclude(r => r.Location)
            .Include(a => a.Request).ThenInclude(r => r.Department)
            .Include(a => a.Request).ThenInclude(r => r.RequestType)
            .Include(a => a.Request).ThenInclude(r => r.Documents)
            .Where(a => a.ApproverUserId == userId && a.Status == "pending")
            .OrderByDescending(a => a.Request.SubmittedAt)
            .Take(6)
            .Select(a => new MyPendingApprovalItemVM
            {
                RequestId = a.Request.RequestId,
                RequestNumber = a.Request.RequestNumber,
                Title = a.Request.Title,
                Priority = a.Request.Priority,
                RequestorName = a.Request.Requestor.FullName,
                StepLevel = a.StepLevel,
                SubmittedAt = a.Request.SubmittedAt,
                UpdatedAt = a.Request.UpdatedAt,
            }).ToListAsync();

        if (isAdmin)
        {
            vm.ByLocation = await _db.Requests
                .Include(r => r.Location)
                .GroupBy(r => r.Location.LocationName)
                .Select(g => new LocationStatVM
                {
                    LocationName = g.Key,
                    Total = g.Count(),
                    Pending = g.Count(r => r.Status == "Open" || r.Status == "Pending Approval" || r.Status == "Partial Approved"),
                    Approved = g.Count(r => r.Status == "Approved"),
                    Rejected = g.Count(r => r.Status == "Rejected"),
                }).ToListAsync();

            vm.ByDepartment = await _db.Requests
                .Include(r => r.Department)
                .GroupBy(r => r.Department.DeptName)
                .Select(g => new DeptStatVM
                {
                    DeptName = g.Key,
                    Total = g.Count(),
                    Pending = g.Count(r => r.Status == "Open" || r.Status == "Pending Approval" || r.Status == "Partial Approved"),
                    Approved = g.Count(r => r.Status == "Approved"),
                    Rejected = g.Count(r => r.Status == "Rejected"),
                }).OrderByDescending(d => d.Total).ToListAsync();

            vm.ByType = await _db.Requests
                .Include(r => r.RequestType)
                .GroupBy(r => r.RequestType.TypeName)
                .Select(g => new { g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Key, x => x.Count);

            vm.ByPriority = await _db.Requests
                .Where(r => r.Status == "Open" || r.Status == "Pending Approval" || r.Status == "Partial Approved")
                .GroupBy(r => r.Priority)
                .Select(g => new { g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Key, x => x.Count);

            var since = IstClock.Now.AddMonths(-5);
            var monthly = await _db.Requests
                .Where(r => r.CreatedAt >= since)
                .GroupBy(r => new { r.CreatedAt.Year, r.CreatedAt.Month })
                .Select(g => new { g.Key.Year, g.Key.Month, Count = g.Count() })
                .ToListAsync();
            vm.MonthlyTrend = monthly
                .OrderBy(m => m.Year).ThenBy(m => m.Month)
                .ToDictionary(m => $"{m.Year}-{m.Month:D2}", m => m.Count);
        }

        return vm;
    }

    private static ReqListItemVM MapItem(Request r) => new()
    {
        RequestId = r.RequestId,
        RequestNumber = r.RequestNumber,
        Title = r.Title,
        Status = r.Status,
        Priority = r.Priority,
        Amount = r.Amount,
        RequestorName = r.Requestor.FullName,
        LocationName = r.Location.LocationName,
        DeptName = r.Department.DeptName,
        RequestTypeName = r.RequestType.TypeName,
        DocumentCount = r.Documents.Count,
        CreatedAt = r.CreatedAt,
        UpdatedAt = r.UpdatedAt,
    };

    public async Task<MyRequestsTabVM> GetMyRequestsTabAsync(int userId)
    {
        var vm = new MyRequestsTabVM
        {
            MyTotal           = await _db.Requests.CountAsync(r => r.RequestorId == userId && r.Status != "Deleted"),
            MyPending         = await _db.Requests.CountAsync(r => r.RequestorId == userId && (r.Status == "Open" || r.Status == "Pending Approval")),
            MyPartialApproved = await _db.Requests.CountAsync(r => r.RequestorId == userId && r.Status == "Partial Approved"),
            MyApproved        = await _db.Requests.CountAsync(r => r.RequestorId == userId && r.Status == "Approved"),
            MyRejected        = await _db.Requests.CountAsync(r => r.RequestorId == userId && r.Status == "Rejected"),
            MyDraft           = await _db.Requests.CountAsync(r => r.RequestorId == userId && r.Status == "Draft"),
            MyClarification   = await _db.Requests.CountAsync(r => r.RequestorId == userId && r.Status == "Clarification Needed"),
            MyRevoked         = await _db.Requests.CountAsync(r => r.RequestorId == userId && r.Status == "Revoked"),
        };

        var myReqs = _db.Requests.AsNoTracking().Where(r => r.RequestorId == userId && r.Status != "Deleted");

        vm.ByTypeSummary = await myReqs
            .GroupBy(r => r.RequestType.TypeName)
            .Select(g => new DashboardSummaryDto
            {
                CategoryName         = g.Key,
                PendingCount         = g.Count(r => r.Status == "Open" || r.Status == "Pending Approval"),
                PartialApprovedCount = g.Count(r => r.Status == "Partial Approved"),
                ApprovedCount        = g.Count(r => r.Status == "Approved"),
                RejectedCount        = g.Count(r => r.Status == "Rejected"),
                ClarificationCount   = g.Count(r => r.Status == "Clarification Needed"),
            })
            .OrderBy(x => x.CategoryName).ToListAsync();

        var priOrder = new[] { "Critical", "High", "Medium", "Low" };
        var rawPri = await myReqs
            .GroupBy(r => r.Priority)
            .Select(g => new DashboardSummaryDto
            {
                CategoryName         = g.Key,
                PendingCount         = g.Count(r => r.Status == "Open" || r.Status == "Pending Approval"),
                PartialApprovedCount = g.Count(r => r.Status == "Partial Approved"),
                ApprovedCount        = g.Count(r => r.Status == "Approved"),
                RejectedCount        = g.Count(r => r.Status == "Rejected"),
                ClarificationCount   = g.Count(r => r.Status == "Clarification Needed"),
            })
            .ToListAsync();
        vm.ByPrioritySummary = rawPri
            .OrderBy(r => { var i = Array.IndexOf(priOrder, r.CategoryName); return i >= 0 ? i : 99; })
            .ToList();

        vm.ByLocationSummary = await myReqs
            .GroupBy(r => r.Location.LocationName)
            .Select(g => new DashboardSummaryDto
            {
                CategoryName         = g.Key,
                PendingCount         = g.Count(r => r.Status == "Open" || r.Status == "Pending Approval"),
                PartialApprovedCount = g.Count(r => r.Status == "Partial Approved"),
                ApprovedCount        = g.Count(r => r.Status == "Approved"),
                RejectedCount        = g.Count(r => r.Status == "Rejected"),
                ClarificationCount   = g.Count(r => r.Status == "Clarification Needed"),
            })
            .OrderBy(x => x.CategoryName).ToListAsync();

        vm.ByDepartmentSummary = await myReqs
            .GroupBy(r => r.Department.DeptName)
            .Select(g => new DashboardSummaryDto
            {
                CategoryName         = g.Key,
                PendingCount         = g.Count(r => r.Status == "Open" || r.Status == "Pending Approval"),
                PartialApprovedCount = g.Count(r => r.Status == "Partial Approved"),
                ApprovedCount        = g.Count(r => r.Status == "Approved"),
                RejectedCount        = g.Count(r => r.Status == "Rejected"),
                ClarificationCount   = g.Count(r => r.Status == "Clarification Needed"),
            })
            .OrderBy(x => x.CategoryName).ToListAsync();

        vm.RecentRequests = await _db.Requests
            .AsNoTracking()
            .Where(r => r.RequestorId == userId)
            .OrderByDescending(r => r.UpdatedAt)
            .Take(10)
            .Select(r => new MyRecentRequestItemVM
            {
                RequestId       = r.RequestId,
                RequestNumber   = r.RequestNumber,
                Title           = r.Title,
                RequestTypeName = r.RequestType.TypeName,
                Status          = r.Status,
                CreatedAt       = r.CreatedAt,
                CurrentStepLevel = r.CurrentStepLevel,
                CurrentApproverName = r.Approvers
                    .Where(a => a.StepLevel == r.CurrentStepLevel && a.Status == "pending")
                    .Select(a => a.ApproverUser.FullName)
                    .FirstOrDefault(),
            })
            .ToListAsync();

        return vm;
    }

    // ── My Approvals tab ──────────────────────────────────────
    // Thin composition root: every KPI card, summary table and the queue is built
    // from the reusable, single-source methods below so the numbers can never drift
    // apart. All of them filter through MyApprovalScope / the pending predicate, so
    // nothing loads unrelated requests into memory and every query runs AsNoTracking.
    public async Task<MyApprovalsTabVM> GetMyApprovalsTabAsync(int userId)
    {
        var summary = await GetMyApprovalSummaryAsync(userId);
        return new MyApprovalsTabVM
        {
            AssignedTotal       = summary.AssignedTotal,
            PendingMyAction     = summary.PendingMyAction,
            ApprovedByMe        = summary.ApprovedByMe,
            RejectedByMe        = summary.RejectedByMe,
            ClarificationByMe   = summary.ClarificationByMe,
            Escalated           = 0,
            ByTypeSummary       = await GetMyApprovalByRequestTypeAsync(userId),
            ByPrioritySummary   = await GetMyApprovalByPriorityAsync(userId),
            ByLocationSummary   = await GetMyApprovalByLocationAsync(userId),
            ByDepartmentSummary = await GetMyApprovalByDepartmentAsync(userId),
            Queue               = await GetRecentApprovalQueueAsync(userId),
        };
    }

    // ── My Approvals — shared filtered dataset ────────────────
    // The DISTINCT set of requests "assigned to" the logged-in approver, matching the
    // My-Approvals KPI rules. A request qualifies when my approver row is:
    //   • pending        → the request currently awaits MY action (I am the current
    //                       approver — only the CurrentStepLevel row is ever "pending").
    //   • rejected       → I rejected it.
    //   • clarification  → I asked the requester for clarification.
    //   • approved + the request reached final "Approved" → I completed my approval and
    //                       the workflow finished.
    // Requests I approved that are still progressing through higher levels — or were
    // later rejected/clarified by a DIFFERENT approver — now belong to another approver
    // and are deliberately excluded (Dashboard Rule §6: only current-approver requests,
    // no previous/future levels). Deleted requests are never counted. Because the four
    // qualifying states are mutually exclusive per request, the buckets partition the
    // set exactly, so Total == Pending + Approved + Rejected + Clarification always
    // reconciles, and every summary table sums back to the same totals.
    private IQueryable<RequestApprover> MyApprovalScope(int userId) =>
        _db.RequestApprovers
            .AsNoTracking()
            .Where(a => a.ApproverUserId == userId
                     && a.Request.Status != "Deleted"
                     && (a.Status == "pending"
                         || a.Status == "rejected"
                         || a.Status == "clarification"
                         || (a.Status == "approved" && a.Request.Status == "Approved")));

    public async Task<MyApprovalSummaryVM> GetMyApprovalSummaryAsync(int userId)
    {
        var scope = MyApprovalScope(userId);
        // DISTINCT RequestId everywhere so a request can never be counted twice, even
        // in the rare case where the same user approves at more than one level.
        return new MyApprovalSummaryVM
        {
            AssignedTotal     = await scope.Select(a => a.RequestId).Distinct().CountAsync(),
            PendingMyAction   = await scope.Where(a => a.Status == "pending").Select(a => a.RequestId).Distinct().CountAsync(),
            ApprovedByMe      = await scope.Where(a => a.Status == "approved").Select(a => a.RequestId).Distinct().CountAsync(),
            RejectedByMe      = await scope.Where(a => a.Status == "rejected").Select(a => a.RequestId).Distinct().CountAsync(),
            ClarificationByMe = await scope.Where(a => a.Status == "clarification").Select(a => a.RequestId).Distinct().CountAsync(),
        };
    }

    public async Task<List<DashboardSummaryDto>> GetMyApprovalByRequestTypeAsync(int userId)
        => await MyApprovalScope(userId)
            .GroupBy(a => a.Request.RequestType.TypeName)
            .Select(g => new DashboardSummaryDto
            {
                CategoryName       = g.Key,
                PendingCount       = g.Count(a => a.Status == "pending"),
                ApprovedCount      = g.Count(a => a.Status == "approved"),
                RejectedCount      = g.Count(a => a.Status == "rejected"),
                ClarificationCount = g.Count(a => a.Status == "clarification"),
            })
            .OrderBy(x => x.CategoryName).ToListAsync();

    public async Task<List<DashboardSummaryDto>> GetMyApprovalByPriorityAsync(int userId)
    {
        var order = new[] { "Critical", "High", "Medium", "Low" };
        var rows = await MyApprovalScope(userId)
            .GroupBy(a => a.Request.Priority)
            .Select(g => new DashboardSummaryDto
            {
                CategoryName       = g.Key,
                PendingCount       = g.Count(a => a.Status == "pending"),
                ApprovedCount      = g.Count(a => a.Status == "approved"),
                RejectedCount      = g.Count(a => a.Status == "rejected"),
                ClarificationCount = g.Count(a => a.Status == "clarification"),
            })
            .ToListAsync();
        return rows
            .OrderBy(r => { var i = Array.IndexOf(order, r.CategoryName); return i >= 0 ? i : 99; })
            .ToList();
    }

    public async Task<List<DashboardSummaryDto>> GetMyApprovalByLocationAsync(int userId)
        => await MyApprovalScope(userId)
            .GroupBy(a => a.Request.Location.LocationName)
            .Select(g => new DashboardSummaryDto
            {
                CategoryName       = g.Key,
                PendingCount       = g.Count(a => a.Status == "pending"),
                ApprovedCount      = g.Count(a => a.Status == "approved"),
                RejectedCount      = g.Count(a => a.Status == "rejected"),
                ClarificationCount = g.Count(a => a.Status == "clarification"),
            })
            .OrderBy(x => x.CategoryName).ToListAsync();

    public async Task<List<DashboardSummaryDto>> GetMyApprovalByDepartmentAsync(int userId)
        => await MyApprovalScope(userId)
            .GroupBy(a => a.Request.Department.DeptName)
            .Select(g => new DashboardSummaryDto
            {
                CategoryName       = g.Key,
                PendingCount       = g.Count(a => a.Status == "pending"),
                ApprovedCount      = g.Count(a => a.Status == "approved"),
                RejectedCount      = g.Count(a => a.Status == "rejected"),
                ClarificationCount = g.Count(a => a.Status == "clarification"),
            })
            .OrderBy(x => x.CategoryName).ToListAsync();

    // Recent Approval Queue: ONLY requests where I am the current approver (my row is
    // "pending") and the request is still awaiting the workflow (Pending Approval /
    // Partial Approved / legacy Open). Oldest pending first. Row count here equals the
    // Pending KPI, because only the current level ever holds a "pending" row.
    public async Task<List<ApprovalQueueItemVM>> GetRecentApprovalQueueAsync(int userId)
    {
        var now = IstClock.Now;
        var queue = await _db.RequestApprovers
            .AsNoTracking()
            .Where(a => a.ApproverUserId == userId
                     && a.Status == "pending"
                     && a.Request.Status != "Deleted"
                     && (a.Request.Status == "Pending Approval"
                         || a.Request.Status == "Open"
                         || a.Request.Status == "Partial Approved"))
            .OrderBy(a => a.Request.SubmittedAt)   // oldest pending first
            .Select(a => new ApprovalQueueItemVM
            {
                RequestId       = a.RequestId,
                RequestNumber   = a.Request.RequestNumber,
                RequestTypeName = a.Request.RequestType.TypeName,
                RequestorName   = a.Request.Requestor.FullName,
                Status          = a.Status,
                SubmittedAt     = a.Request.SubmittedAt,
            })
            .ToListAsync();

        foreach (var q in queue)
            q.PendingDays = q.SubmittedAt.HasValue
                ? (int)(now - q.SubmittedAt.Value).TotalDays
                : 0;

        return queue;
    }

    public async Task<MyTerritoryTabVM> GetMyTerritoryTabAsync(int userId)
    {
        var myDeptIds = await _db.UserDepartments
            .AsNoTracking()
            .Where(ud => ud.UserId == userId)
            .Select(ud => ud.DepartmentId)
            .ToListAsync();

        var myLocIds = await _db.UserLocations
            .AsNoTracking()
            .Where(ul => ul.UserId == userId)
            .Select(ul => ul.LocationId)
            .ToListAsync();

        // Request Type scope: the user's actively-mapped request types (UserRequestTypeMappings).
        var myTypeIds = await _db.UserRequestTypeMappings
            .AsNoTracking()
            .Where(m => m.UserId == userId && !m.SoftDeleted && m.IsActive)
            .Select(m => m.RequestTypeId)
            .ToListAsync();

        // Territory = Location IN (UserLocations) AND Department IN (UserDepartments)
        //           AND RequestType IN (UserRequestTypeMappings) — all three applied together.
        // IDENTICAL filter to RequestService.GetTerritoryListAsync so the KPI counts here always
        // equal the Territory grid.
        var q = _db.Requests
            .AsNoTracking()
            .Where(r => myLocIds.Contains(r.LocationId)
                     && myDeptIds.Contains(r.DepartmentId)
                     && myTypeIds.Contains(r.RequestTypeId)
                     && r.Status != "Deleted");

        var vm = new MyTerritoryTabVM
        {
            TotalInTerritory = await q.CountAsync(),
            Pending          = await q.CountAsync(r => r.Status == "Open" || r.Status == "Pending Approval"),
            Approved         = await q.CountAsync(r => r.Status == "Approved"),
            Rejected         = await q.CountAsync(r => r.Status == "Rejected"),
            Clarification    = await q.CountAsync(r => r.Status == "Clarification Needed"),
        };

        vm.ByStatus = await q
            .GroupBy(r => r.Status)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);

        vm.ByTypeSummary = await q
            .GroupBy(r => r.RequestType.TypeName)
            .Select(g => new DashboardSummaryDto
            {
                CategoryName          = g.Key,
                PendingCount          = g.Count(r => r.Status == "Open" || r.Status == "Pending Approval"),
                PartialApprovedCount  = g.Count(r => r.Status == "Partial Approved"),
                ApprovedCount         = g.Count(r => r.Status == "Approved"),
                RejectedCount         = g.Count(r => r.Status == "Rejected"),
                ClarificationCount    = g.Count(r => r.Status == "Clarification Needed"),
            })
            .OrderBy(x => x.CategoryName).ToListAsync();

        var priOrder3 = new[] { "Critical", "High", "Medium", "Low" };
        var rawPri3 = await q
            .GroupBy(r => r.Priority)
            .Select(g => new DashboardSummaryDto
            {
                CategoryName          = g.Key,
                PendingCount          = g.Count(r => r.Status == "Open" || r.Status == "Pending Approval"),
                PartialApprovedCount  = g.Count(r => r.Status == "Partial Approved"),
                ApprovedCount         = g.Count(r => r.Status == "Approved"),
                RejectedCount         = g.Count(r => r.Status == "Rejected"),
                ClarificationCount    = g.Count(r => r.Status == "Clarification Needed"),
            })
            .ToListAsync();
        vm.ByPrioritySummary = rawPri3
            .OrderBy(r => { var i = Array.IndexOf(priOrder3, r.CategoryName); return i >= 0 ? i : 99; })
            .ToList();

        vm.ByLocationSummary = await q
            .GroupBy(r => r.Location.LocationName)
            .Select(g => new DashboardSummaryDto
            {
                CategoryName          = g.Key,
                PendingCount          = g.Count(r => r.Status == "Open" || r.Status == "Pending Approval"),
                PartialApprovedCount  = g.Count(r => r.Status == "Partial Approved"),
                ApprovedCount         = g.Count(r => r.Status == "Approved"),
                RejectedCount         = g.Count(r => r.Status == "Rejected"),
                ClarificationCount    = g.Count(r => r.Status == "Clarification Needed"),
            })
            .OrderBy(x => x.CategoryName).ToListAsync();

        vm.ByDepartmentSummary = await q
            .GroupBy(r => r.Department.DeptName)
            .Select(g => new DashboardSummaryDto
            {
                CategoryName          = g.Key,
                PendingCount          = g.Count(r => r.Status == "Open" || r.Status == "Pending Approval"),
                PartialApprovedCount  = g.Count(r => r.Status == "Partial Approved"),
                ApprovedCount         = g.Count(r => r.Status == "Approved"),
                RejectedCount         = g.Count(r => r.Status == "Rejected"),
                ClarificationCount    = g.Count(r => r.Status == "Clarification Needed"),
            })
            .OrderBy(x => x.CategoryName).ToListAsync();

        return vm;
    }

    public async Task<List<DashboardSummaryDto>> GetRequestTypeSummaryAsync()
        => await _db.Requests
            .AsNoTracking()
            .Where(r => r.Status != "Deleted")
            .GroupBy(r => r.RequestType.TypeName)
            .Select(g => new DashboardSummaryDto
            {
                CategoryName      = g.Key,
                PendingCount      = g.Count(r => r.Status == "Open" || r.Status == "Pending Approval" || r.Status == "Partial Approved"),
                ApprovedCount     = g.Count(r => r.Status == "Approved"),
                RejectedCount     = g.Count(r => r.Status == "Rejected"),
                ClarificationCount = g.Count(r => r.Status == "Clarification Needed"),
            })
            .OrderBy(x => x.CategoryName)
            .ToListAsync();

    public async Task<List<DashboardSummaryDto>> GetPrioritySummaryAsync()
    {
        var order = new[] { "Critical", "High", "Medium", "Low" };
        var rows = await _db.Requests
            .AsNoTracking()
            .Where(r => r.Status != "Deleted")
            .GroupBy(r => r.Priority)
            .Select(g => new DashboardSummaryDto
            {
                CategoryName      = g.Key,
                PendingCount      = g.Count(r => r.Status == "Open" || r.Status == "Pending Approval" || r.Status == "Partial Approved"),
                ApprovedCount     = g.Count(r => r.Status == "Approved"),
                RejectedCount     = g.Count(r => r.Status == "Rejected"),
                ClarificationCount = g.Count(r => r.Status == "Clarification Needed"),
            })
            .ToListAsync();

        return rows.OrderBy(r => Array.IndexOf(order, r.CategoryName) is int i && i >= 0 ? i : 99).ToList();
    }

    public async Task<List<DashboardSummaryDto>> GetLocationSummaryAsync()
        => await _db.Requests
            .AsNoTracking()
            .Where(r => r.Status != "Deleted")
            .GroupBy(r => r.Location.LocationName)
            .Select(g => new DashboardSummaryDto
            {
                CategoryName      = g.Key,
                PendingCount      = g.Count(r => r.Status == "Open" || r.Status == "Pending Approval" || r.Status == "Partial Approved"),
                ApprovedCount     = g.Count(r => r.Status == "Approved"),
                RejectedCount     = g.Count(r => r.Status == "Rejected"),
                ClarificationCount = g.Count(r => r.Status == "Clarification Needed"),
            })
            .OrderBy(x => x.CategoryName)
            .ToListAsync();

    public async Task<List<DashboardSummaryDto>> GetDepartmentSummaryAsync()
        => await _db.Requests
            .AsNoTracking()
            .Where(r => r.Status != "Deleted")
            .GroupBy(r => r.Department.DeptName)
            .Select(g => new DashboardSummaryDto
            {
                CategoryName      = g.Key,
                PendingCount      = g.Count(r => r.Status == "Open" || r.Status == "Pending Approval" || r.Status == "Partial Approved"),
                ApprovedCount     = g.Count(r => r.Status == "Approved"),
                RejectedCount     = g.Count(r => r.Status == "Rejected"),
                ClarificationCount = g.Count(r => r.Status == "Clarification Needed"),
            })
            .OrderBy(x => x.CategoryName)
            .ToListAsync();
}
