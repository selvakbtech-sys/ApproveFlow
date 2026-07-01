namespace ApproveFlow.MVC.Models.Domain;

public class UserRequestTypeMapping
{
    [Key]
    public int Id { get; set; }
    public int UserId { get; set; }
    public int RequestTypeId { get; set; }
    public bool IsActive { get; set; } = true;
    public bool SoftDeleted { get; set; }
    public int CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public int? ModifiedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int? DeletedBy { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public virtual User User { get; set; } = null!;
    public virtual RequestType RequestType { get; set; } = null!;
}
