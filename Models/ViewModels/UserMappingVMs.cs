namespace ApproveFlow.MVC.Models.ViewModels;

public class UserMappingListVM
{
    public List<UserMappingItemVM> Items { get; set; } = new();
    public string? FilterStatus { get; set; }
    public string? SearchTerm { get; set; }
}

public class UserMappingItemVM
{
    public int UserId { get; set; }
    public string UserName { get; set; } = "";
    public string UserEmail { get; set; } = "";
    public string? AvatarInitials { get; set; }
    public List<string> RequestTypeNames { get; set; } = new();
    public bool IsActive { get; set; }
    public bool SoftDeleted { get; set; }
    public string CreatedByName { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public string? ModifiedByName { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public class UserMappingFormVM
{
    // 0 = create mode, > 0 = edit/view mode (userId of the mapped user)
    public int UserId { get; set; }

    // True when opened from Create action; false when opened from Edit action.
    // Used server-side to block duplicate creation.
    public bool IsCreate { get; set; }

    public List<int> SelectedRequestTypeIds { get; set; } = new();
    public bool IsActive { get; set; } = true;

    // Dropdowns
    public List<SelectListItem> Users { get; set; } = new();
    public List<RequestType> RequestTypes { get; set; } = new();

    // Display-only (populated in edit/view)
    public string? UserName { get; set; }
    public string? CreatedByName { get; set; }
    public string? ModifiedByName { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}
