namespace ApproveFlow.MVC.Models.Domain;

public class Department
{
    [Key]
    public int DepartmentId { get; set; }
    public string Code { get; set; } = "";
    public string DeptName { get; set; } = "";
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public virtual ICollection<UserDepartment> UserDepartments { get; set; } = new List<UserDepartment>();
}
