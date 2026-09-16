namespace ApproveFlow.MVC.Services;

public interface IRequestService
{
    Task<RequestListVM> GetListAsync(RequestListVM filter, int page, int pageSize, int? filterUserId = null, int loggedInUserId = 0);
    Task<RequestListVM> GetMyApprovalsAsync(int approverUserId, RequestListVM filter, int page, int pageSize, string? statusToken = null, bool includeLookups = true);
    Task<RequestListVM> GetTerritoryListAsync(int userId, RequestListVM filter, int page, int pageSize);
    Task<RequestFormVM?> GetDetailAsync(int id, int curUserId);
    Task<RequestFormVM> BuildFormAsync(int userId);
    Task<(bool ok, string? err, int id)> CreateAsync(RequestFormVM vm, int userId);
    Task<(bool ok, string? err)> UpdateAsync(RequestFormVM vm, int userId);
    Task<(bool ok, string? err)> SubmitAsync(int requestId, int userId);
    Task<(bool ok, string? err)> ProcessActionAsync(int requestId, int userId, string action, string? comment, IFormFile? approvalFile = null, string? storagePath = null, int maxFileMb = 20);
    Task<(bool ok, string? err)> SubmitClarificationResponseAsync(int requestId, int userId, string remarks, IFormFile? file = null, string? storagePath = null, int maxFileMb = 20);
    Task<(bool ok, string? err)> RevokeRequestAsync(int requestId, int userId, string remark);
    Task<(bool ok, string? err)> AttachApprovalRemarkAsync(int requestId, int userId, string remark);
    Task<(bool claimed, bool ok, string? err)> TryExecuteDeferredApproveAsync(int pendingId, int requestId, int approverId, string? remark);
    Task<(bool ok, string? err)> AttachApprovalReplyAsync(int requestId, int userId, string? remark, string? attachmentFileName, string? attachmentUrl, string? waMsgId);
    Task<(bool ok, string? err)> DeleteAsync(int requestId, int userId, string userRole = "");
    Task<AgingReportVM> GetAgingAsync(AgingReportVM filter, int? scopeUserId = null);
    Task<WorkflowDefinition?> MatchWorkflowAsync(int locId, int typeId, string priority, int deptId, int userId = 0, decimal? amount = null);
    Task<List<ReqListItemVM>> GetAllForExportAsync(RequestListVM filter, int? scopeUserId = null);
    // Authorization: can this user open/download this specific request? Admin = any;
    // otherwise only the requestor (creator) or an assigned approver.
    Task<bool> CanAccessRequestAsync(int requestId, int userId, string userRole);
    Task<bool> CanViewRequestAsync(int requestId, int userId, string userRole);
}

public class RequestService : IRequestService
{
    private readonly AppDbContext _db;
    private readonly IWhatsAppService _wa;
    private readonly ILogger<RequestService> _log;
    private readonly IUserMappingService _mapping;
    private readonly IConfiguration _cfg;
    private readonly IApplicationUrlService _urlSvc;
    public RequestService(AppDbContext db, IWhatsAppService wa, ILogger<RequestService> log, IUserMappingService mapping, IConfiguration cfg, IApplicationUrlService urlSvc)
    { _db = db; _wa = wa; _log = log; _mapping = mapping; _cfg = cfg; _urlSvc = urlSvc; }

