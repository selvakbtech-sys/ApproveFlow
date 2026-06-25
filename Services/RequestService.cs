namespace ApproveFlow.MVC.Services;

public interface IRequestService
{
    Task<RequestListVM> GetListAsync(RequestListVM filter, int page, int pageSize, int? filterUserId = null);
    Task<RequestListVM> GetMyApprovalsAsync(int approverUserId, RequestListVM filter, int page, int pageSize);
    Task<RequestFormVM?> GetDetailAsync(int id, int curUserId);
    Task<RequestFormVM> BuildFormAsync(int userId);
    Task<(bool ok, string? err, int id)> CreateAsync(RequestFormVM vm, int userId);
    Task<(bool ok, string? err)> UpdateAsync(RequestFormVM vm, int userId);
    Task<(bool ok, string? err)> SubmitAsync(int requestId, int userId);
    Task<(bool ok, string? err)> ProcessActionAsync(int requestId, int userId, string action, string? comment, IFormFile? approvalFile = null, string? storagePath = null, int maxFileMb = 20);
    Task<(bool ok, string? err)> DeleteAsync(int requestId, int userId, string userRole = "");
    Task<AgingReportVM> GetAgingAsync(AgingReportVM filter);
    Task<WorkflowDefinition?> MatchWorkflowAsync(int locId, int typeId, string priority, int deptId);
    Task<List<ReqListItemVM>> GetAllForExportAsync(RequestListVM filter);
}

public class RequestService : IRequestService
{
    private readonly AppDbContext _db;
    private readonly IWhatsAppService _wa;
    private readonly ILogger<RequestService> _log;
    public RequestService(AppDbContext db, IWhatsAppService wa, ILogger<RequestService> log)
    { _db = db; _wa = wa; _log = log; }

