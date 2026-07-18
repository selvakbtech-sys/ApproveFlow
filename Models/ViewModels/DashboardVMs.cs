namespace ApproveFlow.MVC.Models.ViewModels;

public class DashboardVM
{
    public int Total { get; set; }
    public int Draft { get; set; }
    public int Open { get; set; }
    public int PartialApproved { get; set; }
    public int PendingApproval { get; set; }
    public int Approved { get; set; }
    public int Rejected { get; set; }
    public int ClarificationNeeded { get; set; }
    public int MyPendingApprovals { get; set; }
    public int MyTotal { get; set; }
    public int MyDraft { get; set; }
    public int MyOpen { get; set; }
    public int MyPartialApproved { get; set; }
    public int MyPending { get; set; }
    public int MyApproved { get; set; }
    public int MyRejected { get; set; }
    public decimal TotalAmountApproved { get; set; }
    public decimal TotalAmountPending { get; set; }
    public List<ReqListItemVM> RecentRequests { get; set; } = new();
    public List<ReqListItemVM> MyRecentRequests { get; set; } = new();
    public List<ReqListItemVM> UrgentPending { get; set; } = new();
    public List<ReqListItemVM> ClarificationItems { get; set; } = new();
    public List<LocationStatVM> ByLocation { get; set; } = new();
    public Dictionary<string, int> ByType { get; set; } = new();
    public Dictionary<string, int> ByPriority { get; set; } = new();
    public Dictionary<string, int> MonthlyTrend { get; set; } = new();
    public List<MyPendingApprovalItemVM> MyPendingApprovalRequests { get; set; } = new();
    public List<RecentActivityVM> ActivityFeed { get; set; } = new();
    public List<DeptStatVM> ByDepartment { get; set; } = new();
    public int MyClarification { get; set; }
    public int ClarificationItemsTotal { get; set; }
    public int AssignedToMeTotal { get; set; }
    public int AssignedToMePending { get; set; }
    public int ApprovedByMe { get; set; }
    public int RejectedByMe { get; set; }
    public int ClarificationByMe { get; set; }
}

