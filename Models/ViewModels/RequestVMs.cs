namespace ApproveFlow.MVC.Models.ViewModels;

public class RequestListVM
{
    public List<ReqListItemVM> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)Math.Max(1, PageSize));
    public string? FilterStatus { get; set; }
    public int? FilterLocationId { get; set; }
    public int? FilterDeptId { get; set; }
    public int? FilterTypeId { get; set; }
    public string? FilterPriority { get; set; }
    public string? FilterDateFrom { get; set; }
    public string? FilterDateTo { get; set; }
    public string? SearchTerm { get; set; }
    public bool FilterMine { get; set; }
    public List<Location> Locations { get; set; } = new();
    public List<Department> Departments { get; set; } = new();
    public List<RequestType> RequestTypes { get; set; } = new();
}

public class ReqListItemVM
{
    public int RequestId { get; set; }
    public string RequestNumber { get; set; } = "";
    public string DisplayRequestNumber => Helpers.RequestNumberHelper.Format(RequestNumber);
    public string Title { get; set; } = "";
    public string Status { get; set; } = "";
    public string Priority { get; set; } = "";
    public decimal? Amount { get; set; }
    public string RequestorName { get; set; } = "";
    public string LocationName { get; set; } = "";
    public string DeptName { get; set; } = "";
    public string RequestTypeName { get; set; } = "";
    public int DocumentCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int AgeDays => IstClock.DaysSince(UpdatedAt);
    // True when the logged-in user is this request's owner — set server-side so the grid
    // can show the requester-only Clarification action on a Clarification Needed request.
    public bool IsRequester { get; set; }
}

