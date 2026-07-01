namespace ApproveFlow.MVC.Models.ViewModels;

public class NotifListVM
{
    public List<NotifItemVM> Items { get; set; } = new();
    public int UnreadCount { get; set; }
    public int AllCount { get; set; }
    public string Tab { get; set; } = "all";
}

public class NotifItemVM
{
    public int NotificationId { get; set; }
    public int? RequestId { get; set; }
    public string RequestNumber { get; set; } = "";
    public string DisplayRequestNumber => Helpers.RequestNumberHelper.Format(RequestNumber);
    public string NotificationType { get; set; } = "";
    public string Message { get; set; } = "";
    public bool IsRead { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
