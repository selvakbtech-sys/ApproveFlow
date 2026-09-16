namespace ApproveFlow.MVC.Controllers;

// DWO (Discussion Work Order) module. Every screen and action lives here; nothing in this
// controller reaches into the Request/Approval flow, and no existing controller calls into
// it. Forms are posted from modals and always land back on the page they were opened from.
public class DwoController : BaseCtrl
{
    private readonly IDwoService _svc;
    private readonly IConfiguration _cfg;
    private readonly IWebHostEnvironment _env;

    public DwoController(IDwoService svc, IConfiguration cfg, IWebHostEnvironment env)
    { _svc = svc; _cfg = cfg; _env = env; }

    private static readonly int[] AllowedPageSizes = { 10, 25, 50, 100 };
    private const int DefaultPageSize = 10;
    private static int NormalizePageSize(int size) =>
        AllowedPageSizes.Contains(size) ? size : DefaultPageSize;

    // The caller's DWO identity, derived from the granular permission claims. Admin gets
    // Management powers, matching how BaseCtrl.HasPerm already treats Admin everywhere else;
    // otherwise BuildActorAsync resolves Management from the Workflow Definitions.
    private async Task<DwoActor> ActorAsync() => await _svc.BuildActorAsync(new DwoActor
    {
        UserId         = UserId,
        UserName       = User.FindFirstValue(ClaimTypes.Name) ?? "",
        IsManagement   = UserRole == "Admin",
        IsHod          = HasPerm(DwoScreens.Register, Perm.Approve),
        CanCreate      = HasPerm(DwoScreens.Register, Perm.Create),
        CanEditOwn     = HasPerm(DwoScreens.Register, Perm.Edit),
        CanWithdraw    = HasPerm(DwoScreens.Register, Perm.Delete),
        CanViewReports = HasPerm(DwoScreens.Reports),
    });

    private string StoragePath()
    {
        var configured = _cfg["AppSettings:FileStoragePath"] ?? "uploads";
        return Path.IsPathRooted(configured) ? configured : Path.Combine(_env.ContentRootPath, configured);
    }

    private int MaxFileMb() => int.TryParse(_cfg["AppSettings:MaxFileUploadMB"], out var mb) ? mb : 20;

    // Every modal action funnels through here: report the outcome and return to the page
    // the user was on, with its filters intact.
    private IActionResult Back(bool ok, string? err, string okMsg, string? returnUrl)
    {
        if (ok) Ok(okMsg); else Err(err ?? "Could not complete that action.");
        return Url.IsLocalUrl(returnUrl) ? Redirect(returnUrl!) : RedirectToAction(nameof(Register));
    }

    // ─────────────────────────────────────────────────────────
    // Pages
    // ─────────────────────────────────────────────────────────
    public async Task<IActionResult> Dashboard()
    {
        if (DenyIfNoPerm(DwoScreens.Register) is { } d) return d;
        return View(await _svc.GetDashboardAsync(await ActorAsync()));
    }

    public async Task<IActionResult> MyDiscussions(DwoRegisterVM filter, int page = 1, int pageSize = DefaultPageSize)
    {
        if (DenyIfNoPerm(DwoScreens.Register) is { } d) return d;
        var vm = await _svc.GetRegisterAsync(filter, await ActorAsync(), DwoGridMode.Mine, page, NormalizePageSize(pageSize));
        return View("Register", vm);
    }

    public async Task<IActionResult> Register(DwoRegisterVM filter, int page = 1, int pageSize = DefaultPageSize)
    {
        if (DenyIfNoPerm(DwoScreens.Register) is { } d) return d;
        var vm = await _svc.GetRegisterAsync(filter, await ActorAsync(), DwoGridMode.Register, page, NormalizePageSize(pageSize));
        return View(vm);
    }

    public async Task<IActionResult> Sets(int? deptId, string? status)
    {
        if (DenyIfNoPerm(DwoScreens.Register) is { } d) return d;
        return View(await _svc.GetSetsAsync(await ActorAsync(), deptId, status));
    }

    public async Task<IActionResult> Reports(DwoReportVM filter)
    {
        if (DenyIfNoPerm(DwoScreens.Reports) is { } d) return d;
        return View(await _svc.GetReportAsync(filter, await ActorAsync()));
    }

