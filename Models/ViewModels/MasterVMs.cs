namespace ApproveFlow.MVC.Models.ViewModels;

public class MasterFormVM
{
    public int Id { get; set; }
    [Required, MaxLength(20)] public string Code { get; set; } = "";
    [Required, MaxLength(200)] public string Name { get; set; } = "";
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public string EntityType { get; set; } = "";
}
