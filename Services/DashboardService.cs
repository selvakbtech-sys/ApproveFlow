namespace ApproveFlow.MVC.Services;

public interface IDashboardService
{
    Task<DashboardVM> GetAsync(int userId, bool isAdmin);
    Task<MyRequestsTabVM> GetMyRequestsTabAsync(int userId);
    Task<MyApprovalsTabVM> GetMyApprovalsTabAsync(int userId);
    Task<MyTerritoryTabVM> GetMyTerritoryTabAsync(int userId);
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

        var myOpen = await _db.Requests.CountAsync(r => r.RequestorId == userId && r.Status == "Open");
        var myPartial = await _db.Requests.CountAsync(r => r.RequestorId == userId && r.Status == "Partial Approved");
        var vm = new DashboardVM
        {
            MyPendingApprovals = await _db.RequestApprovers.CountAsync(a => a.ApproverUserId == userId && a.Status == "pending"),
            MyTotal = await _db.Requests.CountAsync(r => r.RequestorId == userId && r.Status != "Deleted"),
            MyDraft = await _db.Requests.CountAsync(r => r.RequestorId == userId && r.Status == "Draft"),
            MyOpen = myOpen,
            MyPartialApproved = myPartial,
            MyPending = myOpen + myPartial,
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
            var adminOpen = await _db.Requests.CountAsync(r => r.Status == "Open");
            var adminPartial = await _db.Requests.CountAsync(r => r.Status == "Partial Approved");
            vm.Total = await _db.Requests.CountAsync(r => r.Status != "Deleted");
            vm.Draft = await _db.Requests.CountAsync(r => r.Status == "Draft");
            vm.Open = adminOpen;
            vm.PartialApproved = adminPartial;
            vm.PendingApproval = adminOpen + adminPartial;
            vm.Approved = await _db.Requests.CountAsync(r => r.Status == "Approved");
            vm.Rejected = await _db.Requests.CountAsync(r => r.Status == "Rejected");
            vm.ClarificationNeeded = await _db.Requests.CountAsync(r => r.Status == "Clarification Needed");
            vm.TotalAmountApproved = await _db.Requests.Where(r => r.Status == "Approved" && r.Amount != null).SumAsync(r => r.Amount ?? 0);
            vm.TotalAmountPending = await _db.Requests.Where(r => (r.Status == "Open" || r.Status == "Partial Approved") && r.Amount != null).SumAsync(r => r.Amount ?? 0);
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
            .Where(r => (r.Status == "Open" || r.Status == "Partial Approved") && (r.Priority == "Critical" || r.Priority == "High"));
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
                    Pending = g.Count(r => r.Status == "Open" || r.Status == "Partial Approved"),
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
                    Pending = g.Count(r => r.Status == "Open" || r.Status == "Partial Approved"),
                    Approved = g.Count(r => r.Status == "Approved"),
                    Rejected = g.Count(r => r.Status == "Rejected"),
                }).OrderByDescending(d => d.Total).ToListAsync();

            vm.ByType = await _db.Requests
                .Include(r => r.RequestType)
                .GroupBy(r => r.RequestType.TypeName)
                .Select(g => new { g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Key, x => x.Count);

            vm.ByPriority = await _db.Requests
                .Where(r => r.Status == "Open" || r.Status == "Partial Approved")
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
            MyTotal         = await _db.Requests.CountAsync(r => r.RequestorId == userId && r.Status != "Deleted"),
            MyPending       = await _db.Requests.CountAsync(r => r.RequestorId == userId && (r.Status == "Open" || r.Status == "Partial Approved")),
            MyApproved      = await _db.Requests.CountAsync(r => r.RequestorId == userId && r.Status == "Approved"),
            MyRejected      = await _db.Requests.CountAsync(r => r.RequestorId == userId && r.Status == "Rejected"),
            MyDraft         = await _db.Requests.CountAsync(r => r.RequestorId == userId && r.Status == "Draft"),
            MyClarification = await _db.Requests.CountAsync(r => r.RequestorId == userId && r.Status == "Clarification Needed"),
        };

        var myReqs = _db.Requests.AsNoTracking().Where(r => r.RequestorId == userId && r.Status != "Deleted");

        vm.ByTypeSummary = await myReqs
            .GroupBy(r => r.RequestType.TypeName)
            .Select(g => new DashboardSummaryDto
            {
                CategoryName       = g.Key,
                PendingCount       = g.Count(r => r.Status == "Open" || r.Status == "Partial Approved"),
                ApprovedCount      = g.Count(r => r.Status == "Approved"),
                RejectedCount      = g.Count(r => r.Status == "Rejected"),
                ClarificationCount = g.Count(r => r.Status == "Clarification Needed"),
            })
            .OrderBy(x => x.CategoryName).ToListAsync();

        var priOrder = new[] { "Critical", "High", "Medium", "Low" };
        var rawPri = await myReqs
            .GroupBy(r => r.Priority)
            .Select(g => new DashboardSummaryDto
            {
                CategoryName       = g.Key,
                PendingCount       = g.Count(r => r.Status == "Open" || r.Status == "Partial Approved"),
                ApprovedCount      = g.Count(r => r.Status == "Approved"),
                RejectedCount      = g.Count(r => r.Status == "Rejected"),
                ClarificationCount = g.Count(r => r.Status == "Clarification Needed"),
            })
            .ToListAsync();
        vm.ByPrioritySummary = rawPri
            .OrderBy(r => { var i = Array.IndexOf(priOrder, r.CategoryName); return i >= 0 ? i : 99; })
            .ToList();

        vm.ByLocationSummary = await myReqs
            .GroupBy(r => r.Location.LocationName)
            .Select(g => new DashboardSummaryDto
            {
                CategoryName       = g.Key,
                PendingCount       = g.Count(r => r.Status == "Open" || r.Status == "Partial Approved"),
                ApprovedCount      = g.Count(r => r.Status == "Approved"),
                RejectedCount      = g.Count(r => r.Status == "Rejected"),
                ClarificationCount = g.Count(r => r.Status == "Clarification Needed"),
            })
            .OrderBy(x => x.CategoryName).ToListAsync();

        vm.ByDepartmentSummary = await myReqs
            .GroupBy(r => r.Department.DeptName)
            .Select(g => new DashboardSummaryDto
            {
                CategoryName       = g.Key,
                PendingCount       = g.Count(r => r.Status == "Open" || r.Status == "Partial Approved"),
                ApprovedCount      = g.Count(r => r.Status == "Approved"),
                RejectedCount      = g.Count(r => r.Status == "Rejected"),
                ClarificationCount = g.Count(r => r.Status == "Clarification Needed"),
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

    public async Task<MyApprovalsTabVM> GetMyApprovalsTabAsync(int userId)
    {
        var vm = new MyApprovalsTabVM
        {
            AssignedTotal    = await _db.RequestApprovers.CountAsync(a => a.ApproverUserId == userId
                                   && (a.Status == "pending" || a.Status == "approved" || a.Status == "rejected" || a.Status == "clarification")),
            PendingMyAction  = await _db.RequestApprovers.CountAsync(a => a.ApproverUserId == userId && a.Status == "pending"),
            ApprovedByMe     = await _db.RequestApprovers.CountAsync(a => a.ApproverUserId == userId && a.Status == "approved"),
            RejectedByMe     = await _db.RequestApprovers.CountAsync(a => a.ApproverUserId == userId && a.Status == "rejected"),
            ClarificationByMe = await _db.RequestApprovers.CountAsync(a => a.ApproverUserId == userId && a.Status == "clarification"),
            Escalated        = 0,
        };

        var mySteps = _db.RequestApprovers.AsNoTracking()
            .Where(a => a.ApproverUserId == userId &&
                        (a.Status == "pending" || a.Status == "approved" ||
                         a.Status == "rejected" || a.Status == "clarification"));

        vm.ByTypeSummary = await mySteps
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

        var priOrder2 = new[] { "Critical", "High", "Medium", "Low" };
        var rawPri2 = await mySteps
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
        vm.ByPrioritySummary = rawPri2
            .OrderBy(r => { var i = Array.IndexOf(priOrder2, r.CategoryName); return i >= 0 ? i : 99; })
            .ToList();

        vm.ByLocationSummary = await mySteps
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

        vm.ByDepartmentSummary = await mySteps
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

        var now = IstClock.Now;
        vm.Queue = await _db.RequestApprovers
            .AsNoTracking()
            .Where(a => a.ApproverUserId == userId && a.Status == "pending")
            .OrderBy(a => a.Request.SubmittedAt)
            .Take(20)
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

        foreach (var q in vm.Queue)
            q.PendingDays = q.SubmittedAt.HasValue
                ? (int)(now - q.SubmittedAt.Value).TotalDays
                : 0;

        return vm;
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

        var q = _db.Requests
            .AsNoTracking()
            .Where(r => (myDeptIds.Contains(r.DepartmentId) || myLocIds.Contains(r.LocationId)) && r.Status != "Deleted");

        var vm = new MyTerritoryTabVM
        {
            TotalInTerritory = await q.CountAsync(),
            Pending          = await q.CountAsync(r => r.Status == "Open" || r.Status == "Partial Approved"),
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
                CategoryName       = g.Key,
                PendingCount       = g.Count(r => r.Status == "Open" || r.Status == "Partial Approved"),
                ApprovedCount      = g.Count(r => r.Status == "Approved"),
                RejectedCount      = g.Count(r => r.Status == "Rejected"),
                ClarificationCount = g.Count(r => r.Status == "Clarification Needed"),
            })
            .OrderBy(x => x.CategoryName).ToListAsync();

        var priOrder3 = new[] { "Critical", "High", "Medium", "Low" };
        var rawPri3 = await q
            .GroupBy(r => r.Priority)
            .Select(g => new DashboardSummaryDto
            {
                CategoryName       = g.Key,
                PendingCount       = g.Count(r => r.Status == "Open" || r.Status == "Partial Approved"),
                ApprovedCount      = g.Count(r => r.Status == "Approved"),
                RejectedCount      = g.Count(r => r.Status == "Rejected"),
                ClarificationCount = g.Count(r => r.Status == "Clarification Needed"),
            })
            .ToListAsync();
        vm.ByPrioritySummary = rawPri3
            .OrderBy(r => { var i = Array.IndexOf(priOrder3, r.CategoryName); return i >= 0 ? i : 99; })
            .ToList();

        vm.ByLocationSummary = await q
            .GroupBy(r => r.Location.LocationName)
            .Select(g => new DashboardSummaryDto
            {
                CategoryName       = g.Key,
                PendingCount       = g.Count(r => r.Status == "Open" || r.Status == "Partial Approved"),
                ApprovedCount      = g.Count(r => r.Status == "Approved"),
                RejectedCount      = g.Count(r => r.Status == "Rejected"),
                ClarificationCount = g.Count(r => r.Status == "Clarification Needed"),
            })
            .OrderBy(x => x.CategoryName).ToListAsync();

        vm.ByDepartmentSummary = await q
            .GroupBy(r => r.Department.DeptName)
            .Select(g => new DashboardSummaryDto
            {
                CategoryName       = g.Key,
                PendingCount       = g.Count(r => r.Status == "Open" || r.Status == "Partial Approved"),
                ApprovedCount      = g.Count(r => r.Status == "Approved"),
                RejectedCount      = g.Count(r => r.Status == "Rejected"),
                ClarificationCount = g.Count(r => r.Status == "Clarification Needed"),
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
                PendingCount      = g.Count(r => r.Status == "Open" || r.Status == "Partial Approved"),
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
                PendingCount      = g.Count(r => r.Status == "Open" || r.Status == "Partial Approved"),
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
                PendingCount      = g.Count(r => r.Status == "Open" || r.Status == "Partial Approved"),
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
                PendingCount      = g.Count(r => r.Status == "Open" || r.Status == "Partial Approved"),
                ApprovedCount     = g.Count(r => r.Status == "Approved"),
                RejectedCount     = g.Count(r => r.Status == "Rejected"),
                ClarificationCount = g.Count(r => r.Status == "Clarification Needed"),
            })
            .OrderBy(x => x.CategoryName)
            .ToListAsync();
}