    // ─────────────────────────────────────────────────────────
    // Modal partials (fetched by the page, rendered into the shared modal shell)
    // ─────────────────────────────────────────────────────────
    // Discussion details is a full page with server-side tabs, matching Request Details.
    // `returnUrl` carries the list it was opened from so Back goes there, while the action
    // forms post back to this page so the user sees the result of what they just did.
    public async Task<IActionResult> Detail(int id, string? tab = null, string? returnUrl = null)
    {
        if (DenyIfNoPerm(DwoScreens.Register) is { } d) return d;
        var vm = await _svc.GetPointAsync(id, await ActorAsync());
        if (vm == null)
        {
            Err("That discussion was not found, or it is outside your department.");
            return RedirectToAction(nameof(Register));
        }
        ViewBag.Tab = tab is "timeline" or "documents" ? tab : "details";
        ViewBag.ReturnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl : null;
        return View(vm);
    }

    // ── Create / Edit are full pages, not popups ──────────────
    // They use the shared layout and the same two-column shape as New Request, so the two
    // modules' create screens look and behave identically. `returnUrl` carries the list the
    // user came from, so Back, Cancel and a successful save all land back there.
    [HttpGet]
    public async Task<IActionResult> Create(string? returnUrl = null)
    {
        if (DenyIfNoPerm(DwoScreens.Register, Perm.Create) is { } d) return d;
        ViewBag.ReturnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl : null;
        return View("Form", await _svc.NewFormAsync(await ActorAsync()));
    }

    // "+ Add point for discussion" from an open Set. Gated like the Set actions (Management
    // or the owning HOD — the service checks ownership), not on Register|Create, because the
    // point is raised on the Set's behalf straight into Management Discussion.
    [HttpGet]
    public async Task<IActionResult> AddToSet(int setId, string? returnUrl = null)
    {
        var actor = await ActorAsync();
        if (!actor.IsManagement && !actor.IsHod) return Deny();
        var vm = await _svc.NewFormForSetAsync(setId, actor);
        if (vm == null)
        {
            Err("That Set is not open for new discussions, or it is not yours to manage.");
            return RedirectToAction(nameof(Sets));
        }
        ViewBag.ReturnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl : null;
        return View("Form", vm);
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, string? returnUrl = null)
    {
        if (DenyIfNoPerm(DwoScreens.Register, Perm.Edit) is { } d) return d;
        var vm = await _svc.EditFormAsync(id, await ActorAsync());
        if (vm == null)
        {
            Err("That discussion is not yours to edit, or it has moved past Draft.");
            return RedirectToAction(nameof(MyDiscussions));
        }
        ViewBag.ReturnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl : null;
        return View("Form", vm);
    }

    public async Task<IActionResult> SetModal(int id)
    {
        if (DenyIfNoPerm(DwoScreens.Register) is { } d) return d;
        var vm = await _svc.GetSetAsync(id, await ActorAsync());
        if (vm == null) return NotFound();
        return PartialView("_SetModal", vm);
    }

    // The HOD's "Build Set" modal — lists the ticked points before submission.
    public async Task<IActionResult> BuildSetModal(string ids)
    {
        if (DenyIfNoPerm(DwoScreens.Register, Perm.Approve) is { } d) return d;
        var actor = await ActorAsync();
        var pointIds = ParseIds(ids);
        if (pointIds.Count == 0) return BadRequest("Select at least one Submitted discussion first.");
        var filter = new DwoRegisterVM { FilterStatus = { DwoStatus.Submitted } };
        var all = await _svc.GetRegisterForExportAsync(filter, actor, DwoGridMode.Register);
        ViewBag.Points = all.Where(p => pointIds.Contains(p.PointId)).ToList();
        return PartialView("_BuildSetModal", actor);
    }