    public async Task<List<ReqListItemVM>> GetAllForExportAsync(RequestListVM filter, int? scopeUserId = null)
    {
        var q = _db.Requests
            .Where(r => r.Status != "Deleted")
            .Include(r => r.Requestor)
            .Include(r => r.Location)
            .Include(r => r.Department)
            .Include(r => r.RequestType)
            .Include(r => r.Documents)
            .AsQueryable();

        // Non-admins may only export their OWN requests (scopeUserId set by caller).
        if (scopeUserId.HasValue)
            q = q.Where(r => r.RequestorId == scopeUserId.Value);

        if (!string.IsNullOrEmpty(filter.SearchTerm))
            q = q.Where(r =>
                r.Title.Contains(filter.SearchTerm) ||
                r.RequestNumber.Contains(filter.SearchTerm) ||
                r.Requestor.FullName.Contains(filter.SearchTerm));

        if (!string.IsNullOrEmpty(filter.FilterStatus))
            q = q.Where(r => r.Status == filter.FilterStatus);

        if (filter.FilterTypeId.HasValue)
            q = q.Where(r => r.RequestTypeId == filter.FilterTypeId);

        if (!string.IsNullOrEmpty(filter.FilterPriority))
            q = q.Where(r => r.Priority == filter.FilterPriority);

        if (filter.FilterDeptId.HasValue)
            q = q.Where(r => r.DepartmentId == filter.FilterDeptId);

        if (filter.FilterLocationId.HasValue)
            q = q.Where(r => r.LocationId == filter.FilterLocationId);

        if (!string.IsNullOrEmpty(filter.FilterDateFrom) && DateOnly.TryParse(filter.FilterDateFrom, out var df))
            q = q.Where(r => r.CreatedAt.Date >= df.ToDateTime(TimeOnly.MinValue));
        if (!string.IsNullOrEmpty(filter.FilterDateTo) && DateOnly.TryParse(filter.FilterDateTo, out var dt))
            q = q.Where(r => r.CreatedAt.Date <= dt.ToDateTime(TimeOnly.MaxValue));

        return await q
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new ReqListItemVM
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
                Description = r.Description,
                Notes = r.Notes,
                DocumentCount = r.Documents.Count,
                CreatedAt = r.CreatedAt,
                UpdatedAt = r.UpdatedAt,
                RevokedByName = r.IsRevoked ? r.Requestor.FullName : null,
                RevokedDate = r.RevokedDate,
                RevokedRemark = r.RevokedRemark,
            })
            .ToListAsync();
    }

    public async Task<RequestListVM> GetListAsync(RequestListVM f, int page, int size, int? filterUserId = null, int loggedInUserId = 0)
    {
        var q = _db.Requests
            .AsNoTracking()
            .Include(r => r.Requestor).Include(r => r.Location)
            .Include(r => r.Department).Include(r => r.RequestType).Include(r => r.Documents)
            .Where(r => r.Status != "Deleted")
            .AsQueryable();

        if (filterUserId.HasValue) q = q.Where(r => r.RequestorId == filterUserId.Value);
        if (!string.IsNullOrEmpty(f.FilterStatus)) q = q.Where(r => r.Status == f.FilterStatus);
        if (f.FilterLocationId.HasValue) q = q.Where(r => r.LocationId == f.FilterLocationId);
        if (f.FilterDeptId.HasValue) q = q.Where(r => r.DepartmentId == f.FilterDeptId);
        if (f.FilterTypeId.HasValue) q = q.Where(r => r.RequestTypeId == f.FilterTypeId);
        if (!string.IsNullOrEmpty(f.FilterPriority)) q = q.Where(r => r.Priority == f.FilterPriority);
        if (!string.IsNullOrEmpty(f.SearchTerm))
            q = q.Where(r => r.Title.Contains(f.SearchTerm) ||
                             r.RequestNumber.Contains(f.SearchTerm) ||
                             r.Requestor.FullName.Contains(f.SearchTerm));
        if (!string.IsNullOrEmpty(f.FilterDateFrom) && DateOnly.TryParse(f.FilterDateFrom, out var df))
            q = q.Where(r => r.CreatedAt.Date >= df.ToDateTime(TimeOnly.MinValue));
        if (!string.IsNullOrEmpty(f.FilterDateTo) && DateOnly.TryParse(f.FilterDateTo, out var dt))
            q = q.Where(r => r.CreatedAt.Date <= dt.ToDateTime(TimeOnly.MaxValue));

        var total = await q.CountAsync();
        // Default order: latest Request Number first. The number is REQ-YY-{RequestId:D6}
        // (assigned from RequestId), so RequestId descending == request-number descending and
        // is robust for legacy/variable-width number strings. Filters/paging apply unchanged.
        var items = await q.OrderByDescending(r => r.RequestId)
            .Skip((page - 1) * size).Take(size)
            .Select(r => new ReqListItemVM
            {
                RequestId = r.RequestId,
                RequestNumber = r.RequestNumber,
                Title = r.Title,
                Status = r.Status,
                Priority = r.Priority,
                Amount = r.Amount,
                RequestorName = r.Requestor.FullName,
                LocationName = r.Location.LocationName,
                LocationCode = r.Location.Code,
                DeptName = r.Department.DeptName,
                RequestTypeName = r.RequestType.TypeName,
                DocumentCount = r.Documents.Count,
                CreatedAt = r.CreatedAt,
                UpdatedAt = r.UpdatedAt,
                IsRequester = r.RequestorId == loggedInUserId,
            }).ToListAsync();

        f.Items = items; f.TotalCount = total; f.PageNumber = page; f.PageSize = size;
        f.Locations = await _db.Locations.Where(l => l.IsActive).OrderBy(l => l.LocationName).ToListAsync();
        f.Departments = await _db.Departments.Where(d => d.IsActive).OrderBy(d => d.DeptName).ToListAsync();
        f.RequestTypes = await _mapping.GetAllowedRequestTypesAsync(loggedInUserId);
        return f;
    }

    // Approval Requests grid for the logged-in approver. The base set is IDENTICAL to
    // DashboardService.MyApprovalScope so the grid totals always equal the dashboard KPI
    // counts: requests where my approver row is pending / rejected / clarification, or
    // approved on a request that reached final "Approved" — never a Deleted request, and
    // deduplicated to the distinct parent requests. The friendly ?status= token from the
    // KPI cards is mapped (via ApprovalStatusFilter) to the request statuses it covers:
    //   (none) → All 12 · Pending → Open+Pending Approval+Partial Approved · Approved →
    //   Approved · Rejected → Rejected · Clarification → Clarification Needed.
    // includeLookups=false skips the Location/Department/RequestType dropdown loads, which only
    // the desktop filter bar needs. The mobile pending page passes false so a search keystroke
    // costs one query instead of four. Defaults to true, so existing callers are unchanged.
    public async Task<RequestListVM> GetMyApprovalsAsync(int approverUserId, RequestListVM f, int page, int size, string? statusToken = null, bool includeLookups = true)
    {
        var q = _db.RequestApprovers
            .AsNoTracking()
            .Where(a => a.ApproverUserId == approverUserId
                     && a.Request.Status != RequestStatus.Deleted
                     && (a.Status == "pending"
                         || a.Status == "rejected"
                         || a.Status == "clarification"
                         || (a.Status == "approved" && a.Request.Status == RequestStatus.Approved)))
            .Select(a => a.Request)
            .Distinct();

        // Status filter comes from the KPI-card token, NOT f.FilterStatus, so a single
        // "Pending" click can span both Pending Approval and Partial Approved.
        var statuses = ApprovalStatusFilter.Map(statusToken);
        if (statuses != null) q = q.Where(r => statuses.Contains(r.Status));

        if (f.FilterLocationId.HasValue) q = q.Where(r => r.LocationId == f.FilterLocationId);
        if (f.FilterDeptId.HasValue) q = q.Where(r => r.DepartmentId == f.FilterDeptId);
        if (f.FilterTypeId.HasValue) q = q.Where(r => r.RequestTypeId == f.FilterTypeId);
        if (!string.IsNullOrEmpty(f.FilterPriority)) q = q.Where(r => r.Priority == f.FilterPriority);
        if (!string.IsNullOrEmpty(f.SearchTerm))
            q = q.Where(r => r.Title.Contains(f.SearchTerm) ||
                             r.RequestNumber.Contains(f.SearchTerm) ||
                             r.Requestor.FullName.Contains(f.SearchTerm));
        if (!string.IsNullOrEmpty(f.FilterDateFrom) && DateOnly.TryParse(f.FilterDateFrom, out var df))
            q = q.Where(r => r.CreatedAt.Date >= df.ToDateTime(TimeOnly.MinValue));
        if (!string.IsNullOrEmpty(f.FilterDateTo) && DateOnly.TryParse(f.FilterDateTo, out var dt))
            q = q.Where(r => r.CreatedAt.Date <= dt.ToDateTime(TimeOnly.MaxValue));

        var total = await q.CountAsync();
        // Default order: latest Request Number first (RequestId descending == request-number
        // descending, since the number is derived from RequestId). Scope/paging unchanged.
        var items = await q.OrderByDescending(r => r.RequestId)
            .Skip((page - 1) * size).Take(size)
            .Select(r => new ReqListItemVM
            {
                RequestId = r.RequestId,
                RequestNumber = r.RequestNumber,
                Title = r.Title,
                Status = r.Status,
                Priority = r.Priority,
                Amount = r.Amount,
                RequestorName = r.Requestor.FullName,
                LocationName = r.Location.LocationName,
                LocationCode = r.Location.Code,
                DeptName = r.Department.DeptName,
                RequestTypeName = r.RequestType.TypeName,
                Description = r.Description,
                Notes = r.Notes,
                DocumentCount = r.Documents.Count,
                CreatedAt = r.CreatedAt,
                UpdatedAt = r.UpdatedAt,
                SubmittedAt = r.SubmittedAt,
                CurrentStepLevel = r.CurrentStepLevel,
                IsRequester = r.RequestorId == approverUserId,
                RevokedByName = r.IsRevoked ? r.Requestor.FullName : null,
                RevokedDate = r.RevokedDate,
                RevokedRemark = r.RevokedRemark,
            }).ToListAsync();

        f.Items = items; f.TotalCount = total; f.PageNumber = page; f.PageSize = size;
        if (includeLookups)
        {
            f.Locations = await _db.Locations.Where(l => l.IsActive).OrderBy(l => l.LocationName).ToListAsync();
            f.Departments = await _db.Departments.Where(d => d.IsActive).OrderBy(d => d.DeptName).ToListAsync();
            f.RequestTypes = await _mapping.GetAllowedRequestTypesAsync(approverUserId);
        }
        return f;
    }

    // ── My Territory grid ─────────────────────────────────────
    // Requests inside the logged-in user's territory — the SAME dept+location scope used by
    // DashboardService.GetMyTerritoryTabAsync, so the Territory KPI counts and this grid can
    // never diverge. Backs the Territory KPI-card click-through for ALL roles (the previous
    // behaviour routed to /Requests/Index, which is own-only for non-admins and global for
    // admins — neither equals the territory, so counts never matched). Deleted requests are
    // excluded, matching the counts.
    //
    // Territory = the user's assigned Department(s) AND Location(s), matched as a pair: a
    // request is in-territory only when its DepartmentId is one of the user's departments
    // AND its LocationId is one of the user's locations. Assignments live in two independent
    // tables (UserDepartments / UserLocations), so "matching ANY of my dept/location pairs"
    // is expressed as (dept IN myDepts) AND (loc IN myLocs) — i.e. the cross-product of the
    // user's departments and locations. This filter is applied FIRST, then the UI filters.
    public async Task<RequestListVM> GetTerritoryListAsync(int userId, RequestListVM f, int page, int size)
    {
        var myDeptIds = await _db.UserDepartments.AsNoTracking()
            .Where(ud => ud.UserId == userId).Select(ud => ud.DepartmentId).ToListAsync();
        var myLocIds = await _db.UserLocations.AsNoTracking()
            .Where(ul => ul.UserId == userId).Select(ul => ul.LocationId).ToListAsync();
        // Request Type scope: the user's actively-mapped types (UserRequestTypeMappings).
        var myTypeIds = await _db.UserRequestTypeMappings.AsNoTracking()
            .Where(m => m.UserId == userId && !m.SoftDeleted && m.IsActive)
            .Select(m => m.RequestTypeId).ToListAsync();

        // Territory = Location IN (UserLocations) AND Department IN (UserDepartments)
        //           AND RequestType IN (UserRequestTypeMappings) — all three applied together.
        // IDENTICAL to DashboardService.GetMyTerritoryTabAsync so the grid total always equals
        // the territory KPI counts.
        var q = _db.Requests
            .AsNoTracking()
            .Include(r => r.Requestor).Include(r => r.Location)
            .Include(r => r.Department).Include(r => r.RequestType).Include(r => r.Documents)
            .Where(r => myLocIds.Contains(r.LocationId)
                        && myDeptIds.Contains(r.DepartmentId)
                        && myTypeIds.Contains(r.RequestTypeId)
                        && r.Status != "Deleted")
            .AsQueryable();

        // Status filter. "Pending Approval" also matches the legacy "Open" alias so the
        // grid total equals the territory Pending KPI (which counts Open + Pending Approval).
        if (!string.IsNullOrEmpty(f.FilterStatus))
        {
            if (f.FilterStatus == RequestStatus.PendingApproval)
                q = q.Where(r => r.Status == RequestStatus.PendingApproval || r.Status == RequestStatus.Open);
            else
                q = q.Where(r => r.Status == f.FilterStatus);
        }
        if (f.FilterLocationId.HasValue) q = q.Where(r => r.LocationId == f.FilterLocationId);
        if (f.FilterDeptId.HasValue) q = q.Where(r => r.DepartmentId == f.FilterDeptId);
        if (f.FilterTypeId.HasValue) q = q.Where(r => r.RequestTypeId == f.FilterTypeId);
        if (!string.IsNullOrEmpty(f.FilterPriority)) q = q.Where(r => r.Priority == f.FilterPriority);
        if (!string.IsNullOrEmpty(f.SearchTerm))
            q = q.Where(r => r.Title.Contains(f.SearchTerm) ||
                             r.RequestNumber.Contains(f.SearchTerm) ||
                             r.Requestor.FullName.Contains(f.SearchTerm));
        if (!string.IsNullOrEmpty(f.FilterDateFrom) && DateOnly.TryParse(f.FilterDateFrom, out var df))
            q = q.Where(r => r.CreatedAt.Date >= df.ToDateTime(TimeOnly.MinValue));
        if (!string.IsNullOrEmpty(f.FilterDateTo) && DateOnly.TryParse(f.FilterDateTo, out var dt))
            q = q.Where(r => r.CreatedAt.Date <= dt.ToDateTime(TimeOnly.MaxValue));

        var total = await q.CountAsync();
        // Default order: latest Request Number first (RequestId descending == request-number
        // descending, since the number is derived from RequestId). Scope/paging unchanged.
        var items = await q.OrderByDescending(r => r.RequestId)
            .Skip((page - 1) * size).Take(size)
            .Select(r => new ReqListItemVM
            {
                RequestId = r.RequestId,
                RequestNumber = r.RequestNumber,
                Title = r.Title,
                Status = r.Status,
                Priority = r.Priority,
                Amount = r.Amount,
                RequestorName = r.Requestor.FullName,
                LocationName = r.Location.LocationName,
                LocationCode = r.Location.Code,
                DeptName = r.Department.DeptName,
                RequestTypeName = r.RequestType.TypeName,
                Description = r.Description,
                Notes = r.Notes,
                DocumentCount = r.Documents.Count,
                CreatedAt = r.CreatedAt,
                UpdatedAt = r.UpdatedAt,
                IsRequester = r.RequestorId == userId,
                RevokedByName = r.IsRevoked ? r.Requestor.FullName : null,
                RevokedDate = r.RevokedDate,
                RevokedRemark = r.RevokedRemark,
            }).ToListAsync();

        f.Items = items; f.TotalCount = total; f.PageNumber = page; f.PageSize = size;
        f.Locations = await _db.Locations.Where(l => l.IsActive).OrderBy(l => l.LocationName).ToListAsync();
        f.Departments = await _db.Departments.Where(d => d.IsActive).OrderBy(d => d.DeptName).ToListAsync();
        f.RequestTypes = await _mapping.GetAllowedRequestTypesAsync(userId);
        return f;
    }

    // Authorization gate for opening/downloading a single request. Admin sees any;
    // everyone else may only access requests they created OR are an assigned approver on.
    // Returns false for non-existent requests too (no existence leak / no access).
    public async Task<bool> CanAccessRequestAsync(int requestId, int userId, string userRole)
    {
        if (string.Equals(userRole, "Admin", StringComparison.OrdinalIgnoreCase))
            return await _db.Requests.AnyAsync(r => r.RequestId == requestId);

        return await _db.Requests.AnyAsync(r => r.RequestId == requestId &&
            (r.RequestorId == userId || r.Approvers.Any(a => a.ApproverUserId == userId)));
    }

    // Read-only view gate. Broader than CanAccessRequestAsync: in addition to the owner,
    // the approvers and admins, it also lets a user OPEN a request that falls inside their
    // territory — the SAME Department AND Location AND (when mapped) Request Type scope as
    // GetTerritoryListAsync / the territory KPI. This is exactly the set the Territory grid
    // lists, so every row a user can see there is openable and nothing outside it. It grants
    // viewing ONLY; every mutating action and attachment download still gates on the stricter
    // CanAccessRequestAsync, so a territory co-worker can look but cannot act.
    public async Task<bool> CanViewRequestAsync(int requestId, int userId, string userRole)
    {
        if (string.Equals(userRole, "Admin", StringComparison.OrdinalIgnoreCase))
            return await _db.Requests.AnyAsync(r => r.RequestId == requestId);

        var myDeptIds = await _db.UserDepartments.AsNoTracking()
            .Where(ud => ud.UserId == userId).Select(ud => ud.DepartmentId).ToListAsync();
        var myLocIds = await _db.UserLocations.AsNoTracking()
            .Where(ul => ul.UserId == userId).Select(ul => ul.LocationId).ToListAsync();
        // Same Request-Type scope as the Territory grid: the territory branch grants viewing
        // only for requests whose Location, Department AND Request Type are all mapped to the
        // user. Owner and approver access are never affected by request type.
        var myTypeIds = await _db.UserRequestTypeMappings.AsNoTracking()
            .Where(m => m.UserId == userId && !m.SoftDeleted && m.IsActive)
            .Select(m => m.RequestTypeId).ToListAsync();

        return await _db.Requests.AnyAsync(r => r.RequestId == requestId &&
            (r.RequestorId == userId
             || r.Approvers.Any(a => a.ApproverUserId == userId)
             || (myLocIds.Contains(r.LocationId)
                 && myDeptIds.Contains(r.DepartmentId)
                 && myTypeIds.Contains(r.RequestTypeId))));
    }

    public async Task<RequestFormVM?> GetDetailAsync(int id, int curUserId)
    {
        var r = await _db.Requests
            .AsSplitQuery()
            .Include(r => r.Requestor).Include(r => r.Location).Include(r => r.Department)
            .Include(r => r.RequestType).Include(r => r.Workflow)
            .Include(r => r.Approvers).ThenInclude(a => a.ApproverUser).ThenInclude(u => u.Role)
            .Include(r => r.ActionLogs).ThenInclude(l => l.ActorUser)
            .Include(r => r.Documents).ThenInclude(d => d.UploadedByUser)
            .FirstOrDefaultAsync(x => x.RequestId == id);
        if (r == null) return null;

        var actStep = r.Approvers.FirstOrDefault(a => a.ApproverUserId == curUserId && a.Status == "pending");

        var approvalAtts = await _db.ApprovalAttachments
            .Include(a => a.UploadedByUser)
            .Where(a => a.RequestId == id)
            .OrderBy(a => a.StepLevel).ThenBy(a => a.UploadedAt)
            .ToListAsync();

        // WhatsApp post-approval reply attachments, mapped to their approved step
        // so they render under the matching "approved" history entry.
        var replies = await _db.ApprovalReplies
            .Where(a => a.RequestId == id && a.AttachmentUrl != null)
            .ToListAsync();
        var stepByApprovalId = r.Approvers.ToDictionary(a => a.ApproverId, a => a.StepLevel);
        var nameByUserId = r.Approvers
            .GroupBy(a => a.ApproverUserId)
            .ToDictionary(g => g.Key, g => g.First().ApproverUser.FullName);

        // Inbound WhatsApp actions for this request — used to flag which history
        // entries originated from WhatsApp (approve/reject/clarification via chat).
        var waInbound = await _db.WhatsAppMessageHistories
            .Where(w => w.RequestId == id && w.Direction == "Inbound"
                     && (w.MessageType == "approved" || w.MessageType == "rejected"
                         || w.MessageType == "clarification" || w.MessageType == "clarification_response"))
            .Select(w => new { w.MessageType, w.WorkflowLevel, w.CreatedBy })
            .ToListAsync();

        bool CameViaWhatsApp(RequestActionLog l) =>
            waInbound.Any(w => w.MessageType == l.Action && w.CreatedBy == l.ActorUserId
                            && (w.WorkflowLevel == null || w.WorkflowLevel == l.StepLevel));

        List<HistoryAttachmentVM> BuildLogAttachments(RequestActionLog l)
        {
            if (l.Action == "submitted" || l.Action == "saved_draft")
                return r.Documents.Select(d => new HistoryAttachmentVM
                    { Id = d.DocumentId, FileName = d.FileName, AttachmentType = "Requestor" }).ToList();

            var list = approvalAtts
                .Where(a => a.StepLevel == l.StepLevel && a.ActionType == l.Action)
                .Select(a => new HistoryAttachmentVM
                    { Id = a.AttachmentId, FileName = a.FileName, AttachmentType = "Approval" }).ToList();

            if (l.Action == "approved")
                foreach (var rep in replies.Where(rp =>
                    stepByApprovalId.TryGetValue(rp.ApprovalId, out var sl) && sl == l.StepLevel))
                    list.Add(new HistoryAttachmentVM
                    {
                        Id = rep.ApprovalReplyId,
                        FileName = rep.AttachmentFileName ?? "Attachment",
                        AttachmentType = "Reply",
                        Url = rep.AttachmentUrl
                    });

            return list;
        }

        return new RequestFormVM
        {
            RequestId = r.RequestId,
            RequestNumber = r.RequestNumber,
            Title = r.Title,
            Description = r.Description,
            Notes = r.Notes,
            RequestTypeId = r.RequestTypeId,
            RequestTypeName = r.RequestType.TypeName,
            Priority = r.Priority,
            Amount = r.Amount,
            LocationId = r.LocationId,
            DepartmentId = r.DepartmentId,
            RequestorName = r.Requestor.FullName,
            LocationName = r.Location.LocationName,
            DeptName = r.Department.DeptName,
            Status = r.Status,
            CreatedAt = r.CreatedAt,
            MatchedWorkflowName = r.Workflow?.WorkflowName,
            CanAct = actStep != null,
            CurrentActingStep = actStep?.StepLevel,
            // The requester (and only the requester) responds to a "Clarification Needed"
            // request via the dedicated Submit Clarification panel — not by editing.
            CanSubmitClarification = r.Status == "Clarification Needed" && r.RequestorId == curUserId,
            // Revoke: only the owner, only while still revocable, and not already revoked.
            CanRevoke = r.RequestorId == curUserId && !r.IsRevoked &&
                (r.Status == "Pending Approval" || r.Status == "Open" || r.Status == "Clarification Needed"),
            IsRevoked = r.IsRevoked,
            // The revoker is always the requestor (only they can revoke), so their name
            // is r.Requestor.FullName — no extra lookup needed.
            RevokedByName = r.IsRevoked ? r.Requestor.FullName : null,
            RevokedDate = r.RevokedDate,
            RevokedRemark = r.RevokedRemark,
            LatestClarificationComment = r.ActionLogs
                .Where(l => l.Action == "clarification")
                .OrderByDescending(l => l.CreatedAt)
                .Select(l => l.Comment)
                .FirstOrDefault(),
            ApprovalChain = r.Approvers.OrderBy(a => a.StepLevel).Select(a => new ApproverStepVM
            {
                StepLevel = a.StepLevel,
                ApproverName = a.ApproverUser.FullName,
                ApproverRole = a.ApproverUser.Role.RoleName,
                ApproverEmail = a.ApproverUser.Email,
                Status = a.Status,
                Comment = a.Comment,
                ActionAt = a.ActionAt,
                ActionText = a.ActionText,
            }).ToList(),
            ActionLog = r.ActionLogs.OrderBy(l => l.CreatedAt).Select(l => new ActionLogVM
            {
                ActorName = l.ActorUser.FullName,
                Action = l.Action,
                Comment = l.Comment,
                StepLevel = l.StepLevel,
                CreatedAt = l.CreatedAt,
                Attachments = BuildLogAttachments(l),
                IsWhatsApp = CameViaWhatsApp(l),
            }).ToList(),
            Documents = r.Documents.Select(d => new DocumentVM
            {
                DocumentId = d.DocumentId,
                FileName = d.FileName,
                FileSizeBytes = d.FileSizeBytes,
                ContentType = d.ContentType,
                UploadedAt = d.UploadedAt,
                UploadedByName = d.UploadedByUser.FullName,
            }).ToList(),
            // Approval Attachments = web/WhatsApp approval-time files (ApprovalAttachments)
            // + optional WhatsApp reply files (ApprovalReplies). Distinct sources, so no
            // duplication. Reply files carry a direct Url; approval files use the download action.
            ApprovalAttachments = approvalAtts.Select(a => new ApprovalAttachmentVM
            {
                AttachmentId = a.AttachmentId,
                StepLevel = a.StepLevel,
                ActionType = a.ActionType,
                FileName = a.FileName,
                FileSizeBytes = a.FileSizeBytes,
                ContentType = a.ContentType,
                UploadedAt = a.UploadedAt,
                UploadedByName = a.UploadedByUser.FullName,
            })
            .Concat(replies.Select(rep => new ApprovalAttachmentVM
            {
                AttachmentId = 0,
                StepLevel = stepByApprovalId.TryGetValue(rep.ApprovalId, out var sl) ? sl : 0,
                ActionType = "approved",
                FileName = rep.AttachmentFileName ?? "Attachment",
                FileSizeBytes = 0,
                UploadedAt = rep.CreatedDate,
                UploadedByName = nameByUserId.TryGetValue(rep.CreatedBy, out var nm) ? nm : "",
                Url = rep.AttachmentUrl,
            }))
            .OrderBy(a => a.StepLevel).ThenBy(a => a.UploadedAt).ToList(),
            RequestTypes = await _mapping.GetAllowedRequestTypesAsync(curUserId),
        };
    }

    public async Task<RequestFormVM> BuildFormAsync(int userId)
    {
        var user = await _db.Users
            .Include(u => u.UserLocations).ThenInclude(ul => ul.Location)
            .Include(u => u.UserDepartments).ThenInclude(ud => ud.Department)
            .FirstAsync(u => u.UserId == userId);

        var userLocations = user.UserLocations.Select(ul => ul.Location)
                                  .Where(l => l.IsActive).OrderBy(l => l.LocationName).ToList();
        var userDepartments = user.UserDepartments.Select(ud => ud.Department)
                                  .Where(d => d.IsActive).OrderBy(d => d.DeptName).ToList();

        var defaultLoc = userLocations.FirstOrDefault();
        var defaultDept = userDepartments.FirstOrDefault();

        return new RequestFormVM
        {
            LocationId = defaultLoc?.LocationId ?? 0,
            DepartmentId = defaultDept?.DepartmentId ?? 0,
            LocationName = defaultLoc?.LocationName ?? "",
            DeptName = defaultDept?.DeptName ?? "",
            RequestorName = user.FullName,
            Status = "Draft",
            // New Request form: STRICT mapped types only (empty when unmapped) so the
            // user can only raise request types assigned via User Mapping.
            RequestTypes = await _mapping.GetMappedRequestTypesAsync(userId),
            Locations = userLocations,
            Departments = userDepartments,
            AllDeptNames = userDepartments.Select(d => d.DeptName).ToList(),
            AllLocationNames = userLocations.Select(l => l.LocationName).ToList(),
        };
    }

    public async Task<(bool ok, string? err, int id)> CreateAsync(RequestFormVM vm, int userId)
    {
        var user = await _db.Users
            .Include(u => u.UserLocations).Include(u => u.UserDepartments)
            .FirstOrDefaultAsync(u => u.UserId == userId);
        if (user == null) return (false, "User not found.", 0);

        var userLocIds = user.UserLocations.Select(l => l.LocationId).ToHashSet();
        var userDeptIds = user.UserDepartments.Select(d => d.DepartmentId).ToHashSet();

        int locId = userLocIds.Contains(vm.LocationId) ? vm.LocationId : (userLocIds.FirstOrDefault());
        int deptId = userDeptIds.Contains(vm.DepartmentId) ? vm.DepartmentId : (userDeptIds.FirstOrDefault());

        var wf = await MatchWorkflowAsync(locId, vm.RequestTypeId, vm.Priority, deptId, userId, vm.Amount);

        var entity = new Request
        {
            Title = vm.Title,
            Description = vm.Description,
            Notes = vm.Notes,
            RequestTypeId = vm.RequestTypeId,
            Priority = vm.Priority,
            Amount = vm.Amount,
            LocationId = locId,
            DepartmentId = deptId,
            RequestorId = userId,
            WorkflowId = wf?.WorkflowId,
            Status = "Draft",
            RequestNumber = "",
        };
        _db.Requests.Add(entity);
        await _db.SaveChangesAsync();

        entity.RequestNumber = $"REQ-{IstClock.Now.Year % 100:D2}-{entity.RequestId:D6}";
        _db.RequestActionLogs.Add(new RequestActionLog
        { RequestId = entity.RequestId, ActorUserId = userId, Action = "saved_draft" });
        await _db.SaveChangesAsync();
        return (true, null, entity.RequestId);
    }

    public async Task<(bool ok, string? err)> UpdateAsync(RequestFormVM vm, int userId)
    {
        var e = await _db.Requests.FindAsync(vm.RequestId);
        if (e == null) return (false, "Not found.");
        if (e.RequestorId != userId) return (false, "Not authorized.");
        if (e.Status != "Draft" && e.Status != "Clarification Needed")
            return (false, "Only Draft or Clarification-Needed requests can be edited.");
        var userLocs = await _db.UserLocations.Where(ul => ul.UserId == userId).Select(ul => ul.LocationId).ToListAsync();
        var userDepts = await _db.UserDepartments.Where(ud => ud.UserId == userId).Select(ud => ud.DepartmentId).ToListAsync();

        e.Title = vm.Title; e.Description = vm.Description;
        e.Notes = vm.Notes; e.RequestTypeId = vm.RequestTypeId;
        e.Priority = vm.Priority; e.Amount = vm.Amount;
        if (userLocs.Contains(vm.LocationId)) e.LocationId = vm.LocationId;
        if (userDepts.Contains(vm.DepartmentId)) e.DepartmentId = vm.DepartmentId;
        await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool ok, string? err)> SubmitAsync(int requestId, int userId)
    {
        var r = await _db.Requests.Include(r => r.Approvers)
            .FirstOrDefaultAsync(x => x.RequestId == requestId);
        if (r == null) return (false, "Not found.");
        if (r.RequestorId != userId) return (false, "Not authorized.");
        if (r.Status != "Draft" && r.Status != "Clarification Needed")
            return (false, $"Cannot submit from status '{r.Status}'.");

        if (!r.Approvers.Any())
        {
            if (!r.WorkflowId.HasValue)
            {
                var matchedWf = await MatchWorkflowAsync(
                    r.LocationId, r.RequestTypeId, r.Priority, r.DepartmentId, userId, r.Amount);
                if (matchedWf != null)
                {
                    r.WorkflowId = matchedWf.WorkflowId;
                    await _db.Requests
                        .Where(x => x.RequestId == requestId)
                        .ExecuteUpdateAsync(s => s.SetProperty(x => x.WorkflowId, r.WorkflowId));
                }
            }

            var wf = r.WorkflowId.HasValue
                ? await _db.WorkflowDefinitions.Include(w => w.Steps)
                    .FirstOrDefaultAsync(w => w.WorkflowId == r.WorkflowId)
                : null;
            if (wf == null || !wf.Steps.Any())
                return (false, "No approval workflow is configured for this request. Contact your Admin.");

            var mgrRoleId = await _db.Roles
                .Where(x => x.RoleName == "Manager")
                .Select(x => x.RoleId)
                .FirstOrDefaultAsync();
            // Load active user candidates for every role referenced by the workflow
            // (role-based steps) plus Manager (auto-assign steps), with their dept/
            // location scope so the most relevant approver can be picked per request.
            var neededRoleIds = wf.Steps
                .Where(s => s.ApproverRoleId.HasValue)
                .Select(s => s.ApproverRoleId!.Value)
                .ToList();
            if (wf.Steps.Any(s => s.IsAutoAssignDeptManager) && mgrRoleId != 0)
                neededRoleIds.Add(mgrRoleId);
            neededRoleIds = neededRoleIds.Distinct().ToList();

            var candidates = await _db.Users
                .AsNoTracking()
                .Where(u => neededRoleIds.Contains(u.RoleId) && u.Status == "active")
                .Select(u => new
                {
                    u.UserId,
                    u.RoleId,
                    DeptIds = u.UserDepartments.Select(d => d.DepartmentId).ToList(),
                    LocIds = u.UserLocations.Select(l => l.LocationId).ToList()
                })
                .ToListAsync();

            // Pick the best-matching active holder of a role for this request:
            // same dept + location first, then same dept, then any active holder.
            int ResolveByRole(int roleId)
            {
                var pool = candidates.Where(u => u.RoleId == roleId).ToList();
                var pick = pool.FirstOrDefault(u => u.DeptIds.Contains(r.DepartmentId) && u.LocIds.Contains(r.LocationId))
                         ?? pool.FirstOrDefault(u => u.DeptIds.Contains(r.DepartmentId))
                         ?? pool.FirstOrDefault();
                return pick?.UserId ?? 0;
            }

            foreach (var step in wf.Steps.OrderBy(s => s.StepLevel))
            {
                int appId;
                if (step.IsAutoAssignDeptManager)
                {
                    appId = ResolveByRole(mgrRoleId);
                    if (appId == 0) return (false, "Cannot auto-assign: no active Manager found for this department and location.");
                }
                else if (step.ApproverRoleId.HasValue)
                {
                    appId = ResolveByRole(step.ApproverRoleId.Value);
                    if (appId == 0)
                        return (false, $"No active user is assigned to the approver role for Step {step.StepLevel}. Assign a user to that role, or edit the workflow.");
                }
                else
                {
                    // Legacy user-pinned workflow (created before role-based steps)
                    appId = step.ApproverUserId ?? 0;
                    if (appId == 0)
                        return (false, $"Step {step.StepLevel} has no approver configured. Edit the workflow and select an approver role.");
                }

                var alreadyAdded = _db.RequestApprovers.Local
                    .Any(a => a.RequestId == requestId && a.ApproverUserId == appId);
                if (alreadyAdded)
                    return (false, $"Approver at Step {step.StepLevel} is the same person as a previous step. Each step must have a different approver.");

                _db.RequestApprovers.Add(new RequestApprover
                {
                    RequestId = requestId,
                    StepLevel = step.StepLevel,
                    ApproverUserId = appId,
                    Status = step.StepLevel == 1 ? "pending" : "waiting"
                });
            }
        }
        else
        {
            var cur = r.Approvers.FirstOrDefault(a => a.StepLevel == r.CurrentStepLevel);
            if (cur != null) { cur.Status = "pending"; cur.Comment = null; }
        }

        var isResubmit = r.Status == "Clarification Needed";

        r.Status = "Pending Approval";
        r.SubmittedAt ??= IstClock.Now;
        _db.RequestActionLogs.Add(new RequestActionLog
        { RequestId = requestId, ActorUserId = userId, Action = "submitted", StepLevel = r.CurrentStepLevel });

        RequestApprover? targetApprover;
        if (isResubmit)
        {
            targetApprover = _db.RequestApprovers.Local
                .Where(a => a.RequestId == requestId && a.StepLevel == r.CurrentStepLevel)
                .FirstOrDefault()
                ?? r.Approvers.FirstOrDefault(a => a.StepLevel == r.CurrentStepLevel);
        }
        else
        {
            targetApprover = _db.RequestApprovers.Local
                .Where(a => a.RequestId == requestId)
                .MinBy(a => a.StepLevel)
                ?? r.Approvers.MinBy(a => a.StepLevel);
        }

        if (targetApprover != null)
        {
            await _db.Notifications
                .Where(n => n.UserId == targetApprover.ApproverUserId
                         && n.RequestId == requestId
                         && n.NotificationType == "action_required"
                         && !n.IsRead)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(n => n.IsRead, true)
                    .SetProperty(n => n.ReadAt, IstClock.Now));

            _db.Notifications.Add(new Notification
            {
                UserId = targetApprover.ApproverUserId,
                RequestId = requestId,
                NotificationType = "action_required",
                Message = $"Request {r.RequestNumber} requires your approval (Step {targetApprover.StepLevel})."
            });
        }

        await _db.SaveChangesAsync();

        try
        {
            if (isResubmit && targetApprover != null)
                await _wa.SendResubmitToApproverAsync(requestId, targetApprover.ApproverUserId, targetApprover.StepLevel);
            else
                await _wa.SendNewRequestToL1Async(requestId);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "SubmitAsync: WhatsApp notification failed for RequestId={Id} IsResubmit={Resubmit}", requestId, isResubmit);
        }

        // Notification Alert is intentionally NOT sent on submission/resubmission — per spec,
        // observer roles are only notified starting from the first approval action onward
        // (see ProcessActionAsync), never at request creation.

        return (true, null);
    }

    public async Task<(bool ok, string? err)> ProcessActionAsync(
        int requestId, int userId, string action, string? comment,
        IFormFile? approvalFile = null, string? storagePath = null, int maxFileMb = 20)
    {
        // Comment rules (identical for every level): Approve optional, Reject & Clarification mandatory.
        if (action == "rejected" && string.IsNullOrWhiteSpace(comment))
            return (false, "Rejection remarks are required.");
        if (action == "clarification" && string.IsNullOrWhiteSpace(comment))
            return (false, "Clarification remarks are required.");

        var r = await _db.Requests.Include(r => r.Approvers)
            .FirstOrDefaultAsync(x => x.RequestId == requestId);
        if (r == null) return (false, "Not found.");

        var acting = r.Approvers.FirstOrDefault(a => a.ApproverUserId == userId &&
            (a.Status == "pending" || (action == "clarification" && a.Status == "clarification")));
        if (acting == null) return (false, "You are not the designated approver for the current step.");

        var now = IstClock.Now;
        var approverNewStatus = action == "clarification" ? "clarification" : action;

        int rowsAffected;
        if (action == "clarification")
        {
            rowsAffected = await _db.RequestApprovers
                .Where(a => a.RequestId == requestId && a.ApproverUserId == userId
                         && (a.Status == "pending" || a.Status == "clarification"))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(a => a.Status, approverNewStatus)
                    .SetProperty(a => a.ActionText, action)
                    .SetProperty(a => a.Comment, comment)
                    .SetProperty(a => a.ActionAt, now));
        }
        else
        {
            rowsAffected = await _db.RequestApprovers
                .Where(a => a.RequestId == requestId && a.ApproverUserId == userId && a.Status == "pending")
                .ExecuteUpdateAsync(s => s
                    .SetProperty(a => a.Status, approverNewStatus)
                    .SetProperty(a => a.ActionText, action)
                    .SetProperty(a => a.Comment, comment)
                    .SetProperty(a => a.ActionAt, now));
        }

        if (rowsAffected == 0)
        {
            _log.LogWarning("ProcessAction: Duplicate ignored RequestId={Id} UserId={Uid} Action={Action}", requestId, userId, action);
            return (false, "This action was already processed.");
        }

        acting = await _db.RequestApprovers.FirstOrDefaultAsync(a => a.RequestId == requestId && a.ApproverUserId == userId);
        if (acting == null) return (false, "Approver record not found.");

        string newStatus;
        int? notifExcludeUserId = null;
        if (action == "rejected")
        {
            newStatus = "Rejected";
            notifExcludeUserId = userId;
        }
        else if (action == "clarification")
        {
            newStatus = "Clarification Needed";
            notifExcludeUserId = userId;
        }
        else
        {
            var next = r.Approvers
                .Where(a => a.StepLevel > acting.StepLevel && a.Status == "waiting")
                .MinBy(a => a.StepLevel);
            if (next != null)
            {
                next.Status = "pending";
                r.CurrentStepLevel = next.StepLevel;
                newStatus = "Partial Approved";
                notifExcludeUserId = next.ApproverUserId;
                await _db.Notifications
                    .Where(n => n.UserId == next.ApproverUserId
                             && n.RequestId == requestId
                             && n.NotificationType == "action_required"
                             && !n.IsRead)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(n => n.IsRead, true)
                        .SetProperty(n => n.ReadAt, IstClock.Now));
                _db.Notifications.Add(new Notification
                {
                    UserId = next.ApproverUserId,
                    RequestId = requestId,
                    NotificationType = "action_required",
                    Message = $"Request {r.RequestNumber} requires your approval (Step {next.StepLevel})."
                });
            }
            else { newStatus = "Approved"; notifExcludeUserId = userId; }
        }

        // Notification Alert is only ever sent after a SUCCESSFUL Approve action (Level
        // Approved or Final Approved) — Reject and Clarification must never trigger it.
        // It's also a pure observer add-on: any failure here must never block the core
        // approval transaction.
        var notifEventLabel = newStatus == "Approved" ? "Final Approved / Workflow Completed" : "Level Approved";
        var notifRoleUsers = new List<User>();
        string? notifViewToken = null;
        if (action == "approved")
        {
            try
            {
                notifRoleUsers = await ResolveNotificationRoleUsersAsync(
                    r.WorkflowId, acting.StepLevel, notifExcludeUserId,
                    r.LocationId, r.DepartmentId, r.RequestTypeId);
                if (notifRoleUsers.Count > 0)
                {
                    notifViewToken = await GetOrCreateViewTokenAsync(requestId);
                    QueueNotificationRoleAlerts(requestId, r.RequestNumber, notifRoleUsers, notifEventLabel,
                        _urlSvc.GetRequestViewLinksText(notifViewToken));
                }
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "ProcessAction: Notification Alert resolution failed RequestId={Id} Action={Action}", requestId, action);
                notifRoleUsers = new List<User>();
                notifViewToken = null;
            }
        }

        r.Status = newStatus;
        _db.RequestActionLogs.Add(new RequestActionLog
        {
            RequestId = requestId,
            ActorUserId = userId,
            Action = action,
            Comment = comment,
            StepLevel = acting.StepLevel
        });

        var msg = action switch
        {
            "approved" => $"Your request {r.RequestNumber} has progressed in the approval chain.",
            "rejected" => $"Your request {r.RequestNumber} was rejected. Reason: {comment}",
            "clarification" => $"Clarification needed on {r.RequestNumber}: {comment}",
            _ => ""
        };
        if (newStatus == "Approved") msg = $"Your request {r.RequestNumber} has been fully approved!";
        _db.Notifications.Add(new Notification
        { UserId = r.RequestorId, RequestId = requestId, NotificationType = action, Message = msg });

        await _db.SaveChangesAsync();

        if (approvalFile != null && approvalFile.Length > 0 && !string.IsNullOrEmpty(storagePath))
        {
            var ext = Path.GetExtension(approvalFile.FileName).ToLowerInvariant();
            var stored = $"{Guid.NewGuid()}{ext}";
            Directory.CreateDirectory(storagePath);
            await using var fs = File.Create(Path.Combine(storagePath, stored));
            await approvalFile.CopyToAsync(fs);
            _db.ApprovalAttachments.Add(new ApprovalAttachment
            {
                RequestId = requestId,
                StepLevel = acting.StepLevel,
                ActionType = action,
                FileName = approvalFile.FileName,
                StoredFileName = stored,
                FileSizeBytes = approvalFile.Length,
                ContentType = approvalFile.ContentType,
                UploadedByUserId = userId,
            });
            await _db.SaveChangesAsync();
        }

        try
        {
            if (action == "approved")
                await _wa.SendApprovalProgressAsync(requestId, userId);
            else if (action == "clarification")
                await _wa.SendClarificationToCreatorAsync(requestId, userId, comment!);
            else if (action == "rejected")
                await _wa.SendRejectionToCreatorAsync(requestId, userId, comment!);

            if (notifRoleUsers.Count > 0 && notifViewToken != null)
            {
                // Both links come from the same token — the template needs them as two
                // separate parameters ({{8}} / {{9}}), not as one newline-joined block.
                var (inOfficeUrl, outOfOfficeUrl) = _urlSvc.GetRequestViewUrlPair(notifViewToken);
                foreach (var u in notifRoleUsers)
                    await _wa.SendNotificationRoleAlertAsync(
                        requestId, u.UserId, notifEventLabel, inOfficeUrl, outOfOfficeUrl, acting.StepLevel);
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "ProcessAction: WhatsApp notification EXCEPTION RequestId={Id} Action={Action} Message={Msg}",
                requestId, action, ex.Message);
            if (ex.InnerException != null)
                _log.LogError("ProcessAction: InnerException={Inner}", ex.InnerException.Message);
        }

        return (true, null);
    }

    // ── Requester's Clarification Response ─────────────────────
    // The requester replies to an approver's clarification request WITHOUT editing the
    // original request. The remarks (+ optional file) are appended as a NEW audit record
    // ("clarification_response"); nothing is overwritten. The step that asked for
    // clarification is returned to "pending" and the request goes back to "Pending
    // Approval" so the SAME approver can now act. The approver is notified via the
    // existing resubmit path (in-app + WhatsApp "Re-submitted" card) — no new message.
    public async Task<(bool ok, string? err)> SubmitClarificationResponseAsync(
        int requestId, int userId, string remarks, IFormFile? file = null,
        string? storagePath = null, int maxFileMb = 20)
    {
        if (string.IsNullOrWhiteSpace(remarks))
            return (false, "Clarification remarks are required.");
        if (remarks.Length > 500)
            return (false, "Clarification remarks cannot exceed 500 characters.");

        var r = await _db.Requests.Include(x => x.Approvers)
            .FirstOrDefaultAsync(x => x.RequestId == requestId);
        if (r == null) return (false, "Not found.");

        // Only the request's owner may respond, and only while it awaits clarification.
        if (r.RequestorId != userId) return (false, "Only the requester can respond to clarification.");
        if (r.Status != "Clarification Needed") return (false, "This request is not awaiting clarification.");

        // The step that requested clarification (current level) — send it back to pending.
        var targetApprover = r.Approvers
            .Where(a => a.StepLevel == r.CurrentStepLevel)
            .FirstOrDefault(a => a.Status == "clarification")
            ?? r.Approvers.FirstOrDefault(a => a.StepLevel == r.CurrentStepLevel);
        if (targetApprover == null)
            return (false, "No approver is currently assigned to this request.");

        targetApprover.Status = "pending";
        targetApprover.Comment = null;
        r.Status = "Pending Approval";

        // Append-only audit record for the requester's response.
        _db.RequestActionLogs.Add(new RequestActionLog
        {
            RequestId = requestId,
            ActorUserId = userId,
            Action = "clarification_response",
            Comment = remarks,
            StepLevel = r.CurrentStepLevel,
        });

        // Notify the approver in-app (mark any stale action_required read, then re-raise).
        await _db.Notifications
            .Where(n => n.UserId == targetApprover.ApproverUserId
                     && n.RequestId == requestId
                     && n.NotificationType == "action_required"
                     && !n.IsRead)
            .ExecuteUpdateAsync(s => s
                .SetProperty(n => n.IsRead, true)
                .SetProperty(n => n.ReadAt, IstClock.Now));
        _db.Notifications.Add(new Notification
        {
            UserId = targetApprover.ApproverUserId,
            RequestId = requestId,
            NotificationType = "action_required",
            Message = $"Clarification provided on {r.RequestNumber} — your approval is required (Step {targetApprover.StepLevel})."
        });

        await _db.SaveChangesAsync();

        // Store the optional attachment as a clarification_response ApprovalAttachment so
        // it renders under this response entry in the history timeline (matched by
        // StepLevel + ActionType). Same storage pattern as approver attachments.
        if (file != null && file.Length > 0 && !string.IsNullOrEmpty(storagePath))
        {
            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            var stored = $"{Guid.NewGuid()}{ext}";
            Directory.CreateDirectory(storagePath);
            await using var fs = File.Create(Path.Combine(storagePath, stored));
            await file.CopyToAsync(fs);
            _db.ApprovalAttachments.Add(new ApprovalAttachment
            {
                RequestId = requestId,
                StepLevel = r.CurrentStepLevel,
                ActionType = "clarification_response",
                FileName = file.FileName,
                StoredFileName = stored,
                FileSizeBytes = file.Length,
                ContentType = file.ContentType,
                UploadedByUserId = userId,
            });
            await _db.SaveChangesAsync();
        }

        // Notify the current approver via the existing resubmit channel (unchanged message).
        try
        {
            await _wa.SendResubmitToApproverAsync(requestId, targetApprover.ApproverUserId, targetApprover.StepLevel);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "SubmitClarificationResponse: WhatsApp notify failed RequestId={Id}", requestId);
        }

        return (true, null);
    }

    // ── Requestor's Revoke ────────────────────────────────────
    // The requestor withdraws their OWN request before it is processed further. This is a
    // DEDICATED flow — it deliberately does NOT reuse any Clarification logic. Allowed only
    // while the request is Pending Approval (legacy "Open") or Clarification Needed; every
    // other status (Partial Approved, Approved, Rejected, Deleted, already Revoked) is
    // refused with the spec's message. All writes run inside a single DB transaction:
    //   • the request is stamped Revoked + the four revoke fields,
    //   • every still-active approver row (pending/waiting/clarification) is set to
    //     "cancelled" so no approver can Approve / Reject / Clarify afterwards,
    //   • a "revoked" audit record is appended to the history timeline,
    //   • the current pending approver(s) get an in-app notification (no WhatsApp/email).
    public async Task<(bool ok, string? err)> RevokeRequestAsync(int requestId, int userId, string remark)
    {
        if (string.IsNullOrWhiteSpace(remark))
            return (false, "Revoke remark is required.");
        if (remark.Length > 500)
            return (false, "Revoke remark cannot exceed 500 characters.");

        var r = await _db.Requests.Include(x => x.Approvers)
            .FirstOrDefaultAsync(x => x.RequestId == requestId);
        if (r == null) return (false, "Not found.");

        // Only the ORIGINAL requestor may revoke — approvers, notification-alert users and
        // admins cannot revoke on someone's behalf.
        if (r.RequestorId != userId)
            return (false, "Only the original requestor can revoke this request.");

        // Revoke is allowed ONLY from Pending Approval (legacy "Open") or Clarification
        // Needed. Everything else — including an already-revoked request — is refused with
        // the exact spec message (also prevents a duplicate revoke).
        bool revocable = !r.IsRevoked &&
            (r.Status == RequestStatus.PendingApproval
             || r.Status == RequestStatus.Open
             || r.Status == RequestStatus.ClarificationNeeded);
        if (!revocable)
            return (false, "This request cannot be revoked because it has already been processed.");

        var now = IstClock.Now;
        var finalRemark = remark.Trim();

        // The current pending approver(s) — captured BEFORE we cancel their rows so we can
        // notify them. At Pending Approval this is the "pending" row; at Clarification
        // Needed it is the "clarification" row.
        var currentApproverIds = r.Approvers
            .Where(a => a.Status == "pending" || a.Status == "clarification")
            .Select(a => a.ApproverUserId)
            .Distinct()
            .ToList();

        try
        {
            r.Status = RequestStatus.Revoked;
            r.IsRevoked = true;
            r.RevokedByUserId = userId;
            r.RevokedDate = now;
            r.RevokedRemark = finalRemark;

            // Terminate the workflow: cancel every still-active approver step so no approver
            // can act. Already-approved / already-rejected rows are left intact as history.
            foreach (var a in r.Approvers.Where(a =>
                a.Status == "pending" || a.Status == "waiting" || a.Status == "clarification"))
            {
                a.Status = "cancelled";
            }

            // Append-only audit record for the revoke — surfaces in the History timeline.
            _db.RequestActionLogs.Add(new RequestActionLog
            {
                RequestId = requestId,
                ActorUserId = userId,
                Action = "revoked",
                Comment = finalRemark,
                StepLevel = r.CurrentStepLevel,
            });

            // Notify the current pending approver(s) in-app. Clear any stale action_required
            // notifications first (as tracked entities, so everything commits in the SAME
            // SaveChanges — one implicit transaction — which is atomic and works with the
            // configured retry execution strategy; a manual transaction would not).
            if (currentApproverIds.Count > 0)
            {
                var stale = await _db.Notifications
                    .Where(n => currentApproverIds.Contains(n.UserId)
                             && n.RequestId == requestId
                             && n.NotificationType == "action_required"
                             && !n.IsRead)
                    .ToListAsync();
                foreach (var n in stale) { n.IsRead = true; n.ReadAt = now; }

                var revokeMsg = $"Request {r.RequestNumber} has been revoked by the Requestor. "
                              + $"Reason: {finalRemark}. No further action is required.";
                foreach (var approverId in currentApproverIds)
                    _db.Notifications.Add(new Notification
                    {
                        UserId = approverId,
                        RequestId = requestId,
                        NotificationType = "revoked",
                        Message = revokeMsg,
                    });
            }

            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "RevokeRequest: failed for RequestId={Id} UserId={Uid}", requestId, userId);
            return (false, "Could not revoke the request. Please try again.");
        }

        _log.LogInformation("RevokeRequest: RequestId={Id} revoked by UserId={Uid}", requestId, userId);
        return (true, null);
    }

    // Attaches an OPTIONAL late remark to an approval the approver already completed via the
    // immediate-approve WhatsApp flow. It does NOT re-run the workflow (status is already
    // advanced and the next approver already notified) — it only records the remark against
    // the existing "approved" action log and the approver's chain entry, so it shows in
    // request history. Reuses the same DbContext; creates no duplicate action/notification.
    public async Task<(bool ok, string? err)> AttachApprovalRemarkAsync(int requestId, int userId, string remark)
    {
        if (string.IsNullOrWhiteSpace(remark)) return (false, "Empty remark.");

        var approver = await _db.RequestApprovers
            .FirstOrDefaultAsync(a => a.RequestId == requestId && a.ApproverUserId == userId && a.Status == "approved");
        if (approver == null) return (false, "No approved step found for this approver.");

        approver.Comment = remark;

        var log = await _db.RequestActionLogs
            .Where(l => l.RequestId == requestId && l.ActorUserId == userId && l.Action == "approved")
            .OrderByDescending(l => l.CreatedAt)
            .FirstOrDefaultAsync();
        if (log != null) log.Comment = remark;

        await _db.SaveChangesAsync();
        _log.LogInformation("AttachApprovalRemark: RequestId={Id} UserId={Uid} remark saved to approval history", requestId, userId);
        return (true, null);
    }

    // Persists a post-approval WhatsApp reply that carries an attachment (and,
    // optionally, a text caption). The optional caption is stored as the approval
    // remark exactly like a text-only reply; the file metadata is recorded in the
    // ApprovalReplies table. The file itself is already saved under
    // wwwroot/uploads/whatsapp/ by the webhook — only metadata is stored here.
    public async Task<(bool ok, string? err)> AttachApprovalReplyAsync(
        int requestId, int userId, string? remark, string? attachmentFileName, string? attachmentUrl, string? waMsgId)
    {
        var approver = await _db.RequestApprovers
            .FirstOrDefaultAsync(a => a.RequestId == requestId && a.ApproverUserId == userId && a.Status == "approved");
        if (approver == null) return (false, "No approved step found for this approver.");

        // Caption → approval remark (mirrors the text-only reply behaviour).
        if (!string.IsNullOrWhiteSpace(remark))
        {
            approver.Comment = remark;
            var log = await _db.RequestActionLogs
                .Where(l => l.RequestId == requestId && l.ActorUserId == userId && l.Action == "approved")
                .OrderByDescending(l => l.CreatedAt)
                .FirstOrDefaultAsync();
            if (log != null) log.Comment = remark;
        }

        _db.ApprovalReplies.Add(new ApprovalReply
        {
            ApprovalId = approver.ApproverId,
            RequestId = requestId,
            ReplyMessage = string.IsNullOrWhiteSpace(remark) ? null : remark,
            AttachmentFileName = attachmentFileName,
            AttachmentUrl = attachmentUrl,
            WhatsAppMessageId = waMsgId,
            CreatedDate = IstClock.Now,
            CreatedBy = userId
        });

        await _db.SaveChangesAsync();
        _log.LogInformation("AttachApprovalReply: RequestId={Id} UserId={Uid} attachment={File} saved", requestId, userId, attachmentFileName);
        return (true, null);
    }

    // ── Deferred WhatsApp Approve — exactly-once execution ────
    // Both the approver's reply (webhook) and the timeout sweep call this. The atomic
    // single-row UPDATE of the session from "WaitingForReply" to "Completed" is the CLAIM:
    // SQL Server serialises the two writers on that row, so only the caller whose update
    // affects the row (claimed=1) proceeds to approve; the loser gets claimed=false and
    // stops. Combined with ProcessActionAsync's own "pending"-status row guard, this makes
    // the approval run exactly once despite double taps, duplicate webhooks, a late reply
    // racing the timeout, or overlapping sweeps. Remark is passed as the approval comment,
    // so the existing notification templates carry it to the requester / next approver.
    // The session row is RETAINED (never deleted) as the audit record of the interaction.
    public async Task<(bool claimed, bool ok, string? err)> TryExecuteDeferredApproveAsync(
        int pendingId, int requestId, int approverId, string? remark)
    {
        var finalRemark = string.IsNullOrWhiteSpace(remark) ? null : remark.Trim();
        var completedOn = IstClock.Now;

        var claimed = await _db.WhatsAppPendingClarifications
            .Where(p => p.Id == pendingId && p.Status == "WaitingForReply")
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.Status, "Completed")
                .SetProperty(p => p.CompletedOn, completedOn)
                .SetProperty(p => p.ApprovalRemark, finalRemark));
        if (claimed == 0)
        {
            _log.LogInformation("DeferredApprove: session {PendingId} already claimed — skipping.", pendingId);
            return (false, false, null);
        }

        bool ok; string? err;
        try
        {
            (ok, err) = await ProcessActionAsync(requestId, approverId, "approved", finalRemark);
        }
        catch
        {
            // The claim is already committed but execution blew up, so this session can
            // never approve. Mark it Expired and rethrow rather than reporting success.
            // Deliberately terminal: releasing it back would have every sweep retry a
            // deterministic failure forever. The approval must be completed from the web UI.
            // Best-effort so a failure here cannot mask the original exception.
            try { await MarkSessionExpiredAsync(pendingId); } catch { /* original error wins */ }
            _log.LogError("DeferredApprove: execution threw — session {PendingId} marked Expired; RequestId={Id} still needs approval.",
                pendingId, requestId);
            throw;
        }

        // The claim won but the approval could not execute (e.g. the step was already
        // actioned through the web UI). Mark the session Expired so "Completed" only ever
        // means "the approval ran".
        if (!ok) await MarkSessionExpiredAsync(pendingId);

        _log.LogInformation("DeferredApprove: executed session {PendingId} RequestId={Id} hasRemark={HasRemark} ok={Ok} err={Err}",
            pendingId, requestId, finalRemark != null, ok, err);
        return (true, ok, err);
    }

    // A claimed session that could not approve. "Expired" keeps "Completed" meaning
    // exactly one thing: the approval ran.
    private async Task MarkSessionExpiredAsync(int pendingId) =>
        await _db.WhatsAppPendingClarifications
            .Where(p => p.Id == pendingId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, "Expired"));

    public async Task<(bool ok, string? err)> DeleteAsync(int requestId, int userId, string userRole = "")
    {
        var r = await _db.Requests.FindAsync(requestId);
        if (r == null) return (false, "Not found.");
        bool isAdmin = userRole.Equals("Admin", StringComparison.OrdinalIgnoreCase);
        if (!isAdmin && r.RequestorId != userId) return (false, "Not authorized.");
        if (r.Status != "Pending Approval" && r.Status != "Open") return (false, "Only requests with Pending Approval status can be deleted.");
        r.Status = "Deleted";
        _db.RequestActionLogs.Add(new RequestActionLog
        {
            RequestId = requestId,
            ActorUserId = userId,
            Action = "deleted",
            Comment = "Request deleted while in Pending Approval status.",
            StepLevel = null
        });
        await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<AgingReportVM> GetAgingAsync(AgingReportVM f, int? scopeUserId = null)
    {
        var q = _db.Requests
            .Include(r => r.Requestor).Include(r => r.Location)
            .Include(r => r.Department).Include(r => r.RequestType)
            .Where(r => r.Status == "Open" || r.Status == "Pending Approval" || r.Status == "Partial Approved" || r.Status == "Clarification Needed")
            .AsQueryable();

        // Non-admins may only see aging for their OWN requests.
        if (scopeUserId.HasValue)
            q = q.Where(r => r.RequestorId == scopeUserId.Value);

        if (!string.IsNullOrEmpty(f.FilterStatus)) q = q.Where(r => r.Status == f.FilterStatus);
        if (f.FilterLocationId.HasValue) q = q.Where(r => r.LocationId == f.FilterLocationId);
        if (f.FilterDeptId.HasValue) q = q.Where(r => r.DepartmentId == f.FilterDeptId);
        if (f.FilterTypeId.HasValue) q = q.Where(r => r.RequestTypeId == f.FilterTypeId);
        if (!string.IsNullOrEmpty(f.FilterPriority)) q = q.Where(r => r.Priority == f.FilterPriority);
        if (!string.IsNullOrEmpty(f.FilterDateFrom) && DateOnly.TryParse(f.FilterDateFrom, out var df))
            q = q.Where(r => r.CreatedAt.Date >= df.ToDateTime(TimeOnly.MinValue));
        if (!string.IsNullOrEmpty(f.FilterDateTo) && DateOnly.TryParse(f.FilterDateTo, out var dt))
            q = q.Where(r => r.CreatedAt.Date <= dt.ToDateTime(TimeOnly.MaxValue));

        var rows = await q.ToListAsync();
        var items = rows.Select(r => new AgingItemVM
        {
            RequestId = r.RequestId,
            RequestNumber = r.RequestNumber,
            Title = r.Title,
            RequestorName = r.Requestor.FullName,
            LocationName = r.Location.LocationName,
            DeptName = r.Department.DeptName,
            RequestTypeName = r.RequestType.TypeName,
            Amount = r.Amount,
            Priority = r.Priority,
            Status = r.Status,
            CreatedAt = r.CreatedAt,
            UpdatedAt = r.UpdatedAt,
            AgeDays = IstClock.DaysSince(r.SubmittedAt ?? r.CreatedAt)
        }).ToList();

        items = f.SortBy switch
        {
            "priority" => items.OrderBy(x =>
                new[] { "Critical", "High", "Medium", "Low" }.ToList().IndexOf(x.Priority)).ToList(),
            "amount" => items.OrderByDescending(x => x.Amount ?? 0).ToList(),
            "status" => items.OrderBy(x => x.Status).ToList(),
            _ => items.OrderByDescending(x => x.AgeDays).ToList(),
        };

        f.Items = items; f.TotalOpen = items.Count;
        f.AvgAge = items.Count > 0 ? (int)items.Average(x => x.AgeDays) : 0;
        f.MaxAge = items.Count > 0 ? items.Max(x => x.AgeDays) : 0;
        f.TotalValue = items.Sum(x => x.Amount ?? 0);
        f.Locations = await _db.Locations.Where(l => l.IsActive).OrderBy(l => l.LocationName).ToListAsync();
        f.Departments = await _db.Departments.Where(d => d.IsActive).OrderBy(d => d.DeptName).ToListAsync();
        f.RequestTypes = await _db.RequestTypes.Where(t => t.IsActive).OrderBy(t => t.TypeName).ToListAsync();
        return f;
    }

    // ── Notification Alert (observer roles) ──────────────────
    // Notification roles are configured per approval level — only the roles mapped to the
    // specific level that was just approved are notified, not the whole workflow's roles.
    //
    // A recipient must satisfy ALL of: Active user · configured observer Role · Location IN
    // (their UserLocations) · Department IN (their UserDepartments) · Request Type IN (their
    // UserRequestTypeMappings) — the request's Location, Department AND Request Type must each
    // be mapped to the user. This mirrors the My-Territory scope so an observer is never
    // notified about a request from another location, department or request type. Uses only the
    // existing mapping tables — no schema change.
    private async Task<List<User>> ResolveNotificationRoleUsersAsync(
        int? workflowId, int stepLevel, int? excludeUserId,
        int locationId, int departmentId, int requestTypeId)
    {
        if (workflowId == null) return new List<User>();
        var roleIds = await _db.WorkflowNotificationRoles
            .Where(x => x.WorkflowId == workflowId.Value && x.StepLevel == stepLevel)
            .Select(x => x.RoleId)
            .ToListAsync();
        if (roleIds.Count == 0) return new List<User>();

        return await _db.Users
            .Where(u => roleIds.Contains(u.RoleId)
                     && u.Status == "active"
                     && u.UserId != excludeUserId
                     && u.UserLocations.Any(ul => ul.LocationId == locationId)
                     && u.UserDepartments.Any(ud => ud.DepartmentId == departmentId)
                     && _db.UserRequestTypeMappings.Any(m => m.UserId == u.UserId
                            && !m.SoftDeleted && m.IsActive && m.RequestTypeId == requestTypeId))
            .ToListAsync();
    }

    private void QueueNotificationRoleAlerts(int requestId, string requestNumber, List<User> recipients, string eventLabel, string viewLink)
    {
        foreach (var u in recipients)
            _db.Notifications.Add(new Notification
            {
                UserId = u.UserId,
                RequestId = requestId,
                NotificationType = "fyi",
                Message = $"{eventLabel}: Request {requestNumber}. View: {viewLink}"
            });
    }

    // Returns the request's view TOKEN. Callers turn it into whatever shape they need —
    // the joined link block for the in-app notification, or the separate In-Office /
    // Out-of-Office URLs the notification_alert template requires as {{8}} and {{9}}.
    private async Task<string> GetOrCreateViewTokenAsync(int requestId)
    {
        var expiryDays = int.TryParse(_cfg["AppSettings:ViewLinkExpiryDays"], out var d) ? d : 30;
        var expiresAt = IstClock.Now.AddDays(expiryDays);
        var existing = await _db.RequestViewTokens.FirstOrDefaultAsync(t => t.RequestId == requestId);
        string token;
        if (existing != null) { existing.ExpiresAt = expiresAt; token = existing.Token; }
        else
        {
            token = GenerateSecureToken();
            _db.RequestViewTokens.Add(new RequestViewToken { RequestId = requestId, Token = token, ExpiresAt = expiresAt });
        }
        return token;
    }

    private static string GenerateSecureToken()
    {
        var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    // Workflow matching is driven ONLY by Location / RequestType / Priority / Department
    // and is completely independent of User Mapping. userId/amount are accepted for
    // diagnostic logging only — they do not influence matching.
    public async Task<WorkflowDefinition?> MatchWorkflowAsync(
        int locId, int typeId, string priority, int deptId, int userId = 0, decimal? amount = null)
    {
        _log.LogInformation(
            "WorkflowMatch: lookup start UserId={UserId} RequestTypeId={TypeId} DepartmentId={DeptId} LocationId={LocId} Priority={Priority} Amount={Amount}",
            userId, typeId, deptId, locId, priority, amount);

        // Load all active workflows that have at least one step, then match in memory so
        // we can report exactly which condition eliminated the candidates. Workflow
        // definitions are few (admin-configured), so this is inexpensive.
        var active = await _db.WorkflowDefinitions
            .Include(w => w.Steps)
            .Where(w => w.IsActive && w.Steps.Any())
            .ToListAsync();

        bool LocOk(WorkflowDefinition w)  => w.LocationId    == null || w.LocationId    == locId;
        bool TypeOk(WorkflowDefinition w) => w.RequestTypeId == null || w.RequestTypeId == typeId;
        bool PrioOk(WorkflowDefinition w) => w.Priority      == null || w.Priority      == priority;
        bool DeptOk(WorkflowDefinition w) => w.DepartmentId  == null || w.DepartmentId  == deptId;

        var candidates = active
            .Where(w => LocOk(w) && TypeOk(w) && PrioOk(w) && DeptOk(w))
            .ToList();

        _log.LogInformation(
            "WorkflowMatch: {ActiveCount} active workflow(s) with steps; {CandidateCount} matched the combination.",
            active.Count, candidates.Count);

        if (candidates.Count == 0)
        {
            // Report exactly which condition(s) failed — a condition "FAILED" when no
            // active workflow satisfied it for the selected values.
            _log.LogWarning(
                "WorkflowMatch: NO workflow matched. Conditions → Location={Loc} RequestType={Type} Priority={Prio} Department={Dept}. " +
                "Passing each individually (of {Active} active): Loc={LocPass}, Type={TypePass}, Prio={PrioPass}, Dept={DeptPass}.",
                active.All(w => !LocOk(w))  ? "FAILED" : "ok",
                active.All(w => !TypeOk(w)) ? "FAILED" : "ok",
                active.All(w => !PrioOk(w)) ? "FAILED" : "ok",
                active.All(w => !DeptOk(w)) ? "FAILED" : "ok",
                active.Count,
                active.Count(LocOk), active.Count(TypeOk), active.Count(PrioOk), active.Count(DeptOk));
            return null;
        }

        var best = candidates
            .OrderByDescending(w =>
                (w.LocationId != null && w.LocationId == locId ? 4 : 0) +
                (w.DepartmentId != null && w.DepartmentId == deptId ? 4 : 0) +
                (w.RequestTypeId != null && w.RequestTypeId == typeId ? 2 : 0) +
                (w.Priority != null && w.Priority == priority ? 1 : 0))
            .First();

        _log.LogInformation("WorkflowMatch: matched WorkflowId={WfId} '{WfName}'.", best.WorkflowId, best.WorkflowName);
        return best;
    }
}
