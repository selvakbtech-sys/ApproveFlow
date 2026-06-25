namespace ApproveFlow.MVC.Controllers;

public class RequestsController : BaseCtrl
{
    private readonly IRequestService _svc;
    private readonly AppDbContext _db;
    private readonly IConfiguration _cfg;
    private readonly ILogger<RequestsController> _log;
    private readonly IWebHostEnvironment _env;

    public RequestsController(IRequestService svc, AppDbContext db, IConfiguration cfg, ILogger<RequestsController> log, IWebHostEnvironment env)
    { _svc = svc; _db = db; _cfg = cfg; _log = log; _env = env; }

    public async Task<IActionResult> Index(RequestListVM filter, int page = 1)
    {
        if (DenyIfNoPerm("Requests") is { } r1) return r1;
        ViewBag.UserRole = UserRole;
        ViewBag.CanDelete = UserRole == "Admin" ||
            await _db.Users.Where(u => u.UserId == UserId)
                .Select(u => u.Role.Permissions.Any(p => p.ScreenName == "Requests" && p.CanDelete))
                .FirstOrDefaultAsync();
        int? scopeUser = filter.FilterMine ? UserId : null;
        return View(await _svc.GetListAsync(filter, page, 20, scopeUser));
    }

    public async Task<IActionResult> MyApprovals(RequestListVM filter, int page = 1)
    {
        ViewBag.UserRole = UserRole;
        ViewBag.IsMyApprovals = true;
        return View("Index", await _svc.GetMyApprovalsAsync(UserId, filter, page, 20));
    }

    public async Task<IActionResult> Detail(int id, string tab = "details")
    {
        var vm = await _svc.GetDetailAsync(id, UserId);
        if (vm == null) { Err("Request not found."); return RedirectToAction("Index"); }
        vm.ActiveTab = tab;
        ViewBag.UserRole = UserRole;
        ViewBag.CanDelete = UserRole == "Admin" ||
            await _db.Users.Where(u => u.UserId == UserId)
                .Select(u => u.Role.Permissions.Any(p => p.ScreenName == "Requests" && p.CanDelete))
                .FirstOrDefaultAsync();
        return View(vm);
    }

    public async Task<IActionResult> Create()
        => View("Form", await _svc.BuildFormAsync(UserId));

    public async Task<IActionResult> Edit(int id)
    {
        var vm = await _svc.GetDetailAsync(id, UserId);
        if (vm == null) { Err("Not found."); return RedirectToAction("Index"); }
        var user = await _db.Users
            .Include(u => u.UserLocations).ThenInclude(ul => ul.Location)
            .Include(u => u.UserDepartments).ThenInclude(ud => ud.Department)
            .FirstOrDefaultAsync(u => u.UserId == UserId);
        vm.RequestTypes = await _db.RequestTypes.Where(t => t.IsActive).ToListAsync();
        vm.Locations = user?.UserLocations.Select(ul => ul.Location).Where(l => l.IsActive).OrderBy(l => l.LocationName).ToList() ?? new();
        vm.Departments = user?.UserDepartments.Select(ud => ud.Department).Where(d => d.IsActive).OrderBy(d => d.DeptName).ToList() ?? new();
        return View("Form", vm);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(RequestFormVM vm, string? saveAction)
    {
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
        vm.RequestTypes = await _db.RequestTypes.Where(t => t.IsActive).ToListAsync();
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
        var (ok, err) = await _svc.SubmitAsync(id, UserId);
        if (ok) Ok("Request submitted for approval."); else Err(err!);
        return RedirectToAction("Detail", new { id });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Action(int id, string action, string? comment, IFormFile? approvalFile)
    {
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

    public async Task<IActionResult> DownloadApproval(int id, int attachmentId)
    {
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
        var (ok, err) = await _svc.DeleteAsync(id, UserId, UserRole);
        if (ok) Ok("Request deleted."); else Err(err!);
        return RedirectToAction("Index");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Upload(int id, IFormFile file)
    {
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
        => View(await _svc.GetAgingAsync(filter));

    [HttpGet]
    public async Task<IActionResult> WfPreview(int requestTypeId, string priority)
    {
        var user = await _db.Users
            .Include(u => u.UserLocations).Include(u => u.UserDepartments)
            .FirstOrDefaultAsync(u => u.UserId == UserId);
        int locId = user?.UserLocations.FirstOrDefault()?.LocationId ?? 0;
        int deptId = user?.UserDepartments.FirstOrDefault()?.DepartmentId ?? 0;
        var wf = await _svc.MatchWorkflowAsync(locId, requestTypeId, priority, deptId);
        if (wf == null) return Json(new { matched = false });
        var steps = await _db.WorkflowSteps.Include(s => s.ApproverUser)
            .Where(s => s.WorkflowId == wf.WorkflowId).OrderBy(s => s.StepLevel)
            .Select(s => new { s.StepLevel, name = s.IsAutoAssignDeptManager ? "Dept Manager (auto)" : s.ApproverUser!.FullName })
            .ToListAsync();
        return Json(new { matched = true, workflowName = wf.WorkflowName, steps });
    }

    [AllowAnonymous]
    public async Task<IActionResult> Print(int id)
    {
        var vm = await _svc.GetDetailAsync(id, UserId);
        if (vm == null)
        {
            Err("Request not found.");
            return RedirectToAction("Index");
        }

        return View(vm);
    }

    public async Task<IActionResult> ExportExcel(RequestListVM filter)
    {
        var allItems = await _svc.GetAllForExportAsync(filter);

        var sb = new System.Text.StringBuilder();
        sb.Append('﻿');

        sb.AppendLine("Request #,Title,Requestor,Type,Priority,Amount,Status,Department,Location,Created Date,Submitted Date");

        foreach (var r in allItems)
        {
            sb.AppendLine(
                $"\"{r.RequestNumber}\"," +
                $"\"{r.Title?.Replace("\"", "\"\"")}\"," +
                $"\"{r.RequestorName}\"," +
                $"\"{r.RequestTypeName}\"," +
                $"\"{r.Priority}\"," +
                $"\"{(r.Amount.HasValue ? r.Amount.Value.ToString("N2") : "")}\"," +
                $"\"{r.Status}\"," +
                $"\"{r.DeptName}\"," +
                $"\"{r.LocationName}\"," +
                $"\"{r.CreatedAt:dd-MMM-yyyy}\"," +
                $"\"{(r.UpdatedAt != default ? r.UpdatedAt.ToString("dd-MMM-yyyy") : "")}\"");
        }

        var bytes = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
        return File(
            bytes,
            "text/csv",
            $"Requests_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
    }
}
