namespace ApproveFlow.MVC.Models.Domain;

public class UserDepartment
{
    [Key]
    public int UserDepartmentId { get; set; }
    public int UserId { get; set; }
    public int DepartmentId { get; set; }
    public virtual User User { get; set; } = null!;
    public virtual Department Department { get; set; } = null!;
}
