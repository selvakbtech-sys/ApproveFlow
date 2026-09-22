namespace ApproveFlow.MVC.Models.Domain;

// Company master — introduced with the DWO module. Mirrors Location/Department exactly
// so the generic Masters screen can manage it with no special-casing. Nothing in the
// existing Request/Approval flow references it.
public class Company
{
    [Key]
    public int CompanyId { get; set; }
    public string Code { get; set; } = "";
    public string CompanyName { get; set; } = "";
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
