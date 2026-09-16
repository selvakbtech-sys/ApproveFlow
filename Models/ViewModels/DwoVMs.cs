namespace ApproveFlow.MVC.Models.ViewModels;

// ─────────────────────────────────────────────────────────────
// Who the current user is, in DWO terms. Derived once per request from the granular
// permission claims (see DwoController.Actor) and handed to every service call and view,
// so the register, the modals and the KPI queries can never disagree about what the user
// is allowed to see or do.
// ─────────────────────────────────────────────────────────────
public class DwoActor
{
    public int UserId { get; set; }
    public string UserName { get; set; } = "";

    // Management: sees every department and performs the close/hold/follow-up/reassign
    // actions. Backed by "Management Discussion|Approve".
    public bool IsManagement { get; set; }
    // HOD: reviews Submitted points in their own department and bundles them into Sets.
    // Backed by "DWO Register|Approve".
    public bool IsHod { get; set; }

    public bool CanCreate { get; set; }
    public bool CanEditOwn { get; set; }
    public bool CanWithdraw { get; set; }
    public bool CanViewReports { get; set; }

    // Departments the user is mapped to in User Master. Empty for Management, which is
    // never scoped by department.
    public List<int> DepartmentIds { get; set; } = new();

    public string RoleLabel => IsManagement ? "Management" : IsHod ? "Team HOD" : "Team Member";
}

// ─────────────────────────────────────────────────────────────
// Register / My Discussions / Management Discussion grid
// ─────────────────────────────────────────────────────────────
public class DwoRegisterVM
{
    // ── Filters (round-trip on the form, the pager and the Clear link) ──
    // Company / Site / Department / Status are multi-select: repeated query values bind to
    // the list, and a single "?FilterStatus=Closed" (dashboard cards, chips) still binds.
    public List<int> FilterCompanyId { get; set; } = new();
    public List<int> FilterLocationId { get; set; } = new();
    public List<int> FilterDeptId { get; set; } = new();
    public string? FilterCategory { get; set; }
    public string? FilterPriority { get; set; }
    public List<string> FilterStatus { get; set; } = new();
    public string? FilterTaskId { get; set; }
    public int? FilterRaisedById { get; set; }
    public DateOnly? FromDate { get; set; }
    public DateOnly? ToDate { get; set; }
    public string? SearchTerm { get; set; }

    // ── Results ──
    public List<DwoPointRowVM> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling(TotalCount / (double)PageSize) : 0;

    // Status chip counts. Computed over the same filtered set as the grid but ignoring the
    // status filter itself, so clicking a chip never changes the other chips' numbers.
    public Dictionary<string, int> StatusCounts { get; set; } = new();

    // ── Dropdown sources ──
    public List<Company> Companies { get; set; } = new();
    public List<Location> Locations { get; set; } = new();
    public List<Department> Departments { get; set; } = new();
    public List<string> Categories { get; set; } = new();
    public List<UserOptionVM> Raisers { get; set; } = new();

    public DwoActor Actor { get; set; } = new();
    // Which page is rendering this grid — drives the heading, the chip set and the action.
    public string Mode { get; set; } = DwoGridMode.Register;

    // Every current filter as route values, so chips, pager and Export build their links
    // from ONE place and carry identical filters. `status` overrides the status list
    // (null keeps the current one); list values become repeated query parameters.
    public Microsoft.AspNetCore.Routing.RouteValueDictionary RouteValues(
        IEnumerable<string>? status = null, int? page = null)
    {
        var rv = new Microsoft.AspNetCore.Routing.RouteValueDictionary
        {
            ["FilterCompanyId"]  = FilterCompanyId,
            ["FilterLocationId"] = FilterLocationId,
            ["FilterDeptId"]     = FilterDeptId,
            ["FilterCategory"]   = FilterCategory,
            ["FilterPriority"]   = FilterPriority,
            ["FilterStatus"]     = (status ?? FilterStatus).ToList(),
            ["FilterTaskId"]     = FilterTaskId,
            ["FilterRaisedById"] = FilterRaisedById,
            ["FromDate"]         = FromDate?.ToString("yyyy-MM-dd"),
            ["ToDate"]           = ToDate?.ToString("yyyy-MM-dd"),
            ["SearchTerm"]       = SearchTerm,
            ["pageSize"]         = PageSize,
        };
        if (page.HasValue) rv["page"] = page.Value;
        return rv;
    }

    // The status list with one value toggled — what clicking a chip navigates to.
    public List<string> ToggledStatus(string status) =>
        FilterStatus.Contains(status)
            ? FilterStatus.Where(s => s != status).ToList()
            : FilterStatus.Append(status).ToList();
}

public static class DwoGridMode
{
    public const string Register   = "register";    // department register
    public const string Mine       = "mine";        // My Discussions — created by me
    public const string Management = "management";  // Management Discussion page
}