public class RequestFormVM
{
    public int RequestId { get; set; }
    [Required, MaxLength(500)] public string Title { get; set; } = "";
    [MaxLength(500, ErrorMessage = "Description / Justification cannot exceed 500 characters")]
    public string? Description { get; set; }
    [MaxLength(500, ErrorMessage = "Additional Notes cannot exceed 500 characters")]
    public string? Notes { get; set; }
    [Required(ErrorMessage = "Please select a request type")]
    public int RequestTypeId { get; set; }
    [Required] public string Priority { get; set; } = "Medium";
    [Range(0.01, double.MaxValue, ErrorMessage = "Amount must be positive")]
    public decimal? Amount { get; set; }
    public int LocationId { get; set; }
    public int DepartmentId { get; set; }
    public string RequestorName { get; set; } = "";
    public string LocationName { get; set; } = "";
    public string DeptName { get; set; } = "";
    public string Status { get; set; } = "Draft";
    public string RequestNumber { get; set; } = "";
    public string DisplayRequestNumber => Helpers.RequestNumberHelper.Format(RequestNumber);
    public string? MatchedWorkflowName { get; set; }
    public bool CanAct { get; set; }
    public int? CurrentActingStep { get; set; }
    // Clarification Response flow: the requester (owner) of a "Clarification Needed"
    // request replies with remarks/attachment instead of editing the original request.
    public bool CanSubmitClarification { get; set; }
    public string? LatestClarificationComment { get; set; }
    public string ActiveTab { get; set; } = "details";
    public List<ApproverStepVM> ApprovalChain { get; set; } = new();
    public List<ActionLogVM> ActionLog { get; set; } = new();
    public List<DocumentVM> Documents { get; set; } = new();
    public List<ApprovalAttachmentVM> ApprovalAttachments { get; set; } = new();
    public int TotalDocumentCount => Documents.Count + ApprovalAttachments.Count;
    public List<IFormFile> NewFiles { get; set; } = new();
    public List<int> DeleteDocIds { get; set; } = new();
    public List<Location> Locations { get; set; } = new();
    public List<Department> Departments { get; set; } = new();
    public List<string> AllDeptNames { get; set; } = new();
    public List<string> AllLocationNames { get; set; } = new();
    public List<RequestType> RequestTypes { get; set; } = new();
    public static readonly string[] Priorities = { "Low", "Medium", "High", "Critical" };
    public string RequestTypeName { get; set; } = "";
    public DateTimeOffset? SubmittedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class ApproverStepVM
{
    public int StepLevel { get; set; }
    public string ApproverName { get; set; } = "";
    public string ApproverRole { get; set; } = "";
    public string ApproverEmail { get; set; } = "";
    public string Status { get; set; } = "waiting";
    public string? Comment { get; set; }
    public DateTimeOffset? ActionAt { get; set; }
    public string? ActionText { get; set; }
}

public class ActionLogVM
{
    public string ActorName { get; set; } = "";
    public string Action { get; set; } = "";
    public string? Comment { get; set; }
    public int? StepLevel { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public List<HistoryAttachmentVM> Attachments { get; set; } = new();
    // True when this action originated from a WhatsApp reply/button (derived from
    // WhatsAppMessageHistory), so the timeline can show a "via WhatsApp" indicator.
    public bool IsWhatsApp { get; set; }
}

public class HistoryAttachmentVM
{
    public int Id { get; set; }
    public string FileName { get; set; } = "";
    public string AttachmentType { get; set; } = "";
    // Direct URL for statically-served files (WhatsApp reply attachments). Null for
    // controller-served attachments (Requestor/Approval), which use their download actions.
    public string? Url { get; set; }
}

public class DocumentVM
{
    public int DocumentId { get; set; }
    public string FileName { get; set; } = "";
    public long FileSizeBytes { get; set; }
    public string? ContentType { get; set; }
    public DateTimeOffset UploadedAt { get; set; }
    public string UploadedByName { get; set; } = "";
    public string FormattedSize =>
        FileSizeBytes > 1_048_576 ? $"{FileSizeBytes / 1_048_576.0:F1} MB" : $"{FileSizeBytes / 1024} KB";
}

public class ApprovalAttachmentVM
{
    public int AttachmentId { get; set; }
    public int StepLevel { get; set; }
    public string ActionType { get; set; } = "";
    public string FileName { get; set; } = "";
    public long FileSizeBytes { get; set; }
    public string? ContentType { get; set; }
    public DateTimeOffset UploadedAt { get; set; }
    public string UploadedByName { get; set; } = "";
    // Set for statically-served WhatsApp reply attachments (linked directly). Null
    // for controller-served approval attachments (use the DownloadApproval action).
    public string? Url { get; set; }
    public string FormattedSize =>
        FileSizeBytes > 1_048_576 ? $"{FileSizeBytes / 1_048_576.0:F1} MB" : $"{FileSizeBytes / 1024} KB";
}

public class AgingReportVM
{
    public List<AgingItemVM> Items { get; set; } = new();
    public int TotalOpen { get; set; }
    public int AvgAge { get; set; }
    public int MaxAge { get; set; }
    public decimal TotalValue { get; set; }
    public string? FilterStatus { get; set; }
    public int? FilterLocationId { get; set; }
    public int? FilterDeptId { get; set; }
    public int? FilterTypeId { get; set; }
    public string? FilterPriority { get; set; }
    public string? FilterDateFrom { get; set; }
    public string? FilterDateTo { get; set; }
    public string SortBy { get; set; } = "age";
    public List<Location> Locations { get; set; } = new();
    public List<Department> Departments { get; set; } = new();
    public List<RequestType> RequestTypes { get; set; } = new();
}

public class AgingItemVM
{
    public int RequestId { get; set; }
    public string RequestNumber { get; set; } = "";
    public string DisplayRequestNumber => Helpers.RequestNumberHelper.Format(RequestNumber);
    public string Title { get; set; } = "";
    public string RequestorName { get; set; } = "";
    public string LocationName { get; set; } = "";
    public string DeptName { get; set; } = "";
    public string RequestTypeName { get; set; } = "";
    public decimal? Amount { get; set; }
    public string Priority { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int AgeDays { get; set; }
    public string AgeBucket =>
        AgeDays <= 3 ? "0-3 days" :
        AgeDays <= 7 ? "4-7 days" :
        AgeDays <= 14 ? "8-14 days" :
        AgeDays <= 30 ? "15-30 days" : "30+ days";
}
