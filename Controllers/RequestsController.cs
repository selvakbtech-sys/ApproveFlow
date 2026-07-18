namespace ApproveFlow.MVC.Controllers;

public class RequestsController : BaseCtrl
{
    private readonly IRequestService _svc;
    private readonly IUserMappingService _mapping;
    private readonly AppDbContext _db;
    private readonly IConfiguration _cfg;
    private readonly ILogger<RequestsController> _log;
    private readonly IWebHostEnvironment _env;

    public RequestsController(IRequestService svc, IUserMappingService mapping, AppDbContext db, IConfiguration cfg, ILogger<RequestsController> log, IWebHostEnvironment env)
    { _svc = svc; _mapping = mapping; _db = db; _cfg = cfg; _log = log; _env = env; }

    // Non-admins are ALWAYS scoped to their own requests (RequestorId == UserId).
    // Admins see all, or only their own when the "My Requests" filter is active.
    private int? ScopeUserId(bool filterMine) =>
        UserRole == "Admin" ? (filterMine ? UserId : (int?)null) : UserId;

    public async Task<IActionResult> Index(RequestListVM filter, int page = 1)
    {
        if (DenyIfNoPerm("Requests") is { } r1) return r1;
        ViewBag.UserRole = UserRole;
        ViewBag.CanDelete = UserRole == "Admin" ||
            await _db.Users.Where(u => u.UserId == UserId)
                .Select(u => u.Role.Permissions.Any(p => p.ScreenName == "Requests" && p.CanDelete))
                .FirstOrDefaultAsync();
        int? scopeUser = ScopeUserId(filter.FilterMine);
        return View(await _svc.GetListAsync(filter, page, 20, scopeUser, UserId));
    }

    // Approval Requests page for the logged-in approver. Reached from the dashboard KPI
    // cards as /Requests/Approval?status=Pending|Approved|Rejected|Clarification (empty =
    // All). The friendly `status` token is mapped to concrete request statuses inside the
    // service (ApprovalStatusFilter) so the grid totals always match the KPI counts, and
    // it is echoed to the view so the Status dropdown pre-selects the matching option.
    public async Task<IActionResult> Approval(RequestListVM filter, string? status, int page = 1)
    {
        if (DenyIfNoPerm("Requests") is { } r1) return r1;
        ViewBag.UserRole = UserRole;
        ViewBag.IsMyApprovals = true;
        ViewBag.ApprovalStatus = status;
        return View("Index", await _svc.GetMyApprovalsAsync(UserId, filter, page, 20, status));
    }

    // Backward-compatible alias for older links; delegates to the Approval page.
    public Task<IActionResult> MyApprovals(RequestListVM filter, string? status, int page = 1)
        => Approval(filter, status, page);

    // Territory grid — reached from the dashboard "My Territory" KPI cards as
    // /Requests/Territory?FilterStatus=… The service scopes to the logged-in user's
    // department/location (the SAME scope as the territory KPI counts) for EVERY role, so
    // the grid total always matches the KPI. Renders the shared Index view in territory mode.
    public async Task<IActionResult> Territory(RequestListVM filter, int page = 1)
    {
        if (DenyIfNoPerm("Requests") is { } r1) return r1;
        ViewBag.UserRole = UserRole;
        ViewBag.IsTerritory = true;
        return View("Index", await _svc.GetTerritoryListAsync(UserId, filter, page, 20));
    }