    public async Task<List<ReqListItemVM>> GetAllForExportAsync(RequestListVM filter)
    {
        var q = _db.Requests
            .Where(r => r.Status != "Deleted")
            .Include(r => r.Requestor)
            .Include(r => r.Location)
            .Include(r => r.Department)
            .Include(r => r.RequestType)
            .Include(r => r.Documents)
            .AsQueryable();

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
                DocumentCount = r.Documents.Count,
                CreatedAt = r.CreatedAt,
                UpdatedAt = r.UpdatedAt
            })
            .ToListAsync();
    }

    public async Task<RequestListVM> GetListAsync(RequestListVM f, int page, int size, int? filterUserId = null)
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
        var items = await q.OrderByDescending(r => r.UpdatedAt)
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
                DeptName = r.Department.DeptName,
                RequestTypeName = r.RequestType.TypeName,
                DocumentCount = r.Documents.Count,
                CreatedAt = r.CreatedAt,
                UpdatedAt = r.UpdatedAt,
            }).ToListAsync();

        f.Items = items; f.TotalCount = total; f.PageNumber = page; f.PageSize = size;
        f.Locations = await _db.Locations.Where(l => l.IsActive).OrderBy(l => l.LocationName).ToListAsync();
        f.Departments = await _db.Departments.Where(d => d.IsActive).OrderBy(d => d.DeptName).ToListAsync();
        f.RequestTypes = await _db.RequestTypes.Where(t => t.IsActive).OrderBy(t => t.TypeName).ToListAsync();
        return f;
    }

    public async Task<RequestListVM> GetMyApprovalsAsync(int approverUserId, RequestListVM f, int page, int size)
    {
        var q = _db.RequestApprovers
            .AsNoTracking()
            .Include(a => a.Request).ThenInclude(r => r.Requestor)
            .Include(a => a.Request).ThenInclude(r => r.Location)
            .Include(a => a.Request).ThenInclude(r => r.Department)
            .Include(a => a.Request).ThenInclude(r => r.RequestType)
            .Include(a => a.Request).ThenInclude(r => r.Documents)
            .Where(a => a.ApproverUserId == approverUserId && a.Status == "pending")
            .Select(a => a.Request)
            .AsQueryable();

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
        var items = await q.OrderByDescending(r => r.UpdatedAt)
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
                DeptName = r.Department.DeptName,
                RequestTypeName = r.RequestType.TypeName,
                DocumentCount = r.Documents.Count,
                CreatedAt = r.CreatedAt,
                UpdatedAt = r.UpdatedAt,
            }).ToListAsync();

        f.Items = items; f.TotalCount = total; f.PageNumber = page; f.PageSize = size;
        f.Locations = await _db.Locations.Where(l => l.IsActive).OrderBy(l => l.LocationName).ToListAsync();
        f.Departments = await _db.Departments.Where(d => d.IsActive).OrderBy(d => d.DeptName).ToListAsync();
        f.RequestTypes = await _db.RequestTypes.Where(t => t.IsActive).OrderBy(t => t.TypeName).ToListAsync();
        return f;
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
                Attachments = (l.Action == "submitted" || l.Action == "saved_draft")
                    ? r.Documents.Select(d => new HistoryAttachmentVM
                        { Id = d.DocumentId, FileName = d.FileName, AttachmentType = "Requestor" }).ToList()
                    : approvalAtts
                        .Where(a => a.StepLevel == l.StepLevel && a.ActionType == l.Action)
                        .Select(a => new HistoryAttachmentVM
                            { Id = a.AttachmentId, FileName = a.FileName, AttachmentType = "Approval" }).ToList(),
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
            }).ToList(),
            RequestTypes = await _db.RequestTypes.Where(t => t.IsActive).ToListAsync(),
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
            RequestTypes = await _db.RequestTypes.Where(t => t.IsActive).OrderBy(t => t.TypeName).ToListAsync(),
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

        var wf = await MatchWorkflowAsync(locId, vm.RequestTypeId, vm.Priority, deptId);

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

        entity.RequestNumber = $"REQ-{IstClock.Now.Year}-{entity.RequestId:D4}";
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
                    r.LocationId, r.RequestTypeId, r.Priority, r.DepartmentId);
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
            var mgrCandidates = wf.Steps.Any(s => s.IsAutoAssignDeptManager)
                ? await _db.Users
                    .AsNoTracking()
                    .Where(u => u.RoleId == mgrRoleId && u.Status == "active")
                    .Select(u => new
                    {
                        u.UserId,
                        DeptIds = u.UserDepartments.Select(d => d.DepartmentId).ToList(),
                        LocIds = u.UserLocations.Select(l => l.LocationId).ToList()
                    })
                    .ToListAsync()
                : null;

            foreach (var step in wf.Steps.OrderBy(s => s.StepLevel))
            {
                int appId = step.ApproverUserId ?? 0;
                if (step.IsAutoAssignDeptManager)
                {
                    var mgr = mgrCandidates!.FirstOrDefault(u =>
                        u.DeptIds.Contains(r.DepartmentId) &&
                        u.LocIds.Contains(r.LocationId));
                    if (mgr == null)
                        mgr = mgrCandidates!.FirstOrDefault(u =>
                            u.DeptIds.Contains(r.DepartmentId));
                    if (mgr == null) return (false, "Cannot auto-assign: no active Manager found for this department and location.");
                    appId = mgr.UserId;
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

        return (true, null);
    }

    public async Task<(bool ok, string? err)> ProcessActionAsync(
        int requestId, int userId, string action, string? comment,
        IFormFile? approvalFile = null, string? storagePath = null, int maxFileMb = 20)
    {
        if ((action == "rejected" || action == "clarification") && string.IsNullOrWhiteSpace(comment))
            return (false, "A comment is required for rejection or clarification.");

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
        if (action == "rejected")
        {
            newStatus = "Rejected";
        }
        else if (action == "clarification")
        {
            newStatus = "Clarification Needed";
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
            else newStatus = "Approved";
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

    public async Task<(bool ok, string? err)> DeleteAsync(int requestId, int userId, string userRole = "")
    {
        var r = await _db.Requests.FindAsync(requestId);
        if (r == null) return (false, "Not found.");
        bool isAdmin = userRole.Equals("Admin", StringComparison.OrdinalIgnoreCase);
        if (!isAdmin && r.RequestorId != userId) return (false, "Not authorized.");
        if (r.Status != "Pending Approval") return (false, "Only requests with Pending Approval status can be deleted.");
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

    public async Task<AgingReportVM> GetAgingAsync(AgingReportVM f)
    {
        var q = _db.Requests
            .Include(r => r.Requestor).Include(r => r.Location)
            .Include(r => r.Department).Include(r => r.RequestType)
            .Where(r => r.Status == "Pending Approval" || r.Status == "Partial Approved" || r.Status == "Clarification Needed")
            .AsQueryable();

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

    public async Task<WorkflowDefinition?> MatchWorkflowAsync(
        int locId, int typeId, string priority, int deptId)
    {
        var candidates = await _db.WorkflowDefinitions.Include(w => w.Steps)
            .Where(w => w.IsActive
                && w.Steps.Any()
                && (w.LocationId == null || w.LocationId == locId)
                && (w.RequestTypeId == null || w.RequestTypeId == typeId)
                && (w.Priority == null || w.Priority == priority)
                && (w.DepartmentId == null || w.DepartmentId == deptId))
            .ToListAsync();

        return candidates
            .OrderByDescending(w =>
                (w.LocationId != null && w.LocationId == locId ? 4 : 0) +
                (w.DepartmentId != null && w.DepartmentId == deptId ? 4 : 0) +
                (w.RequestTypeId != null && w.RequestTypeId == typeId ? 2 : 0) +
                (w.Priority != null && w.Priority == priority ? 1 : 0))
            .FirstOrDefault();
    }
}