public class DwoPointRowVM
{
    public int PointId { get; set; }
    public string PointNumber { get; set; } = "";
    public string? SetNumber { get; set; }
    public string? TaskId { get; set; }
    public string Title { get; set; } = "";
    public string Category { get; set; } = "";
    public string Priority { get; set; } = "";
    public string Status { get; set; } = "";
    public string CompanyName { get; set; } = "";
    public string LocationName { get; set; } = "";
    public string DeptName { get; set; } = "";
    public string RaisedByName { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public string? FollowUpOwnerName { get; set; }
    public DateOnly? FollowUpDueDate { get; set; }
    public bool IsFollowUpOverdue { get; set; }
    // Only Submitted points in the HOD's own department can join a Set.
    public bool IsSelectableForSet { get; set; }
}

public class UserOptionVM
{
    public int UserId { get; set; }
    public string FullName { get; set; } = "";
}

// ─────────────────────────────────────────────────────────────
// Create / Edit discussion point (modal)
// ─────────────────────────────────────────────────────────────
public class DwoPointFormVM
{
    public int PointId { get; set; }
    public string? PointNumber { get; set; }

    [MaxLength(60)]
    public string? TaskId { get; set; }
    [Required(ErrorMessage = "Discussion title is required.")]
    [MaxLength(250)]
    public string Title { get; set; } = "";
    [Required(ErrorMessage = "Description is required.")]
    public string Description { get; set; } = "";
    [MaxLength(60)]
    public string Category { get; set; } = "General";
    [MaxLength(20)]
    public string Priority { get; set; } = "Medium";

    [Range(1, int.MaxValue, ErrorMessage = "Company is required.")]
    public int CompanyId { get; set; }
    [Range(1, int.MaxValue, ErrorMessage = "Site is required.")]
    public int LocationId { get; set; }
    [Range(1, int.MaxValue, ErrorMessage = "Department is required.")]
    public int DepartmentId { get; set; }

    // Posted as repeated fields; blanks are dropped on save.
    public List<string> ReferenceLinks { get; set; } = new();
    public List<IFormFile>? NewFiles { get; set; }
    public List<int> RemoveAttachmentIds { get; set; } = new();

    // Existing attachments, shown on Edit only.
    public List<DwoAttachment> Attachments { get; set; } = new();

    public string Status { get; set; } = DwoStatus.Draft;

    // Set when the form is "Add a point for discussion" on an open Set: the point is created
    // straight into Management Discussion, in the Set's department, and joined to the Set.
    public int SetId { get; set; }
    public string? SetNumber { get; set; }

    // Dropdown sources.
    public List<Company> Companies { get; set; } = new();
    public List<Location> Locations { get; set; } = new();
    public List<Department> Departments { get; set; } = new();
    public List<string> Categories { get; set; } = new();
    public DwoActor Actor { get; set; } = new();
}

// ─────────────────────────────────────────────────────────────
// Discussion details modal
// ─────────────────────────────────────────────────────────────
public class DwoPointDetailVM
{
    public DwoPoint Point { get; set; } = null!;
    public DwoSet? Set { get; set; }
    public DwoPoint? OriginPoint { get; set; }
    public DwoFollowUp? OpenFollowUp { get; set; }
    public List<DwoFollowUp> FollowUps { get; set; } = new();
    public List<DwoHistory> History { get; set; } = new();
    public List<DwoAttachment> Attachments { get; set; } = new();
    public List<DwoReferenceLink> ReferenceLinks { get; set; } = new();
    public List<Department> Departments { get; set; } = new();   // reassignment targets
    public List<UserOptionVM> FollowUpOwners { get; set; } = new();
    public DwoActor Actor { get; set; } = new();

    // ── What the current user may do with this point right now ──
    public bool CanEdit { get; set; }
    public bool CanSubmit { get; set; }
    // HOD (own department) or Management may bundle this Submitted point into a Set from here.
    public bool CanBuildSet { get; set; }
    public bool CanWithdraw { get; set; }
    public bool CanReview { get; set; }              // HOD: send back / reject / add to Set
    public bool CanDiscuss { get; set; }             // Management: close / follow-up / hold / reassign
    public bool CanResume { get; set; }              // Management: bring a held point back
    public bool CanCompleteFollowUp { get; set; }