    public async Task<IActionResult> Detail(int id, string tab = "details", string? mode = null)
    {
        if (DenyIfNoPerm("Requests") is { } rd) return rd;
        // A user may only open a request they created or are an approver on (admin = any).
        if (!await _svc.CanAccessRequestAsync(id, UserId, UserRole)) return Deny();
        var vm = await _svc.GetDetailAsync(id, UserId);
        if (vm == null) { Err("Request not found."); return RedirectToAction("Index"); }
        // Clarification Response mode: only meaningful for the request's owner while it is
        // awaiting clarification (server-side gate — CanSubmitClarification already checks
        // owner + status). Force the Details tab so the response panel is in view.
        bool clarificationMode = mode == "clarification" && vm.CanSubmitClarification;
        vm.ActiveTab = clarificationMode ? "details" : tab;
        ViewBag.ClarificationMode = clarificationMode;
        ViewBag.UserRole = UserRole;
        ViewBag.CanDelete = UserRole == "Admin" ||
            await _db.Users.Where(u => u.UserId == UserId)
                .Select(u => u.Role.Permissions.Any(p => p.ScreenName == "Requests" && p.CanDelete))
                .FirstOrDefaultAsync();
        return View(vm);
    }

    // GET New Request page — requires View permission on the New Request screen.
    public async Task<IActionResult> Create()
    {
        if (DenyIfNoPerm("New Request") is { } rc) return rc;
        return View("Form", await _svc.BuildFormAsync(UserId));
    }