    private static List<int> ParseIds(string? csv) =>
        (csv ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, out var n) ? n : 0).Where(n => n > 0).Distinct().ToList();

    // ─────────────────────────────────────────────────────────
    // Point actions
    // ─────────────────────────────────────────────────────────
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(DwoPointFormVM vm, string saveAction = "draft", string? returnUrl = null)
    {
        bool intoSet = vm.PointId == 0 && vm.SetId > 0;
        var actor = await ActorAsync();
        if (intoSet)
        {
            if (!actor.IsManagement && !actor.IsHod) return Deny();
        }
        else if (DenyIfNoPerm(DwoScreens.Register, vm.PointId == 0 ? Perm.Create : Perm.Edit) is { } d) return d;
        var (ok, err, _) = await _svc.SavePointAsync(vm, actor, saveAction, StoragePath(), MaxFileMb());
        if (!ok)
        {
            Err(err ?? "Could not save that discussion.");
            await _svc.FillFormLookupsAsync(vm, actor);
            ViewBag.ReturnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl : null;
            return View("Form", vm);
        }
        if (intoSet)
            return Back(true, null, "Added to the Set.", returnUrl ?? Url.Action(nameof(Sets)));
        var msg = saveAction == "submit" ? "Discussion submitted." : "Discussion saved.";
        return Back(true, null, msg, returnUrl ?? Url.Action(nameof(MyDiscussions)));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(int id, string? returnUrl = null)
    {
        if (DenyIfNoPerm(DwoScreens.Register, Perm.Create) is { } d) return d;
        var actor = await ActorAsync();
        var (ok, err) = await _svc.SubmitPointAsync(id, actor);
        // Management has no HOD above it — its draft goes straight to Management Discussion.
        return Back(ok, err, actor.IsManagement ? "Discussion created." : "Submitted to HOD.", returnUrl);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Withdraw(int id, string? returnUrl = null)
    {
        if (DenyIfNoPerm(DwoScreens.Register, Perm.Delete) is { } d) return d;
        var (ok, err) = await _svc.WithdrawPointAsync(id, await ActorAsync());
        return Back(ok, err, "Withdrawn — the discussion is back in Draft.", returnUrl);
    }

    // HOD: send back for clarification, or reject.
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Review(int id, string action, string? note, string? returnUrl = null)
    {
        if (DenyIfNoPerm(DwoScreens.Register, Perm.Approve) is { } d) return d;
        var (ok, err) = await _svc.ReviewAsync(id, await ActorAsync(), action, note);
        var msg = action == "reject" ? "Discussion rejected." : "Sent back for clarification.";
        return Back(ok, err, msg, returnUrl);
    }

    // ─────────────────────────────────────────────────────────
    // Set actions
    // ─────────────────────────────────────────────────────────
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SubmitSet(DwoSetSubmitVM vm, string? returnUrl = null)
    {
        if (DenyIfNoPerm(DwoScreens.Register, Perm.Approve) is { } d) return d;
        var (ok, err, number) = await _svc.SubmitSetAsync(vm, await ActorAsync(), StoragePath(), MaxFileMb());
        return Back(ok, err, $"Set {number} submitted to management.", returnUrl);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ScheduleAppointment(int setId, DateOnly date, string? note, string? returnUrl = null)
    {
        var actor = await ActorAsync();
        if (!actor.IsManagement) return Deny();
        var (ok, err) = await _svc.ScheduleAppointmentAsync(setId, actor, date, note);
        return Back(ok, err, "Appointment scheduled.", returnUrl);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> PostponeAppointment(int setId, DateOnly date, string? reason, string? returnUrl = null)
    {
        var actor = await ActorAsync();
        if (!actor.IsManagement) return Deny();
        var (ok, err) = await _svc.PostponeAppointmentAsync(setId, actor, date, reason);
        return Back(ok, err, "Appointment postponed.", returnUrl);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveSummaryFile(int setId, IFormFile file, string? returnUrl = null)
    {
        if (DenyIfNoPerm(DwoScreens.Register) is { } d) return d;
        var (ok, err) = await _svc.SaveSummaryFileAsync(setId, await ActorAsync(), file, StoragePath(), MaxFileMb());
        return Back(ok, err, "Summary document saved.", returnUrl);
    }

    // ─────────────────────────────────────────────────────────
    // Management discussion actions
    // ─────────────────────────────────────────────────────────
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Discuss(
        int id, string action, string? remarks, int? followUpOwnerId, DateOnly? dueDate,
        int? targetDepartmentId, IFormFile? file, string? returnUrl = null)
    {
        var actor = await ActorAsync();
        if (!actor.IsManagement) return Deny();
        var input = new DwoDiscussInput
        {
            Remarks = remarks, FollowUpOwnerId = followUpOwnerId, DueDate = dueDate,
            TargetDepartmentId = targetDepartmentId, File = file,
        };
        var (ok, err) = await _svc.DiscussAsync(id, actor, action, input, StoragePath(), MaxFileMb());
        var msg = action switch
        {
            "close"    => "Discussion closed.",
            "followup" => "Follow-up scheduled.",
            "hold"     => "Discussion put on hold.",
            "reassign" => "Discussion reassigned — a new draft was created in the target department.",
            _          => "Updated.",
        };
        return Back(ok, err, msg, returnUrl);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Resume(int id, string? returnUrl = null)
    {
        var actor = await ActorAsync();
        if (!actor.IsManagement) return Deny();
        var (ok, err) = await _svc.ResumeAsync(id, actor);
        return Back(ok, err, "Moved back to management discussion.", returnUrl);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CompleteFollowUp(int id, string note, IFormFile? file, string? returnUrl = null)
    {
        if (DenyIfNoPerm(DwoScreens.Register) is { } d) return d;
        var (ok, err) = await _svc.CompleteFollowUpAsync(id, await ActorAsync(), note, file, StoragePath(), MaxFileMb());
        return Back(ok, err, "Follow-up completed — sent back to management.", returnUrl);
    }

    // ─────────────────────────────────────────────────────────
    // Attachments
    // ─────────────────────────────────────────────────────────
    public async Task<IActionResult> Download(int id)
    {
        if (DenyIfNoPerm(DwoScreens.Register) is { } d) return d;
        var att = await _svc.GetAttachmentAsync(id, await ActorAsync());
        if (att == null) return NotFound();
        var full = Path.Combine(StoragePath(), att.StoredFileName);
        if (!System.IO.File.Exists(full)) { Err("That file is no longer on the server."); return RedirectToAction(nameof(Register)); }
        return PhysicalFile(full, att.ContentType ?? "application/octet-stream", att.FileName);
    }

    // ─────────────────────────────────────────────────────────
    // Export — same CSV shape and filename convention as the Requests grid export
    // ─────────────────────────────────────────────────────────
    public async Task<IActionResult> Export(DwoRegisterVM filter, string mode = DwoGridMode.Register)
    {
        if (DenyIfNoPerm(DwoScreens.Register) is { } d) return d;
        var rows = await _svc.GetRegisterForExportAsync(filter, await ActorAsync(), mode);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("DWO No,Set,Task ID,Title,Category,Priority,Company,Site,Department,Status,Raised By,Raised On,Follow-up Owner,Due Date");
        foreach (var r in rows)
            sb.AppendLine(string.Join(",", new[]
            {
                r.PointNumber, r.SetNumber ?? "", r.TaskId ?? "", r.Title, r.Category, r.Priority,
                r.CompanyName, r.LocationName, r.DeptName, r.Status, r.RaisedByName,
                r.CreatedAt.ToString("dd MMM yyyy"), r.FollowUpOwnerName ?? "",
                r.FollowUpDueDate?.ToString("dd MMM yyyy") ?? "",
            }.Select(Csv)));

        var name = $"dwo-{mode}-{IstClock.Now:yyyyMMdd-HHmm}.csv";
        return File(new System.Text.UTF8Encoding(true).GetBytes(sb.ToString()), "text/csv", name);
    }

    public async Task<IActionResult> ExportReport(DwoReportVM filter)
    {
        if (DenyIfNoPerm(DwoScreens.Reports) is { } d) return d;
        var vm = await _svc.GetReportAsync(filter, await ActorAsync());
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(string.Join(",", vm.Columns.Select(Csv)));
        foreach (var row in vm.Rows) sb.AppendLine(string.Join(",", row.Select(Csv)));
        var name = $"dwo-{vm.Report}-{IstClock.Now:yyyyMMdd-HHmm}.csv";
        return File(new System.Text.UTF8Encoding(true).GetBytes(sb.ToString()), "text/csv", name);
    }

    // Quotes a CSV field. Excel-safe: doubles embedded quotes and wraps anything with a
    // comma, quote or newline.
    private static string Csv(string? value)
    {
        var v = value ?? "";
        return v.Contains(',') || v.Contains('"') || v.Contains('\n') || v.Contains('\r')
            ? $"\"{v.Replace("\"", "\"\"")}\""
            : v;
    }
}
