namespace ApproveFlow.MVC.Models.ViewModels;

// Backing model for the mobile Pending Approvals page — the landing page for approvers who
// tap the link in the WhatsApp Pending Approval Reminder.
public class MobilePendingVM
{
    public string ApproverName { get; set; } = "";

    // The approver's UNFILTERED pending total — the same figure the WhatsApp reminder quotes
    // (both come from IPendingApprovalSummaryService), so the message and the page it opens
    // can never disagree, even when a chip narrows the list below it.
    public int TotalPending { get; set; }

    public string Chip { get; set; } = MobileChip.Pending;
    public string? SearchTerm { get; set; }

    // The card feed itself, produced by the EXISTING IRequestService.GetMyApprovalsAsync.
    public RequestListVM List { get; set; } = new();
}

// Filter chips for the mobile page. Every chip maps onto fields that already exist on
// RequestListVM, so the page adds no new query path and cannot drift from the desktop grid.
public static class MobileChip
{
    public const string Pending = "pending";
    public const string High    = "high";
    public const string Today   = "today";
    public const string Week    = "week";
    public const string All     = "all";

    // Key → label, in display order. Drives the pill row in the view.
    public static readonly (string Key, string Label)[] Items =
    {
        (Pending, "Pending"),
        (High,    "High Priority"),
        (Today,   "Today"),
        (Week,    "This Week"),
        (All,     "All"),
    };
}
