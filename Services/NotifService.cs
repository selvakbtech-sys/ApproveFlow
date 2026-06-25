namespace ApproveFlow.MVC.Services;

public interface INotifService
{
    Task<NotifListVM> GetAsync(int userId, string tab);
    Task<int> CountUnreadAsync(int userId);
    Task MarkReadAsync(int id, int userId);
    Task MarkAllReadAsync(int userId);
}

public class NotifService : INotifService
{
    private readonly AppDbContext _db;
    public NotifService(AppDbContext db) { _db = db; }

    public async Task<NotifListVM> GetAsync(int userId, string tab)
    {
        var q = _db.Notifications.Include(n => n.Request).Where(n => n.UserId == userId);
        if (tab == "unread") q = q.Where(n => !n.IsRead);
        if (tab == "pending") q = q.Where(n => n.NotificationType == "action_required");
        var items = await q.OrderByDescending(n => n.CreatedAt).Take(50)
            .Select(n => new NotifItemVM
            {
                NotificationId = n.NotificationId,
                RequestId = n.RequestId,
                RequestNumber = n.Request != null ? n.Request.RequestNumber : "",
                NotificationType = n.NotificationType,
                Message = n.Message,
                IsRead = n.IsRead,
                CreatedAt = n.CreatedAt,
            }).ToListAsync();
        var allCount = await _db.Notifications.CountAsync(n => n.UserId == userId);
        return new NotifListVM { Items = items, Tab = tab, UnreadCount = await CountUnreadAsync(userId), AllCount = allCount };
    }

    public Task<int> CountUnreadAsync(int userId) =>
        _db.Notifications.CountAsync(n => n.UserId == userId && !n.IsRead);

    public async Task MarkReadAsync(int id, int userId)
    {
        var n = await _db.Notifications.FirstOrDefaultAsync(x => x.NotificationId == id && x.UserId == userId);
        if (n != null) { n.IsRead = true; n.ReadAt = IstClock.Now; await _db.SaveChangesAsync(); }
    }

    public async Task MarkAllReadAsync(int userId)
    {
        await _db.Notifications.Where(n => n.UserId == userId && !n.IsRead)
            .ExecuteUpdateAsync(s => s
                .SetProperty(n => n.IsRead, true)
                .SetProperty(n => n.ReadAt, IstClock.Now));
    }
}
