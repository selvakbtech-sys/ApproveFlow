namespace ApproveFlow.MVC.Models.Domain;

public class RequestViewToken
{
    [Key]
    public int Id { get; set; }
    public int RequestId { get; set; }
    public string Token { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public virtual Request Request { get; set; } = null!;
}
