namespace ApproveFlow.MVC.Services;

// ─────────────────────────────────────────────────────────────
// DWO (Discussion Work Order) module — all data access and workflow.
//
// Self-contained by design: nothing here touches Requests, RequestApprovers,
// WorkflowDefinitions, the approval engine or the WhatsApp/escalation services. The only
// shared tables it reads are the masters (Users, Companies, Locations, Departments) and
// the only shared table it writes is Notifications, through the existing row shape with a
// NULL RequestId.
// ─────────────────────────────────────────────────────────────
public interface IDwoService
{
    Task<DwoActor> BuildActorAsync(DwoActor actor);

    Task<DwoRegisterVM> GetRegisterAsync(DwoRegisterVM filter, DwoActor actor, string mode, int page, int pageSize);
    Task<List<DwoPointRowVM>> GetRegisterForExportAsync(DwoRegisterVM filter, DwoActor actor, string mode);
    Task<DwoPointDetailVM?> GetPointAsync(int pointId, DwoActor actor);

    Task<DwoPointFormVM> NewFormAsync(DwoActor actor);
    Task<DwoPointFormVM?> NewFormForSetAsync(int setId, DwoActor actor);
    Task<DwoPointFormVM?> EditFormAsync(int pointId, DwoActor actor);
    Task FillFormLookupsAsync(DwoPointFormVM vm, DwoActor actor);
    Task<(bool ok, string? err, int pointId)> SavePointAsync(
        DwoPointFormVM vm, DwoActor actor, string saveAction, string storagePath, int maxFileMb);

    Task<(bool ok, string? err)> SubmitPointAsync(int pointId, DwoActor actor);
    Task<(bool ok, string? err)> WithdrawPointAsync(int pointId, DwoActor actor);
    Task<(bool ok, string? err)> ReviewAsync(int pointId, DwoActor actor, string action, string? note);

    Task<DwoSetListVM> GetSetsAsync(DwoActor actor, int? deptId, string? status);
    Task<DwoSetDetailVM?> GetSetAsync(int setId, DwoActor actor);
    Task<(bool ok, string? err, string? setNumber)> SubmitSetAsync(
        DwoSetSubmitVM vm, DwoActor actor, string storagePath, int maxFileMb);
    Task<(bool ok, string? err)> ScheduleAppointmentAsync(int setId, DwoActor actor, DateOnly date, string? note);
    Task<(bool ok, string? err)> PostponeAppointmentAsync(int setId, DwoActor actor, DateOnly date, string? reason);
    Task<(bool ok, string? err)> SaveSummaryFileAsync(int setId, DwoActor actor, IFormFile file, string storagePath, int maxFileMb);

    Task<(bool ok, string? err)> DiscussAsync(
        int pointId, DwoActor actor, string action, DwoDiscussInput input, string storagePath, int maxFileMb);
    Task<(bool ok, string? err)> ResumeAsync(int pointId, DwoActor actor);
    Task<(bool ok, string? err)> CompleteFollowUpAsync(
        int pointId, DwoActor actor, string note, IFormFile? file, string storagePath, int maxFileMb);

    Task<DwoDashboardVM> GetDashboardAsync(DwoActor actor);
    Task<DwoDashboardSummaryVM> GetSummaryAsync(DwoActor actor);
    Task<Models.Dwo.DwoDashboardVM> GetCommonDashboardAsync(DwoActor actor);
    Task<DwoReportVM> GetReportAsync(DwoReportVM filter, DwoActor actor);

    Task<DwoAttachment?> GetAttachmentAsync(int attachmentId, DwoActor actor);
}

// Everything the four Management actions can post. One shape keeps the controller thin —
// each branch of DiscussAsync validates only the fields it needs.
public class DwoDiscussInput
{
    public string? Remarks { get; set; }
    public int? FollowUpOwnerId { get; set; }
    public DateOnly? DueDate { get; set; }
    public int? TargetDepartmentId { get; set; }
    public IFormFile? File { get; set; }
}

public class DwoService : IDwoService
{
    private readonly AppDbContext _db;
    private readonly ILogger<DwoService> _log;

    public DwoService(AppDbContext db, ILogger<DwoService> log) { _db = db; _log = log; }

    private static DateOnly Today => DateOnly.FromDateTime(IstClock.Now.DateTime);

    // ─────────────────────────────────────────────────────────
    // Actor
    // ─────────────────────────────────────────────────────────
    // The controller fills in the permission flags from the claims; this adds the
    // department scope and display name from the database.
    public async Task<DwoActor> BuildActorAsync(DwoActor actor)
    {
        var user = await _db.Users
            .Include(u => u.UserDepartments)
            .FirstOrDefaultAsync(u => u.UserId == actor.UserId);
        actor.UserName = user?.FullName ?? actor.UserName;
        actor.DepartmentIds = user?.UserDepartments.Select(ud => ud.DepartmentId).ToList() ?? new();
        // Admin (passed in by the controller) is always Management; everyone else is
        // Management when the Workflow Definition module puts them at the top approval level.
        if (!actor.IsManagement && user != null)
        {
            var (roleIds, userIds) = await ManagementFromWorkflowsAsync();
            actor.IsManagement = roleIds.Contains(user.RoleId) || userIds.Contains(user.UserId);
        }
        return actor;
    }

    // Management is defined by the Workflow Definition module: the approver at the highest
    // step level of every active workflow. Role-based steps yield the role; legacy
    // user-pinned steps yield the user. Nothing in the DWO module hardcodes a role name.
    private async Task<(HashSet<int> roleIds, HashSet<int> userIds)> ManagementFromWorkflowsAsync()
    {
        var topSteps = await _db.WorkflowSteps
            .Where(s => s.Workflow.IsActive &&
                        s.StepLevel == _db.WorkflowSteps
                            .Where(x => x.WorkflowId == s.WorkflowId)
                            .Max(x => x.StepLevel))
            .Select(s => new { s.ApproverRoleId, s.ApproverUserId })
            .ToListAsync();
        return (
            topSteps.Where(s => s.ApproverRoleId.HasValue).Select(s => s.ApproverRoleId!.Value).ToHashSet(),
            topSteps.Where(s => s.ApproverUserId.HasValue).Select(s => s.ApproverUserId!.Value).ToHashSet());
    }

    // Management sees every department; everyone else only the ones they are mapped to.
    // A user mapped to no department sees nothing but their own points — deny by default.
    private IQueryable<DwoPoint> ScopedPoints(DwoActor actor)
    {
        var q = _db.DwoPoints.AsQueryable();
        if (actor.IsManagement) return q;
        return q.Where(p => actor.DepartmentIds.Contains(p.DepartmentId) || p.RaisedByUserId == actor.UserId);
    }

