namespace ApproveFlow.MVC.Models.Domain;

public class RequestType
{
    [Key]
    public int RequestTypeId { get; set; }
    public string Code { get; set; } = "";
    public string TypeName { get; set; } = "";
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