    public bool IsFollowUpOverdue => OpenFollowUp?.IsOverdue == true;
}

// ─────────────────────────────────────────────────────────────
// Sets
// ─────────────────────────────────────────────────────────────
public class DwoSetListVM
{
    public List<DwoSetRowVM> Items { get; set; } = new();
    public List<Department> Departments { get; set; } = new();
    public int? FilterDeptId { get; set; }
    public string? FilterStatus { get; set; }        // awaiting | completed
    public Dictionary<string, int> StatusCounts { get; set; } = new();
    public DwoActor Actor { get; set; } = new();
}

public class DwoSetRowVM
{
    public int SetId { get; set; }
    public string SetNumber { get; set; } = "";
    public string DeptName { get; set; } = "";
    public int PointCount { get; set; }
    public int UndecidedCount { get; set; }
    public string AppointmentStatus { get; set; } = "";
    public DateOnly? AppointmentDate { get; set; }
    public string SubmittedByName { get; set; } = "";
    public DateTimeOffset SubmittedAt { get; set; }
    // A Set is Completed once no point on it is still in Management Discussion.
    public bool IsCompleted => UndecidedCount == 0 && PointCount > 0;
}

public class DwoSetDetailVM
{
    public DwoSet Set { get; set; } = null!;
    public List<DwoPointRowVM> Points { get; set; } = new();
    public List<DwoAttachment> SupportingDocuments { get; set; } = new();
    public DwoAttachment? SummaryFile { get; set; }
    public List<DwoHistory> History { get; set; } = new();
    public DwoActor Actor { get; set; } = new();
    public bool CanManage { get; set; }              // Management, or the owning HOD
    public bool IsCompleted { get; set; }
}

// Payload for the HOD's "Build Set & Submit to Management" modal.
public class DwoSetSubmitVM
{
    public List<int> PointIds { get; set; } = new();
    public DateOnly? RequestedAppointmentDate { get; set; }
    public string? AppointmentNotes { get; set; }
    public List<IFormFile>? SupportingFiles { get; set; }
    public IFormFile? SummaryFile { get; set; }
}

// ─────────────────────────────────────────────────────────────
// DWO dashboard
// ─────────────────────────────────────────────────────────────
public class DwoDashboardVM
{
    // KPI cards.
    public int Total { get; set; }
    public int Draft { get; set; }
    public int Submitted { get; set; }
    public int ManagementDiscussion { get; set; }
    public int FollowUp { get; set; }
    public int Closed { get; set; }

    // Left widgets.
    public List<DwoPointRowVM> MyPending { get; set; } = new();
    public List<DwoPointRowVM> RecentlyCreated { get; set; } = new();
    public List<DwoPointRowVM> ChangeRequested { get; set; } = new();

    // Right widgets.
    public List<DwoPointRowVM> FollowUpDueToday { get; set; } = new();
    public List<DwoPointRowVM> Overdue { get; set; } = new();
    public Dictionary<string, int> ByDepartment { get; set; } = new();

    // Charts (CSS bars, same treatment as the main dashboard).
    public Dictionary<string, int> MonthlyTrend { get; set; } = new();   // yyyy-MM → count
    public Dictionary<string, int> ByStatus { get; set; } = new();
    public Dictionary<string, int> DepartmentClosed { get; set; } = new();

    public DwoActor Actor { get; set; } = new();
}

// The DWO slice shown on the common ApproveFlow dashboard. Kept separate from
// DwoDashboardVM so adding a card there can never disturb the DWO dashboard's own query.
public class DwoDashboardSummaryVM
{
    public int OpenDiscussions { get; set; }
    public int OverdueFollowUps { get; set; }
    public List<DwoPointRowVM> FollowUpDueToday { get; set; } = new();
    public Dictionary<string, int> ByStatus { get; set; } = new();
    public bool HasAccess { get; set; }
}

// ─────────────────────────────────────────────────────────────
// Reports
// ─────────────────────────────────────────────────────────────
public class DwoReportVM
{
    public string Report { get; set; } = DwoReportKind.Status;
    public int? FilterDeptId { get; set; }
    public int? FilterCompanyId { get; set; }
    public DateOnly? FromDate { get; set; }
    public DateOnly? ToDate { get; set; }

    public List<string> Columns { get; set; } = new();
    public List<List<string>> Rows { get; set; } = new();

    public List<Department> Departments { get; set; } = new();
    public List<Company> Companies { get; set; } = new();
    public DwoActor Actor { get; set; } = new();

    public string Heading => Report switch
    {
        DwoReportKind.Department => "Department Wise Report",
        DwoReportKind.FollowUp   => "Follow-up Report",
        DwoReportKind.Overdue    => "Overdue Report",
        DwoReportKind.Closed     => "Closed Discussion Report",
        _                        => "Discussion Status Report",
    };
}

public static class DwoReportKind
{
    public const string Status     = "status";
    public const string Department = "department";
    public const string FollowUp   = "followup";
    public const string Overdue    = "overdue";
    public const string Closed     = "closed";

    public static readonly (string Key, string Label)[] All =
    {
        (Status,     "Discussion Status Report"),
        (Department, "Department Wise Report"),
        (FollowUp,   "Follow-up Report"),
        (Overdue,    "Overdue Report"),
        (Closed,     "Closed Discussion Report"),
    };
}