    // ─────────────────────────────────────────────────────────
    // Register / My Discussions / Management grid
    // ─────────────────────────────────────────────────────────
    private IQueryable<DwoPoint> ApplyFilters(IQueryable<DwoPoint> q, DwoRegisterVM f, bool includeStatus)
    {
        var companies = f.FilterCompanyId.Where(x => x > 0).ToList();
        var locations = f.FilterLocationId.Where(x => x > 0).ToList();
        var depts     = f.FilterDeptId.Where(x => x > 0).ToList();
        if (companies.Count > 0) q = q.Where(p => companies.Contains(p.CompanyId));
        if (locations.Count > 0) q = q.Where(p => locations.Contains(p.LocationId));
        if (depts.Count > 0)     q = q.Where(p => depts.Contains(p.DepartmentId));
        if (f.FilterRaisedById is int rb && rb > 0) q = q.Where(p => p.RaisedByUserId == rb);
        if (!string.IsNullOrWhiteSpace(f.FilterCategory)) q = q.Where(p => p.Category == f.FilterCategory);
        if (!string.IsNullOrWhiteSpace(f.FilterPriority)) q = q.Where(p => p.Priority == f.FilterPriority);
        if (!string.IsNullOrWhiteSpace(f.FilterTaskId))
            q = q.Where(p => p.TaskId != null && p.TaskId.Contains(f.FilterTaskId!));
        if (!string.IsNullOrWhiteSpace(f.SearchTerm))
        {
            var s = f.SearchTerm!.Trim();
            q = q.Where(p => p.Title.Contains(s) || p.PointNumber.Contains(s) ||
                             (p.TaskId != null && p.TaskId.Contains(s)));
        }
        if (f.FromDate is DateOnly from)
        {
            var fromTs = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), IstClock.Now.Offset);
            q = q.Where(p => p.CreatedAt >= fromTs);
        }
        if (f.ToDate is DateOnly to)
        {
            var toTs = new DateTimeOffset(to.ToDateTime(TimeOnly.MaxValue), IstClock.Now.Offset);
            q = q.Where(p => p.CreatedAt <= toTs);
        }
        var statuses = f.FilterStatus.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
        if (includeStatus && statuses.Count > 0)
            q = q.Where(p => statuses.Contains(p.Status));
        return q;
    }

    // Base set for a grid, before any user filter. "mine" is the strict My Discussions
    // rule: points I raised, nothing else — regardless of department scope.
    private IQueryable<DwoPoint> GridBase(DwoActor actor, string mode) => mode switch
    {
        DwoGridMode.Mine       => _db.DwoPoints.Where(p => p.RaisedByUserId == actor.UserId),
        _                      => ScopedPoints(actor),
    };

    public async Task<DwoRegisterVM> GetRegisterAsync(
        DwoRegisterVM filter, DwoActor actor, string mode, int page, int pageSize)
    {
        filter.Mode = mode;
        filter.Actor = actor;

        var baseQ = GridBase(actor, mode);

        // Chip counts use the SAME filters as the grid minus the status filter itself, so a
        // chip's number always equals the row count you get after clicking it.
        var countsQ = ApplyFilters(baseQ, filter, includeStatus: false);
        filter.StatusCounts = await countsQ
            .GroupBy(p => p.Status)
            .Select(g => new { Status = g.Key, N = g.Count() })
            .ToDictionaryAsync(x => x.Status, x => x.N);

        var q = ApplyFilters(baseQ, filter, includeStatus: true);
        filter.TotalCount = await q.CountAsync();
        filter.PageNumber = Math.Max(1, page);
        filter.PageSize = pageSize;

        filter.Items = await ProjectRows(q)
            .OrderByDescending(r => r.CreatedAt)
            .Skip((filter.PageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
        MarkSelectable(filter.Items, actor, mode);

        await FillRegisterLookupsAsync(filter, actor);
        return filter;
    }

    public async Task<List<DwoPointRowVM>> GetRegisterForExportAsync(
        DwoRegisterVM filter, DwoActor actor, string mode)
    {
        var q = ApplyFilters(GridBase(actor, mode), filter, includeStatus: true);
        return await ProjectRows(q).OrderByDescending(r => r.CreatedAt).Take(5000).ToListAsync();
    }

    // One projection for every grid and widget, so a row always carries the same fields.
    private IQueryable<DwoPointRowVM> ProjectRows(IQueryable<DwoPoint> q)
    {
        var today = Today;
        return q.Select(p => new DwoPointRowVM
        {
            PointId      = p.PointId,
            PointNumber  = p.PointNumber,
            SetNumber    = p.Set != null ? p.Set.SetNumber : null,
            TaskId       = p.TaskId,
            Title        = p.Title,
            Category     = p.Category,
            Priority     = p.Priority,
            Status       = p.Status,
            CompanyName  = p.Company.CompanyName,
            LocationName = p.Location.LocationName,
            DeptName     = p.Department.DeptName,
            RaisedByName = p.RaisedByUser != null ? p.RaisedByUser.FullName : "Unassigned (reassigned)",
            CreatedAt    = p.CreatedAt,
            FollowUpOwnerName = p.FollowUps
                .Where(f => f.CompletedAt == null)
                .OrderByDescending(f => f.FollowUpId)
                .Select(f => f.OwnerUser.FullName).FirstOrDefault(),
            FollowUpDueDate = p.FollowUps
                .Where(f => f.CompletedAt == null)
                .OrderByDescending(f => f.FollowUpId)
                .Select(f => (DateOnly?)f.DueDate).FirstOrDefault(),
            IsFollowUpOverdue = p.Status == DwoStatus.FollowUp && p.FollowUps
                .Any(f => f.CompletedAt == null && f.DueDate < today),
        });
    }

    // Submitted rows may be ticked for a Set by an HOD (own department — the grid is already
    // scoped to it) or by Management (any department), matching SubmitSetAsync.
    private static void MarkSelectable(List<DwoPointRowVM> rows, DwoActor actor, string mode)
    {
        if ((!actor.IsHod && !actor.IsManagement) || mode == DwoGridMode.Mine) return;
        foreach (var r in rows)
            r.IsSelectableForSet = r.Status == DwoStatus.Submitted;
    }

    private async Task FillRegisterLookupsAsync(DwoRegisterVM vm, DwoActor actor)
    {
        vm.Companies = await _db.Companies.Where(c => c.IsActive).OrderBy(c => c.CompanyName).ToListAsync();
        vm.Locations = await _db.Locations.Where(l => l.IsActive).OrderBy(l => l.LocationName).ToListAsync();
        vm.Departments = await VisibleDepartmentsAsync(actor);
        vm.Categories = DwoCategory.All.ToList();
        var raiserIds = await GridBase(actor, vm.Mode)
            .Where(p => p.RaisedByUserId != null)
            .Select(p => p.RaisedByUserId!.Value).Distinct().ToListAsync();
        vm.Raisers = await _db.Users.Where(u => raiserIds.Contains(u.UserId))
            .OrderBy(u => u.FullName)
            .Select(u => new UserOptionVM { UserId = u.UserId, FullName = u.FullName })
            .ToListAsync();
    }

    private async Task<List<Department>> VisibleDepartmentsAsync(DwoActor actor)
    {
        var q = _db.Departments.Where(d => d.IsActive);
        if (!actor.IsManagement) q = q.Where(d => actor.DepartmentIds.Contains(d.DepartmentId));
        return await q.OrderBy(d => d.DeptName).ToListAsync();
    }

    // ─────────────────────────────────────────────────────────
    // Detail
    // ─────────────────────────────────────────────────────────
    public async Task<DwoPointDetailVM?> GetPointAsync(int pointId, DwoActor actor)
    {
        var p = await _db.DwoPoints
            .Include(x => x.Company).Include(x => x.Location).Include(x => x.Department)
            .Include(x => x.RaisedByUser).Include(x => x.ReviewedByUser)
            .Include(x => x.Set)
            .Include(x => x.OriginPoint)
            .FirstOrDefaultAsync(x => x.PointId == pointId);
        if (p == null) return null;
        if (!CanSee(p, actor)) return null;

        var followUps = await _db.DwoFollowUps
            .Include(f => f.OwnerUser).Include(f => f.CompletedByUser)
            .Where(f => f.PointId == pointId).OrderByDescending(f => f.FollowUpId).ToListAsync();

        var vm = new DwoPointDetailVM
        {
            Point = p,
            Set = p.Set,
            OriginPoint = p.OriginPoint,
            FollowUps = followUps,
            OpenFollowUp = followUps.FirstOrDefault(f => f.CompletedAt == null),
            History = await _db.DwoHistories.Include(h => h.ActorUser)
                .Where(h => h.PointId == pointId).OrderBy(h => h.HistoryId).ToListAsync(),
            Attachments = await _db.DwoAttachments.Include(a => a.UploadedByUser)
                .Where(a => a.PointId == pointId).OrderBy(a => a.AttachmentId).ToListAsync(),
            ReferenceLinks = await _db.DwoReferenceLinks
                .Where(r => r.PointId == pointId).OrderBy(r => r.LinkId).ToListAsync(),
            Departments = await _db.Departments.Where(d => d.IsActive && d.DepartmentId != p.DepartmentId)
                .OrderBy(d => d.DeptName).ToListAsync(),
            Actor = actor,
        };

        // Follow-up owners offered to Management: everyone mapped to the point's department.
        vm.FollowUpOwners = await _db.UserDepartments
            .Where(ud => ud.DepartmentId == p.DepartmentId && ud.User.Status == "active")
            .Select(ud => new UserOptionVM { UserId = ud.UserId, FullName = ud.User.FullName })
            .Distinct().OrderBy(u => u.FullName).ToListAsync();

        var owns = Owns(p, actor);
        vm.CanEdit    = actor.CanEditOwn && owns &&
                        p.Status is DwoStatus.Draft or DwoStatus.Submitted or DwoStatus.ChangeRequested;
        vm.CanSubmit  = actor.CanCreate && owns && p.Status == DwoStatus.Draft;
        // A Draft is already withdrawn — offering Withdraw on it would be a no-op button.
        vm.CanWithdraw = actor.CanWithdraw && owns &&
                        p.Status is not (DwoStatus.Draft or DwoStatus.Closed or DwoStatus.FollowUp or DwoStatus.Rejected or DwoStatus.Reassigned);
        vm.CanReview  = actor.IsHod && !actor.IsManagement &&
                        p.Status == DwoStatus.Submitted && actor.DepartmentIds.Contains(p.DepartmentId);
        // Same rule as the Register grid's tick column (MarkSelectable / SubmitSetAsync).
        vm.CanBuildSet = p.Status == DwoStatus.Submitted &&
                        (actor.IsManagement || (actor.IsHod && actor.DepartmentIds.Contains(p.DepartmentId)));
        vm.CanDiscuss = actor.IsManagement && p.Status == DwoStatus.ManagementDiscussion;
        vm.CanResume  = actor.IsManagement && p.Status == DwoStatus.Hold;
        vm.CanCompleteFollowUp = !actor.IsManagement && p.Status == DwoStatus.FollowUp &&
                        vm.OpenFollowUp != null &&
                        (vm.OpenFollowUp.OwnerUserId == actor.UserId ||
                         (actor.IsHod && actor.DepartmentIds.Contains(p.DepartmentId)));
        return vm;
    }

    private static bool CanSee(DwoPoint p, DwoActor actor) =>
        actor.IsManagement || p.RaisedByUserId == actor.UserId || actor.DepartmentIds.Contains(p.DepartmentId);

    // Ownership for edit/submit/withdraw. A point created by a reassignment has no raiser
    // until someone in the target department picks it up, so anyone there may claim it.
    private static bool Owns(DwoPoint p, DwoActor actor) =>
        p.RaisedByUserId == actor.UserId ||
        (p.RaisedByUserId == null && actor.DepartmentIds.Contains(p.DepartmentId));

    // ─────────────────────────────────────────────────────────
    // Create / edit
    // ─────────────────────────────────────────────────────────
    public async Task<DwoPointFormVM> NewFormAsync(DwoActor actor)
    {
        var vm = new DwoPointFormVM { Actor = actor };
        await FillFormLookupsAsync(vm, actor);
        vm.CompanyId    = vm.Companies.FirstOrDefault()?.CompanyId ?? 0;
        vm.LocationId   = vm.Locations.FirstOrDefault()?.LocationId ?? 0;
        vm.DepartmentId = vm.Departments.FirstOrDefault()?.DepartmentId ?? 0;
        return vm;
    }

    // "Add a point for discussion" on an open Set. Same rule as the prototype: Management,
    // or the HOD who owns the Set, while the Set still has something awaiting discussion.
    public async Task<DwoPointFormVM?> NewFormForSetAsync(int setId, DwoActor actor)
    {
        var set = await OpenSetForAddAsync(setId, actor);
        if (set == null) return null;

        // FillFormLookupsAsync pins the department to the Set's once SetId is present.
        var vm = await NewFormAsync(actor);
        vm.SetId = set.SetId;
        await FillFormLookupsAsync(vm, actor);
        return vm;
    }

    // The Set a point may be added to, or null when it is missing, out of scope, not the
    // caller's to manage, or already Completed.
    private async Task<DwoSet?> OpenSetForAddAsync(int setId, DwoActor actor)
    {
        var set = await _db.DwoSets.FirstOrDefaultAsync(s => s.SetId == setId);
        if (set == null) return null;
        bool canManage = actor.IsManagement || (actor.IsHod && actor.DepartmentIds.Contains(set.DepartmentId));
        if (!canManage) return null;
        bool anyPoint  = await _db.DwoPoints.AnyAsync(p => p.SetId == setId);
        bool completed = anyPoint && !await _db.DwoPoints.AnyAsync(p => p.SetId == setId && p.Status == DwoStatus.ManagementDiscussion);
        return completed ? null : set;
    }

    public async Task<DwoPointFormVM?> EditFormAsync(int pointId, DwoActor actor)
    {
        var p = await _db.DwoPoints
            .Include(x => x.ReferenceLinks)
            .FirstOrDefaultAsync(x => x.PointId == pointId);
        if (p == null || !Owns(p, actor)) return null;
        if (p.Status is not (DwoStatus.Draft or DwoStatus.Submitted or DwoStatus.ChangeRequested)) return null;

        var vm = new DwoPointFormVM
        {
            PointId = p.PointId, PointNumber = p.PointNumber, TaskId = p.TaskId,
            Title = p.Title, Description = p.Description, Category = p.Category, Priority = p.Priority,
            CompanyId = p.CompanyId, LocationId = p.LocationId, DepartmentId = p.DepartmentId,
            Status = p.Status,
            ReferenceLinks = p.ReferenceLinks.OrderBy(r => r.LinkId).Select(r => r.Url).ToList(),
            Attachments = await _db.DwoAttachments
                .Where(a => a.PointId == pointId && a.Kind == DwoAttachmentKind.Point)
                .OrderBy(a => a.AttachmentId).ToListAsync(),
            Actor = actor,
        };
        await FillFormLookupsAsync(vm, actor);
        return vm;
    }

    public async Task FillFormLookupsAsync(DwoPointFormVM vm, DwoActor actor)
    {
        vm.Actor = actor;
        vm.Companies = await _db.Companies.Where(c => c.IsActive).OrderBy(c => c.CompanyName).ToListAsync();
        vm.Locations = await _db.Locations.Where(l => l.IsActive).OrderBy(l => l.LocationName).ToListAsync();
        vm.Departments = await VisibleDepartmentsAsync(actor);
        // A form re-rendered after a failed "Add to Set" save keeps the Set's fixed department.
        if (vm.PointId == 0 && vm.SetId > 0)
        {
            var set = await _db.DwoSets.FirstOrDefaultAsync(x => x.SetId == vm.SetId);
            if (set != null)
            {
                vm.SetNumber = set.SetNumber;
                vm.DepartmentId = set.DepartmentId;
                vm.Departments = vm.Departments.Where(d => d.DepartmentId == set.DepartmentId).ToList();
            }
        }
        // Management raises points straight into a department, so it needs the full list.
        if (actor.IsManagement)
            vm.Departments = await _db.Departments.Where(d => d.IsActive).OrderBy(d => d.DeptName).ToListAsync();
        vm.Categories = DwoCategory.All.ToList();
    }

    // saveAction: "draft" | "submit". Management raising a point goes straight to
    // Management Discussion — it has no HOD above it to review.
    public async Task<(bool ok, string? err, int pointId)> SavePointAsync(
        DwoPointFormVM vm, DwoActor actor, string saveAction, string storagePath, int maxFileMb)
    {
        if (string.IsNullOrWhiteSpace(vm.Title))       return (false, "Discussion title is required.", 0);
        if (string.IsNullOrWhiteSpace(vm.Description)) return (false, "Description is required.", 0);
        if (vm.CompanyId <= 0)    return (false, "Company is required.", 0);
        if (vm.LocationId <= 0)   return (false, "Site is required.", 0);

        // Adding into an open Set: the Set fixes the department, and the manage-the-Set rule
        // replaces the usual create/department checks.
        DwoSet? targetSet = null;
        if (vm.PointId == 0 && vm.SetId > 0)
        {
            targetSet = await OpenSetForAddAsync(vm.SetId, actor);
            if (targetSet == null) return (false, "That Set is not open for new discussions, or it is not yours to manage.", 0);
            vm.DepartmentId = targetSet.DepartmentId;
        }
        else
        {
            if (vm.DepartmentId <= 0) return (false, "Department is required.", 0);
            if (!actor.IsManagement && !actor.DepartmentIds.Contains(vm.DepartmentId))
                return (false, "You are not mapped to that department.", 0);
        }

        DwoPoint p;
        bool isNew = vm.PointId == 0;
        string historyAction;

        if (targetSet != null)
        {
            p = new DwoPoint { RaisedByUserId = actor.UserId, Status = DwoStatus.ManagementDiscussion, SetId = targetSet.SetId };
            _db.DwoPoints.Add(p);
            historyAction = $"Raised for discussion within Set {targetSet.SetNumber}";
        }
        else if (isNew)
        {
            if (!actor.CanCreate) return (false, "You do not have permission to create a discussion.", 0);
            p = new DwoPoint { RaisedByUserId = actor.UserId, Status = DwoStatus.Draft };
            _db.DwoPoints.Add(p);
            historyAction = "Raised";
        }
        else
        {
            var existing = await _db.DwoPoints.Include(x => x.ReferenceLinks)
                .FirstOrDefaultAsync(x => x.PointId == vm.PointId);
            if (existing == null) return (false, "Discussion not found.", 0);
            if (!Owns(existing, actor)) return (false, "You can only edit your own discussions.", 0);
            if (existing.Status is not (DwoStatus.Draft or DwoStatus.Submitted or DwoStatus.ChangeRequested))
                return (false, $"A discussion in {existing.Status} can no longer be edited.", 0);
            p = existing;
            // Whoever updates an unclaimed reassigned point becomes its raiser.
            p.RaisedByUserId ??= actor.UserId;
            _db.DwoReferenceLinks.RemoveRange(p.ReferenceLinks);
            historyAction = "Edited";
        }

        p.TaskId = string.IsNullOrWhiteSpace(vm.TaskId) ? null : vm.TaskId!.Trim();
        p.Title = vm.Title.Trim();
        p.Description = vm.Description.Trim();
        p.Category = vm.Category;
        p.Priority = vm.Priority;
        p.CompanyId = vm.CompanyId;
        p.LocationId = vm.LocationId;
        p.DepartmentId = vm.DepartmentId;

        foreach (var url in (vm.ReferenceLinks ?? new()).Select(u => u?.Trim()).Where(u => !string.IsNullOrWhiteSpace(u)))
            _db.DwoReferenceLinks.Add(new DwoReferenceLink { Point = p, Url = url! });

        await _db.SaveChangesAsync();

        // The number is derived from the identity value, so it can only be stamped after
        // the first save. Guaranteed unique, no sequence table, no race.
        if (isNew)
        {
            var deptCode = await _db.Departments.Where(d => d.DepartmentId == p.DepartmentId)
                .Select(d => d.Code).FirstOrDefaultAsync() ?? "DWO";
            p.PointNumber = $"{deptCode.ToUpperInvariant()}-{1000 + p.PointId}";
        }

        if (!string.IsNullOrWhiteSpace(storagePath))
        {
            var (_, uploadErr) = await SaveFilesAsync(
                vm.NewFiles, storagePath, maxFileMb, actor, DwoAttachmentKind.Point, pointId: p.PointId);
            if (uploadErr != null) _log.LogWarning("DWO point {Id}: {Err}", p.PointId, uploadErr);
        }
        if (vm.RemoveAttachmentIds?.Count > 0)
        {
            var kill = await _db.DwoAttachments
                .Where(a => a.PointId == p.PointId && vm.RemoveAttachmentIds.Contains(a.AttachmentId))
                .ToListAsync();
            RemoveFiles(kill, storagePath);
            _db.DwoAttachments.RemoveRange(kill);
        }

        // Submitting from the form: Draft/Change Requested → Submitted (Management → straight
        // to Management Discussion). Editing a Change Requested point and saving resubmits it.
        var resubmitted = !isNew && p.Status == DwoStatus.ChangeRequested;
        if (targetSet != null)
        {
            // Already in Management Discussion and on the Set; record it on the Set too.
            AddHistory(null, targetSet.SetId, actor, "Discussion added", $"{p.PointNumber} — {p.Title}");
        }
        else if (saveAction == "submit" || resubmitted)
        {
            p.Status = actor.IsManagement ? DwoStatus.ManagementDiscussion : DwoStatus.Submitted;
            historyAction = isNew
                ? (actor.IsManagement ? "Raised for management discussion" : "Raised and submitted to HOD")
                : (resubmitted ? "Edited & resubmitted" : "Submitted to HOD");
        }

        AddHistory(p.PointId, null, actor, historyAction, null);
        await _db.SaveChangesAsync();

        if (p.Status == DwoStatus.Submitted)
            await NotifyHodAsync(p, $"DWO {p.PointNumber} \"{p.Title}\" is awaiting your review.");
        else if (p.Status == DwoStatus.ManagementDiscussion)
            await NotifyManagementAsync($"DWO {p.PointNumber} \"{p.Title}\" was raised for management discussion.");

        return (true, null, p.PointId);
    }

    public async Task<(bool ok, string? err)> SubmitPointAsync(int pointId, DwoActor actor)
    {
        var p = await _db.DwoPoints.FirstOrDefaultAsync(x => x.PointId == pointId);
        if (p == null) return (false, "Discussion not found.");
        if (!Owns(p, actor)) return (false, "You can only submit your own discussions.");
        if (p.Status != DwoStatus.Draft) return (false, "Only a Draft discussion can be submitted.");

        p.RaisedByUserId ??= actor.UserId;
        p.Status = actor.IsManagement ? DwoStatus.ManagementDiscussion : DwoStatus.Submitted;
        AddHistory(p.PointId, null, actor,
            actor.IsManagement ? "Moved to management discussion" : "Submitted to HOD", null);
        await _db.SaveChangesAsync();

        if (p.Status == DwoStatus.Submitted)
            await NotifyHodAsync(p, $"DWO {p.PointNumber} \"{p.Title}\" is awaiting your review.");
        return (true, null);
    }

    public async Task<(bool ok, string? err)> WithdrawPointAsync(int pointId, DwoActor actor)
    {
        var p = await _db.DwoPoints.FirstOrDefaultAsync(x => x.PointId == pointId);
        if (p == null) return (false, "Discussion not found.");
        if (!Owns(p, actor)) return (false, "You can only withdraw your own discussions.");
        if (p.Status is DwoStatus.Closed or DwoStatus.FollowUp or DwoStatus.Rejected or DwoStatus.Reassigned)
            return (false, $"A discussion in {p.Status} cannot be withdrawn.");

        p.Status = DwoStatus.Draft;
        p.SetId = null;                     // withdrawing pulls it out of any Set it had joined
        AddHistory(p.PointId, null, actor, "Withdrawn", null);
        await _db.SaveChangesAsync();
        return (true, null);
    }

    // ─────────────────────────────────────────────────────────
    // HOD review — send back / reject. (Moving to Management goes through a Set.)
    // ─────────────────────────────────────────────────────────
    public async Task<(bool ok, string? err)> ReviewAsync(int pointId, DwoActor actor, string action, string? note)
    {
        if (!actor.IsHod && !actor.IsManagement) return (false, "You do not have review rights.");
        var p = await _db.DwoPoints.FirstOrDefaultAsync(x => x.PointId == pointId);
        if (p == null) return (false, "Discussion not found.");
        if (!actor.IsManagement && !actor.DepartmentIds.Contains(p.DepartmentId))
            return (false, "That discussion is outside your department.");
        if (p.Status != DwoStatus.Submitted) return (false, "Only a Submitted discussion can be reviewed.");
        if (string.IsNullOrWhiteSpace(note)) return (false, "Remarks are required.");

        var trimmed = note!.Trim();
        string historyAction;
        switch (action)
        {
            case "send_back":
                p.Status = DwoStatus.ChangeRequested;
                historyAction = "Sent back for clarification";
                break;
            case "reject":
                p.Status = DwoStatus.Rejected;
                historyAction = "Rejected";
                break;
            default:
                return (false, "Unknown review action.");
        }
        p.ReviewedByUserId = actor.UserId;
        p.ReviewedAt = IstClock.Now;
        p.ReviewRemarks = trimmed;
        AddHistory(p.PointId, null, actor, historyAction, trimmed);
        await _db.SaveChangesAsync();

        if (p.RaisedByUserId is int raiser)
            await NotifyAsync(raiser, "dwo_" + action,
                $"DWO {p.PointNumber} \"{p.Title}\" — {historyAction.ToLowerInvariant()}: {trimmed}");
        return (true, null);
    }

    // ─────────────────────────────────────────────────────────
    // Sets
    // ─────────────────────────────────────────────────────────
    private IQueryable<DwoSet> ScopedSets(DwoActor actor) =>
        actor.IsManagement
            ? _db.DwoSets
            : _db.DwoSets.Where(s => actor.DepartmentIds.Contains(s.DepartmentId));

    public async Task<DwoSetListVM> GetSetsAsync(DwoActor actor, int? deptId, string? status)
    {
        var vm = new DwoSetListVM { Actor = actor, FilterDeptId = deptId, FilterStatus = status };
        var q = ScopedSets(actor);
        if (deptId is int d && d > 0) q = q.Where(s => s.DepartmentId == d);

        var rows = await q.Select(s => new DwoSetRowVM
        {
            SetId = s.SetId,
            SetNumber = s.SetNumber,
            DeptName = s.Department.DeptName,
            PointCount = s.Points.Count,
            UndecidedCount = s.Points.Count(p => p.Status == DwoStatus.ManagementDiscussion),
            AppointmentStatus = s.AppointmentStatus,
            AppointmentDate = s.ScheduledDate ?? s.RequestedAppointmentDate,
            SubmittedByName = s.SubmittedByUser.FullName,
            SubmittedAt = s.SubmittedAt,
        }).OrderByDescending(r => r.SetId).ToListAsync();

        vm.StatusCounts = new Dictionary<string, int>
        {
            ["awaiting"]  = rows.Count(r => !r.IsCompleted),
            ["completed"] = rows.Count(r => r.IsCompleted),
        };
        if (status == "awaiting")  rows = rows.Where(r => !r.IsCompleted).ToList();
        if (status == "completed") rows = rows.Where(r => r.IsCompleted).ToList();

        vm.Items = rows;
        vm.Departments = await VisibleDepartmentsAsync(actor);
        return vm;
    }

    public async Task<DwoSetDetailVM?> GetSetAsync(int setId, DwoActor actor)
    {
        var set = await _db.DwoSets
            .Include(s => s.Department).Include(s => s.SubmittedByUser)
            .FirstOrDefaultAsync(s => s.SetId == setId);
        if (set == null) return null;
        if (!actor.IsManagement && !actor.DepartmentIds.Contains(set.DepartmentId)) return null;

        var points = await ProjectRows(_db.DwoPoints.Where(p => p.SetId == setId))
            .OrderBy(p => p.PointId).ToListAsync();
        var attachments = await _db.DwoAttachments.Include(a => a.UploadedByUser)
            .Where(a => a.SetId == setId).OrderBy(a => a.AttachmentId).ToListAsync();

        return new DwoSetDetailVM
        {
            Set = set,
            Points = points,
            SupportingDocuments = attachments.Where(a => a.Kind == DwoAttachmentKind.SetSupporting).ToList(),
            SummaryFile = attachments.LastOrDefault(a => a.Kind == DwoAttachmentKind.SetSummary),
            History = await _db.DwoHistories.Include(h => h.ActorUser)
                .Where(h => h.SetId == setId).OrderBy(h => h.HistoryId).ToListAsync(),
            Actor = actor,
            IsCompleted = points.Count > 0 && points.All(p => p.Status != DwoStatus.ManagementDiscussion),
            CanManage = actor.IsManagement || (actor.IsHod && actor.DepartmentIds.Contains(set.DepartmentId)),
        };
    }

    // The HOD's one route to Management: bundle Submitted points from their own department
    // with a supporting pack and a requested appointment date. Every point on the Set moves
    // to Management Discussion together.
    public async Task<(bool ok, string? err, string? setNumber)> SubmitSetAsync(
        DwoSetSubmitVM vm, DwoActor actor, string storagePath, int maxFileMb)
    {
        if (!actor.IsHod && !actor.IsManagement)
            return (false, "You do not have permission to submit a Set.", null);
        if (vm.PointIds == null || vm.PointIds.Count == 0)
            return (false, "Select at least one Submitted discussion first.", null);
        if (vm.RequestedAppointmentDate is not DateOnly requested)
            return (false, "A requested appointment date is required.", null);
        if (requested < Today)
            return (false, "The appointment date cannot be in the past.", null);
        var supporting = (vm.SupportingFiles ?? new()).Where(f => f != null && f.Length > 0).ToList();
        if (supporting.Count == 0)
            return (false, "At least one supporting document is required.", null);

        var points = await _db.DwoPoints
            .Where(p => vm.PointIds.Contains(p.PointId) && p.Status == DwoStatus.Submitted)
            .ToListAsync();
        if (points.Count == 0)
            return (false, "None of the selected discussions are still Submitted.", null);

        var deptIds = points.Select(p => p.DepartmentId).Distinct().ToList();
        if (deptIds.Count > 1)
            return (false, "A Set must contain discussions from a single department.", null);
        var deptId = deptIds[0];
        if (!actor.IsManagement && !actor.DepartmentIds.Contains(deptId))
            return (false, "Those discussions are outside your department.", null);

        var set = new DwoSet
        {
            DepartmentId = deptId,
            RequestedAppointmentDate = requested,
            AppointmentNotes = string.IsNullOrWhiteSpace(vm.AppointmentNotes) ? null : vm.AppointmentNotes!.Trim(),
            AppointmentStatus = DwoAppointmentStatus.Requested,
            SubmittedByUserId = actor.UserId,
            SubmittedAt = IstClock.Now,
        };
        _db.DwoSets.Add(set);
        await _db.SaveChangesAsync();
        set.SetNumber = $"DWO{100 + set.SetId}";

        foreach (var p in points)
        {
            p.SetId = set.SetId;
            p.Status = DwoStatus.ManagementDiscussion;
            p.ReviewedByUserId = actor.UserId;
            p.ReviewedAt = IstClock.Now;
            AddHistory(p.PointId, null, actor, "Moved to management discussion",
                $"Set {set.SetNumber} — appointment requested for {requested:dd MMM yyyy}");
        }
        AddHistory(null, set.SetId, actor, "Set submitted",
            $"{points.Count} discussion(s); appointment requested for {requested:dd MMM yyyy}");

        await SaveFilesAsync(supporting, storagePath, maxFileMb, actor, DwoAttachmentKind.SetSupporting, setId: set.SetId);
        if (vm.SummaryFile is { Length: > 0 })
            await SaveFilesAsync(new List<IFormFile> { vm.SummaryFile }, storagePath, maxFileMb, actor,
                DwoAttachmentKind.SetSummary, setId: set.SetId);

        await _db.SaveChangesAsync();
        await NotifyManagementAsync(
            $"Set {set.SetNumber} ({points.Count} discussion(s)) was submitted for management discussion.");
        return (true, null, set.SetNumber);
    }

    public async Task<(bool ok, string? err)> ScheduleAppointmentAsync(int setId, DwoActor actor, DateOnly date, string? note)
    {
        if (!actor.IsManagement) return (false, "Only Management can schedule an appointment.");
        var set = await _db.DwoSets.FirstOrDefaultAsync(s => s.SetId == setId);
        if (set == null) return (false, "Set not found.");
        if (date < Today) return (false, "The appointment date cannot be in the past.");

        set.AppointmentStatus = DwoAppointmentStatus.Scheduled;
        set.ScheduledDate = date;
        AddHistory(null, set.SetId, actor, "Appointment scheduled",
            $"{date:dd MMM yyyy}{(string.IsNullOrWhiteSpace(note) ? "" : " — " + note!.Trim())}");
        await _db.SaveChangesAsync();
        await NotifyAsync(set.SubmittedByUserId, "dwo_appointment",
            $"Set {set.SetNumber} — appointment scheduled for {date:dd MMM yyyy}.");
        return (true, null);
    }

    public async Task<(bool ok, string? err)> PostponeAppointmentAsync(int setId, DwoActor actor, DateOnly date, string? reason)
    {
        if (!actor.IsManagement) return (false, "Only Management can postpone an appointment.");
        var set = await _db.DwoSets.FirstOrDefaultAsync(s => s.SetId == setId);
        if (set == null) return (false, "Set not found.");
        if (date < Today) return (false, "The new appointment date cannot be in the past.");

        set.AppointmentStatus = DwoAppointmentStatus.Postponed;
        set.ScheduledDate = date;
        AddHistory(null, set.SetId, actor, "Appointment postponed",
            $"Moved to {date:dd MMM yyyy}{(string.IsNullOrWhiteSpace(reason) ? "" : " — " + reason!.Trim())}");
        await _db.SaveChangesAsync();
        await NotifyAsync(set.SubmittedByUserId, "dwo_appointment",
            $"Set {set.SetNumber} — appointment postponed to {date:dd MMM yyyy}.");
        return (true, null);
    }

    public async Task<(bool ok, string? err)> SaveSummaryFileAsync(
        int setId, DwoActor actor, IFormFile file, string storagePath, int maxFileMb)
    {
        var set = await _db.DwoSets.FirstOrDefaultAsync(s => s.SetId == setId);
        if (set == null) return (false, "Set not found.");
        if (!actor.IsManagement && !(actor.IsHod && actor.DepartmentIds.Contains(set.DepartmentId)))
            return (false, "You cannot change this Set.");
        if (file is not { Length: > 0 }) return (false, "Choose a file to upload.");

        var (saved, err) = await SaveFilesAsync(new List<IFormFile> { file }, storagePath, maxFileMb, actor,
            DwoAttachmentKind.SetSummary, setId: setId);
        if (err != null) return (false, err);

        AddHistory(null, setId, actor, "Summary document updated", saved.FirstOrDefault()?.FileName);
        await _db.SaveChangesAsync();
        return (true, null);
    }

    // ─────────────────────────────────────────────────────────
    // Management discussion actions
    // ─────────────────────────────────────────────────────────
    public async Task<(bool ok, string? err)> DiscussAsync(
        int pointId, DwoActor actor, string action, DwoDiscussInput input, string storagePath, int maxFileMb)
    {
        if (!actor.IsManagement) return (false, "Only Management can act on a discussion.");
        var p = await _db.DwoPoints
            .Include(x => x.Department)
            .FirstOrDefaultAsync(x => x.PointId == pointId);
        if (p == null) return (false, "Discussion not found.");
        if (p.Status != DwoStatus.ManagementDiscussion)
            return (false, "Only a discussion under Management Discussion can be actioned.");

        var remarks = input.Remarks?.Trim();
        string historyAction;
        string? note;

        switch (action)
        {
            case "close":
                if (string.IsNullOrWhiteSpace(remarks)) return (false, "Resolution remarks are required.");
                p.Status = DwoStatus.Closed;
                historyAction = "Closed"; note = remarks;
                break;

            case "followup":
            {
                if (input.FollowUpOwnerId is not int owner || owner <= 0)
                    return (false, "A follow-up owner is required.");
                if (input.DueDate is not DateOnly due) return (false, "A due date is required.");
                if (due < Today) return (false, "The due date cannot be earlier than today.");
                _db.DwoFollowUps.Add(new DwoFollowUp
                {
                    PointId = p.PointId, OwnerUserId = owner, DueDate = due, Notes = remarks,
                });
                p.Status = DwoStatus.FollowUp;
                var ownerName = await _db.Users.Where(u => u.UserId == owner)
                    .Select(u => u.FullName).FirstOrDefaultAsync() ?? "";
                historyAction = "Follow-up scheduled";
                note = $"{ownerName}, due {due:dd MMM yyyy}{(string.IsNullOrWhiteSpace(remarks) ? "" : " — " + remarks)}";
                break;
            }

            case "hold":
                if (string.IsNullOrWhiteSpace(remarks)) return (false, "A hold reason is required.");
                p.Status = DwoStatus.Hold;
                historyAction = "On hold"; note = remarks;
                break;

            case "reassign":
            {
                if (input.TargetDepartmentId is not int target || target <= 0)
                    return (false, "A target department is required.");
                if (target == p.DepartmentId)
                    return (false, "Choose a department other than the current one.");
                var targetDept = await _db.Departments.FirstOrDefaultAsync(d => d.DepartmentId == target && d.IsActive);
                if (targetDept == null) return (false, "That department is not available.");

                var originName = p.RaisedByUser?.FullName
                    ?? await _db.Users.Where(u => u.UserId == p.RaisedByUserId)
                        .Select(u => u.FullName).FirstOrDefaultAsync() ?? "the original raiser";

                // The original becomes Reassigned and read-only; a fresh Draft lands in the
                // target department carrying the references, the attachment list and a link
                // back to its origin.
                var clone = new DwoPoint
                {
                    TaskId = p.TaskId,
                    Title = $"[Reassigned from {p.Department.DeptName}] {p.Title}",
                    Description = $"{p.Description}\n\n(Originally raised by {originName}, {p.Department.DeptName}.)",
                    Category = p.Category, Priority = p.Priority,
                    CompanyId = p.CompanyId, LocationId = p.LocationId, DepartmentId = target,
                    RaisedByUserId = null,                 // unclaimed until someone there picks it up
                    Status = DwoStatus.Draft,
                    OriginPointId = p.PointId,
                };
                _db.DwoPoints.Add(clone);

                p.Status = DwoStatus.Reassigned;
                historyAction = "Reassigned";
                note = $"To {targetDept.DeptName}{(string.IsNullOrWhiteSpace(remarks) ? "" : ": " + remarks)}";

                await _db.SaveChangesAsync();
                clone.PointNumber = $"{targetDept.Code.ToUpperInvariant()}-{1000 + clone.PointId}";

                // Copy the reference links and the attachment metadata onto the clone. The
                // stored file is shared — the same GUID is referenced twice, and deleting a
                // point never deletes another point's file (see RemoveFiles).
                foreach (var link in await _db.DwoReferenceLinks.Where(r => r.PointId == p.PointId).ToListAsync())
                    _db.DwoReferenceLinks.Add(new DwoReferenceLink { PointId = clone.PointId, Url = link.Url });
                foreach (var a in await _db.DwoAttachments
                             .Where(a => a.PointId == p.PointId && a.Kind == DwoAttachmentKind.Point).ToListAsync())
                    _db.DwoAttachments.Add(new DwoAttachment
                    {
                        PointId = clone.PointId, Kind = DwoAttachmentKind.Point,
                        FileName = a.FileName, StoredFileName = a.StoredFileName,
                        FileSizeBytes = a.FileSizeBytes, ContentType = a.ContentType,
                        UploadedByUserId = a.UploadedByUserId,
                    });

                AddHistory(clone.PointId, null, actor,
                    $"Created via reassignment — awaiting {targetDept.DeptName} to update and submit",
                    $"Origin: {p.PointNumber}");
                await NotifyDepartmentHodAsync(target,
                    $"DWO {clone.PointNumber} was reassigned to {targetDept.DeptName} from {p.Department.DeptName}.");
                break;
            }

            default:
                return (false, "Unknown management action.");
        }

        p.ReviewedByUserId = actor.UserId;
        p.ReviewedAt = IstClock.Now;
        if (!string.IsNullOrWhiteSpace(remarks)) p.ReviewRemarks = remarks;

        if (input.File is { Length: > 0 })
        {
            var (saved, _) = await SaveFilesAsync(new List<IFormFile> { input.File }, storagePath, maxFileMb,
                actor, DwoAttachmentKind.Action, pointId: p.PointId);
            if (saved.Count > 0) note = $"{note} (+{saved.Count} attachment)";
        }

        AddHistory(p.PointId, null, actor, historyAction, note);
        await _db.SaveChangesAsync();

        // Phase 17 notifications.
        if (action == "followup" && input.FollowUpOwnerId is int fo)
            await NotifyAsync(fo, "dwo_followup",
                $"DWO {p.PointNumber} \"{p.Title}\" — follow-up assigned to you, due {input.DueDate:dd MMM yyyy}.");
        if (action == "close" && p.RaisedByUserId is int creator)
            await NotifyAsync(creator, "dwo_closed", $"DWO {p.PointNumber} \"{p.Title}\" was closed. {remarks}");

        return (true, null);
    }

    public async Task<(bool ok, string? err)> ResumeAsync(int pointId, DwoActor actor)
    {
        if (!actor.IsManagement) return (false, "Only Management can resume a discussion.");
        var p = await _db.DwoPoints.FirstOrDefaultAsync(x => x.PointId == pointId);
        if (p == null) return (false, "Discussion not found.");
        if (p.Status != DwoStatus.Hold) return (false, "Only a discussion on Hold can be resumed.");

        p.Status = DwoStatus.ManagementDiscussion;
        AddHistory(p.PointId, null, actor, "Resumed discussion", null);
        await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool ok, string? err)> CompleteFollowUpAsync(
        int pointId, DwoActor actor, string note, IFormFile? file, string storagePath, int maxFileMb)
    {
        if (string.IsNullOrWhiteSpace(note)) return (false, "Describe what was done.");
        var p = await _db.DwoPoints.FirstOrDefaultAsync(x => x.PointId == pointId);
        if (p == null) return (false, "Discussion not found.");
        if (p.Status != DwoStatus.FollowUp) return (false, "This discussion has no open follow-up.");

        var fu = await _db.DwoFollowUps.Where(f => f.PointId == pointId && f.CompletedAt == null)
            .OrderByDescending(f => f.FollowUpId).FirstOrDefaultAsync();
        if (fu == null) return (false, "This discussion has no open follow-up.");

        var isOwner = fu.OwnerUserId == actor.UserId;
        var isDeptHod = actor.IsHod && actor.DepartmentIds.Contains(p.DepartmentId);
        if (!isOwner && !isDeptHod) return (false, "Only the follow-up owner can complete it.");

        fu.CompletedAt = IstClock.Now;
        fu.CompletedByUserId = actor.UserId;
        fu.CompletionNote = note.Trim();

        // Completing a follow-up always returns the point to Management for a decision.
        p.Status = DwoStatus.ManagementDiscussion;

        var suffix = "";
        if (file is { Length: > 0 })
        {
            var (saved, _) = await SaveFilesAsync(new List<IFormFile> { file }, storagePath, maxFileMb,
                actor, DwoAttachmentKind.FollowUpCompletion, pointId: p.PointId);
            if (saved.Count > 0) suffix = " (+1 attachment)";
        }
        AddHistory(p.PointId, null, actor, "Follow-up completed", note.Trim() + suffix);
        await _db.SaveChangesAsync();

        await NotifyManagementAsync($"DWO {p.PointNumber} \"{p.Title}\" — follow-up completed by {actor.UserName}.");
        return (true, null);
    }

    // ─────────────────────────────────────────────────────────
    // Dashboard
    // ─────────────────────────────────────────────────────────
    public async Task<DwoDashboardVM> GetDashboardAsync(DwoActor actor)
    {
        var scoped = ScopedPoints(actor);
        var today = Today;

        var byStatus = await scoped.GroupBy(p => p.Status)
            .Select(g => new { g.Key, N = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.N);

        int S(string s) => byStatus.GetValueOrDefault(s, 0);

        var vm = new DwoDashboardVM
        {
            Actor = actor,
            ByStatus = byStatus,
            Total = byStatus.Values.Sum(),
            Draft = S(DwoStatus.Draft),
            Submitted = S(DwoStatus.Submitted),
            ManagementDiscussion = S(DwoStatus.ManagementDiscussion),
            FollowUp = S(DwoStatus.FollowUp),
            Closed = S(DwoStatus.Closed),
        };

        // Left widgets.
        vm.MyPending = await ProjectRows(scoped.Where(p =>
                p.RaisedByUserId == actor.UserId &&
                (p.Status == DwoStatus.Draft || p.Status == DwoStatus.Submitted || p.Status == DwoStatus.ChangeRequested)))
            .OrderByDescending(r => r.CreatedAt).Take(8).ToListAsync();
        vm.RecentlyCreated = await ProjectRows(scoped)
            .OrderByDescending(r => r.CreatedAt).Take(8).ToListAsync();
        vm.ChangeRequested = await ProjectRows(scoped.Where(p => p.Status == DwoStatus.ChangeRequested))
            .OrderByDescending(r => r.CreatedAt).Take(8).ToListAsync();

        // Right widgets — both driven by the open follow-up row, same as the grid badge.
        var dueToday = scoped.Where(p => p.Status == DwoStatus.FollowUp &&
            p.FollowUps.Any(f => f.CompletedAt == null && f.DueDate == today));
        vm.FollowUpDueToday = await ProjectRows(dueToday).Take(8).ToListAsync();

        var overdue = scoped.Where(p => p.Status == DwoStatus.FollowUp &&
            p.FollowUps.Any(f => f.CompletedAt == null && f.DueDate < today));
        vm.Overdue = await ProjectRows(overdue).Take(8).ToListAsync();

        vm.ByDepartment = await scoped.GroupBy(p => p.Department.DeptName)
            .Select(g => new { g.Key, N = g.Count() })
            .OrderByDescending(x => x.N)
            .ToDictionaryAsync(x => x.Key, x => x.N);

        vm.DepartmentClosed = await scoped.Where(p => p.Status == DwoStatus.Closed)
            .GroupBy(p => p.Department.DeptName)
            .Select(g => new { g.Key, N = g.Count() })
            .OrderByDescending(x => x.N)
            .ToDictionaryAsync(x => x.Key, x => x.N);

        // Monthly trend, last 6 months including this one.
        var firstOfWindow = new DateTimeOffset(
            new DateTime(today.Year, today.Month, 1).AddMonths(-5), IstClock.Now.Offset);
        var raw = await scoped.Where(p => p.CreatedAt >= firstOfWindow)
            .GroupBy(p => new { p.CreatedAt.Year, p.CreatedAt.Month })
            .Select(g => new { g.Key.Year, g.Key.Month, N = g.Count() })
            .ToListAsync();
        for (var i = 5; i >= 0; i--)
        {
            var m = new DateTime(today.Year, today.Month, 1).AddMonths(-i);
            var hit = raw.FirstOrDefault(x => x.Year == m.Year && x.Month == m.Month);
            vm.MonthlyTrend[$"{m.Year:0000}-{m.Month:00}"] = hit?.N ?? 0;
        }
        return vm;
    }

    // The small DWO slice the common ApproveFlow dashboard shows. Deliberately cheap —
    // three aggregate queries — because it runs on the app's landing page.
    // ─────────────────────────────────────────────────────────
    // Common Dashboard (DWO section)
    // ─────────────────────────────────────────────────────────
    // Every card's count is the row total of the grid its Url opens: the same base set
    // (ScopedPoints / the "mine" rule) and the same single FilterStatus the Register page
    // applies, so a card and the grid behind it can never disagree.
    public async Task<Models.Dwo.DwoDashboardVM> GetCommonDashboardAsync(DwoActor actor)
    {
        var scoped = ScopedPoints(actor);
        var mine   = _db.DwoPoints.Where(p => p.RaisedByUserId == actor.UserId);

        var byStatus = await scoped.GroupBy(p => p.Status)
            .Select(g => new { g.Key, N = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.N);
        int S(string st) => byStatus.GetValueOrDefault(st, 0);

        static string RegisterUrl(string status) => "/DWO/Register?FilterStatus=" + Uri.EscapeDataString(status);

        var vm = new Models.Dwo.DwoDashboardVM();
        vm.Kpis.Add(new() { Key = Models.Dwo.DwoKpiKeys.DiscussionRegister,   Count = byStatus.Values.Sum(),             Url = "/DWO/Register" });
        vm.Kpis.Add(new() { Key = Models.Dwo.DwoKpiKeys.MyDiscussions,        Count = await mine.CountAsync(),          Url = "/DWO/MyDiscussions" });
        vm.Kpis.Add(new() { Key = Models.Dwo.DwoKpiKeys.ManagementDiscussion, Count = S(DwoStatus.ManagementDiscussion), Url = RegisterUrl(DwoStatus.ManagementDiscussion) });
        vm.Kpis.Add(new() { Key = Models.Dwo.DwoKpiKeys.PendingAction,        Count = S(DwoStatus.Submitted),            Url = RegisterUrl(DwoStatus.Submitted) });
        vm.Kpis.Add(new() { Key = Models.Dwo.DwoKpiKeys.Closed,               Count = S(DwoStatus.Closed),               Url = RegisterUrl(DwoStatus.Closed) });
        // The last slot carries Follow-up rather than Reports: Reports is a page, not a set of
        // discussions, so it has no honest count. The label override makes that explicit.
        vm.Kpis.Add(new() { Key = Models.Dwo.DwoKpiKeys.Reports, Label = "Follow-up", Count = S(DwoStatus.FollowUp), Url = RegisterUrl(DwoStatus.FollowUp) });

        // Same projection and ordering as the Register grid's first page.
        var recent = await ProjectRows(scoped)
            .OrderByDescending(r => r.CreatedAt)
            .Take(8)
            .ToListAsync();
        vm.RecentDiscussions = recent.Select(r => new Models.Dwo.DwoRecentDiscussionVM
        {
            DiscussionNo = r.PointNumber,
            Title        = r.Title,
            Department   = r.DeptName,
            Status       = r.Status,
            DueDate      = r.FollowUpDueDate is DateOnly due
                ? new DateTimeOffset(due.ToDateTime(TimeOnly.MinValue), IstClock.Now.Offset)
                : null,
            Url          = "/DWO/Detail/" + r.PointId,
        }).ToList();

        return vm;
    }

    public async Task<DwoDashboardSummaryVM> GetSummaryAsync(DwoActor actor)
    {
        var scoped = ScopedPoints(actor);
        var today = Today;
        var open = new[]
        {
            DwoStatus.Submitted, DwoStatus.ChangeRequested,
            DwoStatus.ManagementDiscussion, DwoStatus.FollowUp, DwoStatus.Hold
        };
        return new DwoDashboardSummaryVM
        {
            HasAccess = true,
            OpenDiscussions = await scoped.CountAsync(p => open.Contains(p.Status)),
            OverdueFollowUps = await scoped.CountAsync(p => p.Status == DwoStatus.FollowUp &&
                p.FollowUps.Any(f => f.CompletedAt == null && f.DueDate < today)),
            FollowUpDueToday = await ProjectRows(scoped.Where(p => p.Status == DwoStatus.FollowUp &&
                p.FollowUps.Any(f => f.CompletedAt == null && f.DueDate == today))).Take(5).ToListAsync(),
            ByStatus = await scoped.GroupBy(p => p.Status)
                .Select(g => new { g.Key, N = g.Count() })
                .ToDictionaryAsync(x => x.Key, x => x.N),
        };
    }

    // ─────────────────────────────────────────────────────────
    // Reports
    // ─────────────────────────────────────────────────────────
    public async Task<DwoReportVM> GetReportAsync(DwoReportVM filter, DwoActor actor)
    {
        filter.Actor = actor;
        filter.Departments = await VisibleDepartmentsAsync(actor);
        filter.Companies = await _db.Companies.Where(c => c.IsActive).OrderBy(c => c.CompanyName).ToListAsync();

        var q = ScopedPoints(actor);
        if (filter.FilterDeptId is int d && d > 0) q = q.Where(p => p.DepartmentId == d);
        if (filter.FilterCompanyId is int c && c > 0) q = q.Where(p => p.CompanyId == c);
        if (filter.FromDate is DateOnly from)
            q = q.Where(p => p.CreatedAt >= new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), IstClock.Now.Offset));
        if (filter.ToDate is DateOnly to)
            q = q.Where(p => p.CreatedAt <= new DateTimeOffset(to.ToDateTime(TimeOnly.MaxValue), IstClock.Now.Offset));

        var today = Today;
        switch (filter.Report)
        {
            case DwoReportKind.Department:
            {
                filter.Columns = new() { "Department", "Total", "Draft", "Submitted", "Management Discussion", "Follow-up", "Closed" };
                var rows = await q.GroupBy(p => p.Department.DeptName).Select(g => new
                {
                    Dept = g.Key,
                    Total = g.Count(),
                    Draft = g.Count(x => x.Status == DwoStatus.Draft),
                    Submitted = g.Count(x => x.Status == DwoStatus.Submitted),
                    Mgmt = g.Count(x => x.Status == DwoStatus.ManagementDiscussion),
                    Fu = g.Count(x => x.Status == DwoStatus.FollowUp),
                    Closed = g.Count(x => x.Status == DwoStatus.Closed),
                }).OrderByDescending(x => x.Total).ToListAsync();
                filter.Rows = rows.Select(r => new List<string>
                {
                    r.Dept, $"{r.Total}", $"{r.Draft}", $"{r.Submitted}", $"{r.Mgmt}", $"{r.Fu}", $"{r.Closed}"
                }).ToList();
                break;
            }

            case DwoReportKind.FollowUp:
            case DwoReportKind.Overdue:
            {
                var onlyOverdue = filter.Report == DwoReportKind.Overdue;
                filter.Columns = new() { "DWO No", "Title", "Department", "Owner", "Due Date", "Status", "Days" };
                var fq = _db.DwoFollowUps.Where(f => f.CompletedAt == null)
                    .Where(f => q.Select(p => p.PointId).Contains(f.PointId));
                if (onlyOverdue) fq = fq.Where(f => f.DueDate < today);
                var rows = await fq.OrderBy(f => f.DueDate).Select(f => new
                {
                    f.Point.PointNumber, f.Point.Title, Dept = f.Point.Department.DeptName,
                    Owner = f.OwnerUser.FullName, f.DueDate, f.Point.Status,
                }).Take(5000).ToListAsync();
                filter.Rows = rows.Select(r => new List<string>
                {
                    r.PointNumber, r.Title, r.Dept, r.Owner, r.DueDate.ToString("dd MMM yyyy"), r.Status,
                    $"{r.DueDate.DayNumber - today.DayNumber}"
                }).ToList();
                break;
            }

            case DwoReportKind.Closed:
            {
                filter.Columns = new() { "DWO No", "Task ID", "Title", "Department", "Raised By", "Raised On", "Closed On", "Resolution" };
                var rows = await q.Where(p => p.Status == DwoStatus.Closed)
                    .OrderByDescending(p => p.ReviewedAt).Take(5000).Select(p => new
                    {
                        p.PointNumber, p.TaskId, p.Title, Dept = p.Department.DeptName,
                        Raiser = p.RaisedByUser != null ? p.RaisedByUser.FullName : "",
                        p.CreatedAt, p.ReviewedAt, p.ReviewRemarks,
                    }).ToListAsync();
                filter.Rows = rows.Select(r => new List<string>
                {
                    r.PointNumber, r.TaskId ?? "", r.Title, r.Dept, r.Raiser,
                    r.CreatedAt.ToString("dd MMM yyyy"),
                    r.ReviewedAt?.ToString("dd MMM yyyy") ?? "",
                    r.ReviewRemarks ?? ""
                }).ToList();
                break;
            }

            default:   // Discussion Status Report
            {
                filter.Columns = new() { "DWO No", "Task ID", "Title", "Company", "Site", "Department", "Priority", "Status", "Raised By", "Raised On" };
                var rows = await q.OrderByDescending(p => p.CreatedAt).Take(5000).Select(p => new
                {
                    p.PointNumber, p.TaskId, p.Title,
                    Company = p.Company.CompanyName, Site = p.Location.LocationName,
                    Dept = p.Department.DeptName, p.Priority, p.Status,
                    Raiser = p.RaisedByUser != null ? p.RaisedByUser.FullName : "",
                    p.CreatedAt,
                }).ToListAsync();
                filter.Rows = rows.Select(r => new List<string>
                {
                    r.PointNumber, r.TaskId ?? "", r.Title, r.Company, r.Site, r.Dept,
                    r.Priority, r.Status, r.Raiser, r.CreatedAt.ToString("dd MMM yyyy")
                }).ToList();
                break;
            }
        }
        return filter;
    }

    // ─────────────────────────────────────────────────────────
    // Attachments
    // ─────────────────────────────────────────────────────────
    // Authorises the download: a file is reachable only through a point or a Set the caller
    // is already allowed to open.
    public async Task<DwoAttachment?> GetAttachmentAsync(int attachmentId, DwoActor actor)
    {
        var a = await _db.DwoAttachments
            .Include(x => x.Point).Include(x => x.Set)
            .FirstOrDefaultAsync(x => x.AttachmentId == attachmentId);
        if (a == null) return null;
        if (a.Point != null) return CanSee(a.Point, actor) ? a : null;
        if (a.Set != null)
            return actor.IsManagement || actor.DepartmentIds.Contains(a.Set.DepartmentId) ? a : null;
        return null;
    }

    // Writes files to disk with a GUID name and records the metadata — same convention and
    // same storage root as RequestDocuments. Rows are added to the change tracker but NOT
    // saved; the caller's SaveChangesAsync commits them with the rest of the action.
    private async Task<(List<DwoAttachment> saved, string? err)> SaveFilesAsync(
        IEnumerable<IFormFile>? files, string storagePath, int maxFileMb, DwoActor actor,
        string kind, int? pointId = null, int? setId = null)
    {
        var saved = new List<DwoAttachment>();
        var errors = new List<string>();
        var list = (files ?? Enumerable.Empty<IFormFile>()).Where(f => f != null && f.Length > 0).ToList();
        if (list.Count == 0) return (saved, null);

        Directory.CreateDirectory(storagePath);
        foreach (var file in list)
        {
            if (file.Length > maxFileMb * 1024L * 1024L)
            {
                errors.Add($"{file.FileName} exceeds {maxFileMb} MB — skipped.");
                continue;
            }
            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            var stored = $"{Guid.NewGuid()}{ext}";
            await using (var fs = File.Create(Path.Combine(storagePath, stored)))
                await file.CopyToAsync(fs);

            var att = new DwoAttachment
            {
                PointId = pointId, SetId = setId, Kind = kind,
                FileName = file.FileName, StoredFileName = stored,
                FileSizeBytes = file.Length, ContentType = file.ContentType,
                UploadedByUserId = actor.UserId,
            };
            _db.DwoAttachments.Add(att);
            saved.Add(att);
        }
        return (saved, errors.Count > 0 ? string.Join(" | ", errors) : null);
    }

    // Deletes the file behind an attachment only when no OTHER attachment row still points
    // at it — a reassignment clone shares the original's stored file.
    private void RemoveFiles(List<DwoAttachment> attachments, string storagePath)
    {
        foreach (var a in attachments)
        {
            var stillUsed = _db.DwoAttachments.Any(x =>
                x.StoredFileName == a.StoredFileName && x.AttachmentId != a.AttachmentId);
            if (stillUsed) continue;
            try
            {
                var full = Path.Combine(storagePath, a.StoredFileName);
                if (File.Exists(full)) File.Delete(full);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "DWO: could not delete {File}", a.StoredFileName);
            }
        }
    }

    // ─────────────────────────────────────────────────────────
    // History + notifications
    // ─────────────────────────────────────────────────────────
    // Append-only. Nothing in this service ever updates or removes a DwoHistory row.
    private void AddHistory(int? pointId, int? setId, DwoActor actor, string action, string? note)
    {
        _db.DwoHistories.Add(new DwoHistory
        {
            PointId = pointId, SetId = setId,
            ActorUserId = actor.UserId, ActorName = actor.UserName,
            Action = action,
            Note = string.IsNullOrWhiteSpace(note) ? null : note!.Trim(),
        });
    }

    // DWO notifications reuse the existing Notifications table with a NULL RequestId, so
    // the bell count and the Notifications page pick them up unchanged. The DWO number is
    // written into the message because the row cannot deep-link to a DWO point.
    private void QueueNotification(int userId, string type, string message)
    {
        _db.Notifications.Add(new Notification
        {
            UserId = userId, RequestId = null, NotificationType = type, Message = message,
        });
    }

    private async Task NotifyAsync(int userId, string type, string message)
    {
        QueueNotification(userId, type, message);
        await _db.SaveChangesAsync();
    }

    // "The HOD" of a department = every active user mapped to it whose role carries
    // Approve on the DWO Register screen. Resolved from the permission matrix so there is
    // no second place to keep in step.
    private async Task NotifyHodAsync(DwoPoint p, string message)
    {
        var ids = await HodUserIdsAsync(p.DepartmentId);
        foreach (var id in ids.Where(id => id != p.RaisedByUserId))
            QueueNotification(id, "dwo_action_required", message);
        if (ids.Count > 0) await _db.SaveChangesAsync();
    }

    private async Task NotifyDepartmentHodAsync(int departmentId, string message)
    {
        var ids = await HodUserIdsAsync(departmentId);
        foreach (var id in ids) QueueNotification(id, "dwo_reassigned", message);
        if (ids.Count > 0) await _db.SaveChangesAsync();
    }

    private Task<List<int>> HodUserIdsAsync(int departmentId) =>
        _db.UserDepartments
            .Where(ud => ud.DepartmentId == departmentId && ud.User.Status == "active" &&
                         ud.User.Role.Permissions.Any(rp => rp.ScreenName == DwoScreens.Register && rp.CanApprove))
            .Select(ud => ud.UserId).Distinct().ToListAsync();

    // Management = every active user at the top approval level of the Workflow Definitions
    // (see ManagementFromWorkflowsAsync).
    private async Task NotifyManagementAsync(string message)
    {
        var (roleIds, userIds) = await ManagementFromWorkflowsAsync();
        var ids = await _db.Users
            .Where(u => u.Status == "active" && (roleIds.Contains(u.RoleId) || userIds.Contains(u.UserId)))
            .Select(u => u.UserId).ToListAsync();
        foreach (var id in ids) QueueNotification(id, "dwo_action_required", message);
        if (ids.Count > 0) await _db.SaveChangesAsync();
    }
}
