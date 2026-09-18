namespace ApproveFlow.MVC.Models;

// Central definition of the DWO discussion-point statuses. These nine names are fixed by
// the approved prototype and must not be renamed — the register chips, the dashboard KPIs
// and the workflow guards all key off them.
public static class DwoStatus
{
    public const string Draft                = "Draft";
    public const string Submitted            = "Submitted";
    public const string ChangeRequested      = "Change Requested";
    public const string Rejected             = "Rejected";
    public const string ManagementDiscussion = "Management Discussion";
    public const string FollowUp             = "Follow-up";
    public const string Hold                 = "Hold";
    public const string Reassigned           = "Reassigned";
    public const string Closed               = "Closed";

    // Register chip order (every status, for Team Member / HOD).
    public static readonly string[] All =
    {
        Draft, Submitted, ChangeRequested, Rejected,
        ManagementDiscussion, FollowUp, Hold, Reassigned, Closed
    };

    // Display-only label for a Submitted point sitting on a Draft Set. The stored status
    // stays Submitted (every workflow guard keys off it); only what the user sees changes.
    public const string SetDraft = "Set Draft";
    public static string Display(string status, bool onDraftSet) =>
        status == Submitted && onDraftSet ? SetDraft : status;

    // Badge class in site.css for each status (reuses the existing .badge-* palette).
    public static string Badge(string status) => status switch
    {
        Draft                => "badge-gray",
        SetDraft             => "badge-purple",
        Submitted            => "badge-blue",
        ChangeRequested      => "badge-amber",
        Rejected             => "badge-red",
        ManagementDiscussion => "badge-sky",
        FollowUp             => "badge-amber",
        Hold                 => "badge-gray",
        Reassigned           => "badge-purple",
        Closed               => "badge-green",
        _                    => "badge-gray",
    };
}

// Lifecycle of a Set. A Draft is the HOD's work-in-progress bundle: its points keep their
// Submitted status and Management never sees it. Submitted is the live Set.
public static class DwoSetStatus
{
    public const string Draft     = "Draft";
    public const string Submitted = "Submitted";
}

// Appointment state of a Set once the HOD has submitted it to Management.
public static class DwoAppointmentStatus
{
    public const string Requested = "Requested";
    public const string Scheduled = "Scheduled";
    public const string Postponed = "Postponed";

    public static string Badge(string status) => status switch
    {
        Scheduled => "badge-green",
        Postponed => "badge-amber",
        _         => "badge-gray",
    };
}

// Which slot an uploaded file occupies. One table serves every DWO attachment point so
// the storage/download plumbing is written once.
public static class DwoAttachmentKind
{
    public const string Point               = "Point";               // attached when the point was raised/edited
    public const string SetSupporting       = "SetSupporting";       // HOD's supporting pack for a Set
    public const string SetSummary          = "SetSummary";          // single common summary file for a Set
    public const string Action              = "Action";              // attached by an HOD/Management action
    public const string FollowUpCompletion  = "FollowUpCompletion";  // proof uploaded when a follow-up is completed
}

// Discussion categories and priorities. Fixed lists per the approved prototype — there is
// no master screen for them, so this is the single place to change them.
public static class DwoCategory
{
    public static readonly string[] All =
        { "General", "Policy", "Budget", "Compliance", "Vendor", "System", "Finance" };
}

public static class DwoPriority
{
    public const string Low    = "Low";
    public const string Medium = "Medium";
    public const string High   = "High";

    public static readonly string[] All = { Low, Medium, High };

    public static string Badge(string priority) => priority switch
    {
        High   => "badge-red",
        Medium => "badge-amber",
        _      => "badge-gray",
    };
}

// The permission screens the DWO module registers in Roles & Permissions. Membership of a
// DWO "role" is derived from these rather than from a role name, so renaming a role never
// breaks the workflow:
//   Register|View     → DWO Dashboard, My Discussions, Discussion Register
//   Register|Approve  → HOD powers (review, send back, reject, build a Set)
// Management powers (close, hold, follow-up, reassign, appointments) are not a permission
// row: a user is Management when they are Admin or sit at the highest approval level of an
// active Workflow Definition (DwoService.ManagementFromWorkflowsAsync).
public static class DwoScreens
{
    public const string Register   = "DWO Register";
    // Requestor-side screens: My Discussions + raise/edit/submit/withdraw a point.
    public const string Points     = "Discussion Point";
    // Sets list / Set modal / summary document.
    public const string Sets       = "Sets";
    public const string Reports    = "DWO Reports";
    public const string Companies  = "Companies";
}
