namespace ApproveFlow.MVC.Controllers;

public class NotificationsController : BaseCtrl
{
    private readonly INotifService _svc;
    public NotificationsController(INotifService svc) { _svc = svc; }

    public async Task<IActionResult> Index(string tab = "all")
        => View(await _svc.GetAsync(UserId, tab));

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