    public async Task<IActionResult> Edit(int id)
    {
        if (DenyIfNoPerm("Requests", Perm.Edit) is { } re) return re;
        if (!await _svc.CanAccessRequestAsync(id, UserId, UserRole)) return Deny();
        var vm = await _svc.GetDetailAsync(id, UserId);
        if (vm == null) { Err("Not found."); return RedirectToAction("Index"); }
        // Editing the original request is no longer how clarification is answered — the
        // requester replies through the Submit Clarification panel on the Detail page.
        if (vm.Status == "Clarification Needed")
        {
            Err("This request is awaiting clarification. Use \"Submit Clarification\" to respond.");
            return RedirectToAction("Detail", new { id });
        }
        var user = await _db.Users
            .Include(u => u.UserLocations).ThenInclude(ul => ul.Location)
            .Include(u => u.UserDepartments).ThenInclude(ud => ud.Department)
            .FirstOrDefaultAsync(u => u.UserId == UserId);
        vm.RequestTypes = await _mapping.GetMappedRequestTypesAsync(UserId);
        vm.Locations = user?.UserLocations.Select(ul => ul.Location).Where(l => l.IsActive).OrderBy(l => l.LocationName).ToList() ?? new();
        vm.Departments = user?.UserDepartments.Select(ud => ud.Department).Where(d => d.IsActive).OrderBy(d => d.DeptName).ToList() ?? new();
        return View("Form", vm);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(RequestFormVM vm, string? saveAction)
    {
        // Permission: creating a new request needs New Request → Create;
        // editing an existing one needs Requests → Edit. (Submit is part of Create.)
        var neededPerm = vm.RequestId == 0
            ? DenyIfNoPerm("New Request", Perm.Create)
            : DenyIfNoPerm("Requests", Perm.Edit);
        if (neededPerm is { } rp) return rp;

        // Part 3 — never trust the posted RequestTypeId. It must belong to the
        // logged-in user's active User Mapping, otherwise refuse to save (HTTP 403).
        if (!await _mapping.IsRequestTypeAllowedAsync(UserId, vm.RequestTypeId))
            return Deny();

        if (!ModelState.IsValid)
        {
            await ReloadFormDropdowns(vm);
            return View("Form", vm);
        }

        int requestId;

        if (vm.RequestId == 0)
        {
            var (ok, err, newId) = await _svc.CreateAsync(vm, UserId);
            if (!ok)
            {
                Err(err!);
                await ReloadFormDropdowns(vm);
                return View("Form", vm);
            }
            requestId = newId;
        }
        else
        {
            var (ok, err) = await _svc.UpdateAsync(vm, UserId);
            if (!ok)
            {
                Err(err!);
                await ReloadFormDropdowns(vm);
                return View("Form", vm);
            }
            requestId = vm.RequestId;
        }

        if (vm.DeleteDocIds?.Any() == true)
        {
            var docsToDelete = await _db.RequestDocuments
                .Where(d => d.RequestId == requestId && vm.DeleteDocIds.Contains(d.DocumentId))
                .ToListAsync();
            foreach (var doc in docsToDelete)
            {
                var p = System.IO.Path.Combine(GetStoragePath(), doc.StoredFileName);
                if (System.IO.File.Exists(p)) System.IO.File.Delete(p);
            }
            _db.RequestDocuments.RemoveRange(docsToDelete);
            await _db.SaveChangesAsync();
        }

        var storagePath = GetStoragePath();
        Directory.CreateDirectory(storagePath);
        var maxMb = int.Parse(_cfg["AppSettings:MaxFileUploadMB"] ?? "20");
        var uploadErrors = new List<string>();

        foreach (var file in (vm.NewFiles ?? new List<IFormFile>()).Where(f => f != null && f.Length > 0))
        {
            if (file.Length > maxMb * 1024L * 1024L)
            {
                uploadErrors.Add($"{file.FileName} exceeds {maxMb} MB — skipped.");
                continue;
            }
            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            var stored = $"{Guid.NewGuid()}{ext}";
            await using (var fs = System.IO.File.Create(System.IO.Path.Combine(storagePath, stored)))
                await file.CopyToAsync(fs);
            _db.RequestDocuments.Add(new RequestDocument
            {
                RequestId = requestId,
                FileName = file.FileName,
                StoredFileName = stored,
                FileSizeBytes = file.Length,
                ContentType = file.ContentType,
                UploadedByUserId = UserId,
            });
        }
        if (uploadErrors.Any())
            Err(string.Join(" | ", uploadErrors));

        await _db.SaveChangesAsync();

        if (saveAction == "submit")
        {
            var (ok, err) = await _svc.SubmitAsync(requestId, UserId);
            if (ok) Ok("Request saved and submitted for approval.");
            else Err(err!);
            return RedirectToAction("Detail", new { id = requestId });
        }

        Ok("Request saved as draft.");
        return RedirectToAction("Detail", new { id = requestId });
    }

    private async Task ReloadFormDropdowns(RequestFormVM vm)
    {
        var user = await _db.Users
            .Include(u => u.UserLocations).ThenInclude(ul => ul.Location)
            .Include(u => u.UserDepartments).ThenInclude(ud => ud.Department)
            .FirstOrDefaultAsync(u => u.UserId == UserId);
        vm.RequestTypes = await _mapping.GetMappedRequestTypesAsync(UserId);
        vm.Locations = user?.UserLocations.Select(ul => ul.Location).Where(l => l.IsActive).OrderBy(l => l.LocationName).ToList() ?? new();
        vm.Departments = user?.UserDepartments.Select(ud => ud.Department).Where(d => d.IsActive).OrderBy(d => d.DeptName).ToList() ?? new();
        vm.AllDeptNames = vm.Departments.Select(d => d.DeptName).ToList();
        vm.AllLocationNames = vm.Locations.Select(l => l.LocationName).ToList();

        vm.RequestorName = user?.FullName ?? vm.RequestorName;
        if (string.IsNullOrEmpty(vm.LocationName))
            vm.LocationName = vm.Locations.FirstOrDefault(l => l.LocationId == vm.LocationId)?.LocationName
                              ?? vm.Locations.FirstOrDefault()?.LocationName ?? "";
        if (string.IsNullOrEmpty(vm.DeptName))
            vm.DeptName = vm.Departments.FirstOrDefault(d => d.DepartmentId == vm.DepartmentId)?.DeptName
                          ?? vm.Departments.FirstOrDefault()?.DeptName ?? "";
    }

    private string GetStoragePath()
    {
        var configured = _cfg["AppSettings:FileStoragePath"] ?? "uploads";
        if (System.IO.Path.IsPathRooted(configured))
            return configured;
        return System.IO.Path.Combine(_env.ContentRootPath, configured);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(int id)
    {
        // Submitting a request needs New Request → Create.
        if (DenyIfNoPerm("New Request", Perm.Create) is { } rp) return rp;

        // Part 3 — the request's type must still belong to the user's active mapping.
        var reqInfo = await _db.Requests.Where(r => r.RequestId == id)
            .Select(r => new { r.RequestTypeId, r.Status }).FirstOrDefaultAsync();
        if (reqInfo == null) { Err("Request not found."); return RedirectToAction("Index"); }
        if (!await _mapping.IsRequestTypeAllowedAsync(UserId, reqInfo.RequestTypeId))
            return Deny();

        // Clarification is answered through Submit Clarification, not by resubmitting the
        // request — block the old edit-and-resubmit path even if reached directly.
        if (reqInfo.Status == "Clarification Needed")
        {
            Err("This request is awaiting clarification. Use \"Submit Clarification\" to respond.");
            return RedirectToAction("Detail", new { id });
        }

        var (ok, err) = await _svc.SubmitAsync(id, UserId);
        if (ok) Ok("Request submitted for approval."); else Err(err!);
        return RedirectToAction("Detail", new { id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Action(int id, string action, string? comment, IFormFile? approvalFile)
    {
        // Approve / Reject / Clarification are approval actions.
        if (DenyIfNoPerm("Requests", Perm.Approve) is { } rp) return rp;
        if (approvalFile != null && approvalFile.Length > 0)
        {
            var maxMb = int.Parse(_cfg["AppSettings:MaxFileUploadMB"] ?? "20");
            if (approvalFile.Length > maxMb * 1024L * 1024L)
            {
                Err($"Attachment exceeds {maxMb} MB.");
                return RedirectToAction("Detail", new { id });
            }
            var allowed = new[] { ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".jpg", ".jpeg", ".png" };
            var ext = Path.GetExtension(approvalFile.FileName).ToLowerInvariant();
            if (!allowed.Contains(ext))
            {
                Err("Invalid file type. Allowed: PDF, DOC, DOCX, XLS, XLSX, JPG, PNG.");
                return RedirectToAction("Detail", new { id });
            }
        }
        var maxFileMb = int.Parse(_cfg["AppSettings:MaxFileUploadMB"] ?? "20");
        var (ok, err) = await _svc.ProcessActionAsync(id, UserId, action, comment, approvalFile, GetStoragePath(), maxFileMb);
        if (ok) Ok($"Action '{action}' recorded."); else Err(err!);
        return RedirectToAction("Detail", new { id });
    }

    // Requester responds to a clarification request (does NOT edit the original request).
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SubmitClarification(int id, string clarificationRemarks, IFormFile? clarificationFile)
    {
        if (DenyIfNoPerm("Requests") is { } rp) return rp;
        if (!await _svc.CanAccessRequestAsync(id, UserId, UserRole)) return Deny();

        if (clarificationFile != null && clarificationFile.Length > 0)
        {
            var maxMb = int.Parse(_cfg["AppSettings:MaxFileUploadMB"] ?? "20");
            if (clarificationFile.Length > maxMb * 1024L * 1024L)
            {
                Err($"Attachment exceeds {maxMb} MB.");
                return RedirectToAction("Detail", new { id });
            }
            var allowed = new[] { ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".jpg", ".jpeg", ".png" };
            var ext = Path.GetExtension(clarificationFile.FileName).ToLowerInvariant();
            if (!allowed.Contains(ext))
            {
                Err("Invalid file type. Allowed: PDF, DOC, DOCX, XLS, XLSX, JPG, PNG.");
                return RedirectToAction("Detail", new { id });
            }
        }

        var maxFileMb = int.Parse(_cfg["AppSettings:MaxFileUploadMB"] ?? "20");
        var (ok, err) = await _svc.SubmitClarificationResponseAsync(id, UserId, clarificationRemarks, clarificationFile, GetStoragePath(), maxFileMb);
        if (ok) Ok("Clarification submitted. The approver has been notified."); else Err(err!);
        return RedirectToAction("Detail", new { id });
    }

    // Requestor revokes their own request (Pending Approval / Clarification Needed only).
    // Dedicated flow — independent of Clarification. All rules are enforced server-side in
    // RequestService.RevokeRequestAsync (owner + status + duplicate guard).
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Revoke(int id, string revokeRemark)
    {
        if (DenyIfNoPerm("Requests") is { } rp) return rp;
        if (!await _svc.CanAccessRequestAsync(id, UserId, UserRole)) return Deny();

        var (ok, err) = await _svc.RevokeRequestAsync(id, UserId, revokeRemark);
        if (ok) Ok("Request revoked. The current approver has been notified."); else Err(err!);
        return RedirectToAction("Detail", new { id });
    }

    public async Task<IActionResult> DownloadApproval(int id, int attachmentId)
    {
        if (DenyIfNoPerm("Requests") is { } rp) return rp;
        if (!await _svc.CanAccessRequestAsync(id, UserId, UserRole)) return Deny();
        var att = await _db.ApprovalAttachments
            .FirstOrDefaultAsync(a => a.AttachmentId == attachmentId && a.RequestId == id);
        if (att == null) return NotFound();
        var fullPath = System.IO.Path.Combine(GetStoragePath(), att.StoredFileName);
        if (!System.IO.File.Exists(fullPath)) return NotFound();
        return PhysicalFile(fullPath, att.ContentType ?? "application/octet-stream", att.FileName);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        if (DenyIfNoPerm("Requests", Perm.Delete) is { } rp) return rp;
        var (ok, err) = await _svc.DeleteAsync(id, UserId, UserRole);
        if (ok) Ok("Request deleted."); else Err(err!);
        return RedirectToAction("Index");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Upload(int id, IFormFile file)
    {
        if (DenyIfNoPerm("Requests", Perm.Edit) is { } rp) return rp;
        if (!await _svc.CanAccessRequestAsync(id, UserId, UserRole)) return Deny();
        var maxMb = int.Parse(_cfg["AppSettings:MaxFileUploadMB"] ?? "20");
        if (file == null || file.Length == 0) { Err("No file selected."); return RedirectToAction("Detail", new { id, tab = "documents" }); }
        if (file.Length > maxMb * 1024L * 1024L) { Err($"File exceeds {maxMb} MB."); return RedirectToAction("Detail", new { id, tab = "documents" }); }
        var path = GetStoragePath();
        Directory.CreateDirectory(path);
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        var stored = $"{Guid.NewGuid()}{ext}";
        await using (var fs = System.IO.File.Create(System.IO.Path.Combine(path, stored)))
            await file.CopyToAsync(fs);
        _db.RequestDocuments.Add(new RequestDocument
        {
            RequestId = id,
            FileName = file.FileName,
            StoredFileName = stored,
            FileSizeBytes = file.Length,
            ContentType = file.ContentType,
            UploadedByUserId = UserId,
        });
        await _db.SaveChangesAsync();
        Ok("File uploaded.");
        return RedirectToAction("Detail", new { id, tab = "documents" });
    }

    public async Task<IActionResult> Download(int id, int docId)
    {
        if (DenyIfNoPerm("Requests") is { } rp) return rp;
        if (!await _svc.CanAccessRequestAsync(id, UserId, UserRole)) return Deny();
        var doc = await _db.RequestDocuments
            .FirstOrDefaultAsync(d => d.DocumentId == docId && d.RequestId == id);
        if (doc == null) return NotFound();
        var fullPath = System.IO.Path.Combine(GetStoragePath(), doc.StoredFileName);
        if (!System.IO.File.Exists(fullPath)) return NotFound();
        return PhysicalFile(fullPath, doc.ContentType ?? "application/octet-stream", doc.FileName);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteDoc(int id, int docId)
    {
        if (DenyIfNoPerm("Requests", Perm.Edit) is { } rp) return rp;
        if (!await _svc.CanAccessRequestAsync(id, UserId, UserRole)) return Deny();
        var doc = await _db.RequestDocuments.FirstOrDefaultAsync(d => d.DocumentId == docId && d.RequestId == id);
        if (doc != null)
        {
            var p = System.IO.Path.Combine(_cfg["AppSettings:FileStoragePath"] ?? "uploads", doc.StoredFileName);
            if (System.IO.File.Exists(p)) System.IO.File.Delete(p);
            _db.RequestDocuments.Remove(doc);
            await _db.SaveChangesAsync();
            Ok("Document removed.");
        }
        return RedirectToAction("Detail", new { id, tab = "documents" });
    }

    public async Task<IActionResult> Aging(AgingReportVM filter)
    {
        if (DenyIfNoPerm("Requests") is { } rp) return rp;
        // Non-admins only see aging for their own requests.
        int? scope = UserRole == "Admin" ? null : UserId;
        return View(await _svc.GetAgingAsync(filter, scope));
    }

    [HttpGet]
    public async Task<IActionResult> WfPreview(int requestTypeId, string priority, int locationId, int departmentId, decimal? amount = null)
    {
        // Workflow preview is used by the New Request / Edit request forms.
        if (!HasPerm("New Request") && !HasPerm("Requests")) return Deny();
        // Match against the Location/Department SELECTED on the form — never the user's
        // default location/department. Workflow matching is independent of User Mapping.
        var wf = await _svc.MatchWorkflowAsync(locationId, requestTypeId, priority, departmentId, UserId, amount);
        if (wf == null) return Json(new { matched = false });
        var steps = await _db.WorkflowSteps
            .Include(s => s.ApproverRole).Include(s => s.ApproverUser).ThenInclude(u => u!.Role)
            .Where(s => s.WorkflowId == wf.WorkflowId).OrderBy(s => s.StepLevel)
            .Select(s => new
            {
                s.StepLevel,
                name = s.IsAutoAssignDeptManager ? "Dept Manager (auto)"
                    : (s.ApproverRole != null ? s.ApproverRole.RoleName
                       : (s.ApproverUser != null ? s.ApproverUser.Role.RoleName : "Unassigned"))
            })
            .ToListAsync();
        return Json(new { matched = true, workflowName = wf.WorkflowName, steps });
    }

    // Print is reached from the authenticated Requests list/detail. It must NOT be
    // anonymous (that would expose any request by raw id). The token-based share flow
    // uses ViewShared below, not this action.
    public async Task<IActionResult> Print(int id)
    {
        if (DenyIfNoPerm("Requests") is { } rp) return rp;
        if (!await _svc.CanAccessRequestAsync(id, UserId, UserRole)) return Deny();
        var vm = await _svc.GetDetailAsync(id, UserId);
        if (vm == null)
        {
            Err("Request not found.");
            return RedirectToAction("Index");
        }

        return View(vm);
    }

    // ── Notification Alert: secure read-only share link ───────
    [AllowAnonymous]
    [Route("r/{token}")]
    public async Task<IActionResult> ViewShared(string token)
    {
        var tok = await _db.RequestViewTokens.AsNoTracking().FirstOrDefaultAsync(t => t.Token == token);
        if (tok == null || tok.ExpiresAt < IstClock.Now)
            return View("ViewSharedExpired");

        var vm = await _svc.GetDetailAsync(tok.RequestId, 0);
        if (vm == null || vm.Status == "Deleted")
            return View("ViewSharedExpired");

        ViewBag.Token = token;
        return View("ViewShared", vm);
    }

    [AllowAnonymous]
    public async Task<IActionResult> ViewSharedDownload(string token, int docId)
    {
        var tok = await _db.RequestViewTokens.AsNoTracking().FirstOrDefaultAsync(t => t.Token == token);
        if (tok == null || tok.ExpiresAt < IstClock.Now) return NotFound();

        var doc = await _db.RequestDocuments.FirstOrDefaultAsync(d => d.DocumentId == docId && d.RequestId == tok.RequestId);
        if (doc == null) return NotFound();
        var fullPath = System.IO.Path.Combine(GetStoragePath(), doc.StoredFileName);
        if (!System.IO.File.Exists(fullPath)) return NotFound();
        return PhysicalFile(fullPath, doc.ContentType ?? "application/octet-stream", doc.FileName);
    }

    [AllowAnonymous]
    public async Task<IActionResult> ViewSharedDownloadApproval(string token, int attachmentId)
    {
        var tok = await _db.RequestViewTokens.AsNoTracking().FirstOrDefaultAsync(t => t.Token == token);
        if (tok == null || tok.ExpiresAt < IstClock.Now) return NotFound();

        var att = await _db.ApprovalAttachments.FirstOrDefaultAsync(a => a.AttachmentId == attachmentId && a.RequestId == tok.RequestId);
        if (att == null) return NotFound();
        var fullPath = System.IO.Path.Combine(GetStoragePath(), att.StoredFileName);
        if (!System.IO.File.Exists(fullPath)) return NotFound();
        return PhysicalFile(fullPath, att.ContentType ?? "application/octet-stream", att.FileName);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ExportExcel(RequestListVM filter)
    {
        if (DenyIfNoPerm("Requests") is { } rp) return rp;
        if (string.IsNullOrWhiteSpace(filter.FilterDateFrom))
            return Json(new { error = "From Date is required." });
        if (string.IsNullOrWhiteSpace(filter.FilterDateTo))
            return Json(new { error = "To Date is required." });
        if (!DateOnly.TryParse(filter.FilterDateFrom, out var from))
            return Json(new { error = "From Date is invalid." });
        if (!DateOnly.TryParse(filter.FilterDateTo, out var to))
            return Json(new { error = "To Date is invalid." });
        if (from > to)
            return Json(new { error = "From Date cannot be greater than To Date." });

        // Non-admins only export their own requests (Admins export all, or own if filtered).
        var allItems = await _svc.GetAllForExportAsync(filter, ScopeUserId(filter.FilterMine));

        if (!allItems.Any())
            return Json(new { error = "No records found for the selected date range." });

        var sb = new System.Text.StringBuilder();
        sb.Append('﻿');

        sb.AppendLine("Request #,Title,Requestor,Type,Priority,Amount,Status,Department,Location,Created Date,Submitted Date,Revoked By,Revoked Date,Revoke Remark");

        foreach (var r in allItems)
        {
            sb.AppendLine(
                $"\"{r.DisplayRequestNumber}\"," +
                $"\"{r.Title?.Replace("\"", "\"\"")}\"," +
                $"\"{r.RequestorName}\"," +
                $"\"{r.RequestTypeName}\"," +
                $"\"{r.Priority}\"," +
                $"\"{(r.Amount.HasValue ? r.Amount.Value.ToString("N2") : "")}\"," +
                $"\"{(r.Status == "Open" ? "Pending Approval" : r.Status)}\"," +
                $"\"{r.DeptName}\"," +
                $"\"{r.LocationName}\"," +
                $"\"{r.CreatedAt:dd-MMM-yyyy}\"," +
                $"\"{(r.UpdatedAt != default ? r.UpdatedAt.ToString("dd-MMM-yyyy") : "")}\"," +
                $"\"{r.RevokedByName}\"," +
                $"\"{(r.RevokedDate.HasValue ? r.RevokedDate.Value.ToString("dd-MMM-yyyy") : "")}\"," +
                $"\"{r.RevokedRemark?.Replace("\"", "\"\"")}\"");
        }

        var bytes = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
        return File(
            bytes,
            "text/csv",
            $"Requests_{filter.FilterDateFrom}_to_{filter.FilterDateTo}.csv");
    }
}
