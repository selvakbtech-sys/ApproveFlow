namespace ApproveFlow.MVC.Controllers;

public class NotificationsController : BaseCtrl
{
    private readonly INotifService _svc;
    private readonly IRequestService _reqSvc;
    private readonly AppDbContext _db;
    public NotificationsController(INotifService svc, IRequestService reqSvc, AppDbContext db)
    { _svc = svc; _reqSvc = reqSvc; _db = db; }

    public async Task<IActionResult> Index(string tab = "all")
        => View(await _svc.GetAsync(UserId, tab));

    // Open the request a notification points to. Authorization is checked FIRST — an
    // unauthorized user gets the AccessDenied page and the notification is left UNREAD
    // (so it isn't silently consumed). Only after a successful, authorized navigation is
    // the notification marked read. Works for every notification type (approval, rejection,
    // clarification, resubmission, revoked, etc.) and every role, because it defers to the
    // same CanAccessRequestAsync gate the Detail page uses. RequestId never changes across
    // status transitions, so the link keeps working after Approve/Reject/Clarify/Revoke.
    public async Task<IActionResult> Open(int id)
    {
        var n = await _db.Notifications
            .FirstOrDefaultAsync(x => x.NotificationId == id && x.UserId == UserId);
        if (n == null) { Err("Notification not found."); return RedirectToAction("Index"); }

        if (n.RequestId.HasValue)
        {
            // Not authorized to view this request → show Access Denied, keep it unread.
            if (!await _reqSvc.CanAccessRequestAsync(n.RequestId.Value, UserId, UserRole))
                return Deny();

            await _svc.MarkReadAsync(id, UserId);
            return RedirectToAction("Detail", "Requests", new { id = n.RequestId.Value });
        }

        // Notification with no linked request (rare) — just mark it read.
        await _svc.MarkReadAsync(id, UserId);
        return RedirectToAction("Index");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkRead(int id)
    { await _svc.MarkReadAsync(id, UserId); return RedirectToAction("Index"); }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkAll()
    { await _svc.MarkAllReadAsync(UserId); return RedirectToAction("Index"); }

    [HttpGet]
    public async Task<IActionResult> Count()
        => Json(new { count = await _svc.CountUnreadAsync(UserId) });
}