public class MyPendingApprovalItemVM
{
    public int RequestId { get; set; }
    public string RequestNumber { get; set; } = "";
    public string DisplayRequestNumber => Helpers.RequestNumberHelper.Format(RequestNumber);
    public string Title { get; set; } = "";
    public string Priority { get; set; } = "";
    public string RequestorName { get; set; } = "";
    public int StepLevel { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public class LocationStatVM
{
    public string LocationName { get; set; } = "";
    public int Total { get; set; }
    public int Pending { get; set; }
    public int Approved { get; set; }
    public int Rejected { get; set; }
}

public class RecentActivityVM
{
    public int RequestId { get; set; }
    public string RequestNumber { get; set; } = "";
    public string DisplayRequestNumber => Helpers.RequestNumberHelper.Format(RequestNumber);
    public string Title { get; set; } = "";
    public string ActorName { get; set; } = "";
    public string Action { get; set; } = "";
    public string? Comment { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string ActionIcon => Action switch
    {
        "approved"  => "✅",
        "rejected"  => "❌",
        "submitted" => "📤",
        "saved_draft" => "📝",
        "clarification_requested" => "💬",
        "resubmitted" => "🔄",
        _ => "📌"
    };
    public string ActionLabel => Action switch
    {
        "approved"  => "Approved",
        "rejected"  => "Rejected",
        "submitted" => "Submitted",
        "saved_draft" => "Saved Draft",
        "clarification_requested" => "Clarification Requested",
        "resubmitted" => "Resubmitted",
        _ => Action
    };
}

public class DeptStatVM
{
    public string DeptName { get; set; } = "";
    public int Total { get; set; }
    public int Pending { get; set; }
    public int Approved { get; set; }
    public int Rejected { get; set; }
}

public class DashboardSummaryDto
{
    public string CategoryName          { get; set; } = "";
    public int    PendingCount          { get; set; }
    public int    PartialApprovedCount  { get; set; }
    public int    ApprovedCount         { get; set; }
    public int    RejectedCount         { get; set; }
    public int    ClarificationCount    { get; set; }
}

public class SummaryTableVM
{
    public string Title { get; set; } = "";
    public string LabelHeader { get; set; } = "";
    public string Icon { get; set; } = "";
    public List<DashboardSummaryDto> Rows { get; set; } = new();
}

public class MyRequestsTabVM
{
    public int MyTotal           { get; set; }
    public int MyPending         { get; set; }
    public int MyPartialApproved { get; set; }
    public int MyApproved        { get; set; }
    public int MyRejected        { get; set; }
    public int MyDraft           { get; set; }
    public int MyClarification   { get; set; }
    public int MyRevoked         { get; set; }
    public List<DashboardSummaryDto> ByTypeSummary { get; set; } = new();
    public List<DashboardSummaryDto> ByPrioritySummary { get; set; } = new();
    public List<DashboardSummaryDto> ByLocationSummary { get; set; } = new();
    public List<DashboardSummaryDto> ByDepartmentSummary { get; set; } = new();
    public List<MyRecentRequestItemVM> RecentRequests { get; set; } = new();
}

public class MyRecentRequestItemVM
{
    public int RequestId { get; set; }
    public string RequestNumber { get; set; } = "";
    public string DisplayRequestNumber => Helpers.RequestNumberHelper.Format(RequestNumber);
    public string Title { get; set; } = "";
    public string RequestTypeName { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public string? CurrentApproverName { get; set; }
    public int? CurrentStepLevel { get; set; }
}

// Carrier for the six "My Approvals" KPI counts, produced by
// DashboardService.GetMyApprovalSummaryAsync so the KPI cards, summary tables
// and queue all draw from one calculation.
public class MyApprovalSummaryVM
{
    public int AssignedTotal { get; set; }
    public int PendingMyAction { get; set; }
    public int ApprovedByMe { get; set; }
    public int RejectedByMe { get; set; }
    public int ClarificationByMe { get; set; }
}

public class MyApprovalsTabVM
{
    public int AssignedTotal { get; set; }
    public int PendingMyAction { get; set; }
    public int ApprovedByMe { get; set; }
    public int RejectedByMe { get; set; }
    public int ClarificationByMe { get; set; }
    public int Escalated { get; set; }
    public List<DashboardSummaryDto> ByTypeSummary { get; set; } = new();
    public List<DashboardSummaryDto> ByPrioritySummary { get; set; } = new();
    public List<DashboardSummaryDto> ByLocationSummary { get; set; } = new();
    public List<DashboardSummaryDto> ByDepartmentSummary { get; set; } = new();
    public List<ApprovalQueueItemVM> Queue { get; set; } = new();
}

public class ApprovalQueueItemVM
{
    public int RequestId { get; set; }
    public string RequestNumber { get; set; } = "";
    public string DisplayRequestNumber => Helpers.RequestNumberHelper.Format(RequestNumber);
    public string RequestTypeName { get; set; } = "";
    public string RequestorName { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTimeOffset? SubmittedAt { get; set; }
    public int PendingDays { get; set; }
}

public class MyTerritoryTabVM
{
    public int TotalInTerritory { get; set; }
    public int Pending { get; set; }
    public int Approved { get; set; }
    public int Rejected { get; set; }
    public int Clarification { get; set; }
    public Dictionary<string, int> ByStatus { get; set; } = new();
    public List<DashboardSummaryDto> ByTypeSummary { get; set; } = new();
    public List<DashboardSummaryDto> ByPrioritySummary { get; set; } = new();
    public List<DashboardSummaryDto> ByLocationSummary { get; set; } = new();
    public List<DashboardSummaryDto> ByDepartmentSummary { get; set; } = new();
}
