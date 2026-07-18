namespace ApproveFlow.MVC.Services;

public interface IUserMappingService
{
    Task<UserMappingListVM> GetListAsync(string? search, string? status);
    Task<UserMappingFormVM> BuildFormAsync(int? userId);
    Task<(bool ok, string? err)> SaveAsync(UserMappingFormVM vm, int createdBy);
    Task<(bool ok, string? err)> DeleteAsync(int userId, int deletedBy);
    Task<(bool ok, string? err)> ToggleStatusAsync(int userId, bool isActive);
    Task<List<RequestType>> GetAllowedRequestTypesAsync(int userId);
    Task<List<RequestType>> GetMappedRequestTypesAsync(int userId);
    Task<bool> IsRequestTypeAllowedAsync(int userId, int requestTypeId);
    Task<bool> UserMappingExistsAsync(int userId);
    Task<int?> GetMappedUserIdAsync(int userId);  // returns userId if mapping exists (for edit URL)
}

public class UserMappingService : IUserMappingService
{
    private readonly AppDbContext _db;
    private readonly ILogger<UserMappingService> _log;

    public UserMappingService(AppDbContext db, ILogger<UserMappingService> log)
    { _db = db; _log = log; }

    // Returns one row per user (grouped), excluding soft-deleted entries.
    public async Task<UserMappingListVM> GetListAsync(string? search, string? status)
    {
        var q = _db.UserRequestTypeMappings
            .AsNoTracking()
            .Where(m => !m.SoftDeleted)
            .Include(m => m.User)
            .Include(m => m.RequestType)
            .AsQueryable();

        var rows = await q.ToListAsync();

        // Resolve creator names from a single query
        var creatorIds = rows.Select(m => m.CreatedBy).Distinct().ToList();
        var modifierIds = rows.Where(m => m.ModifiedBy.HasValue).Select(m => m.ModifiedBy!.Value).Distinct().ToList();
        var allIds = creatorIds.Union(modifierIds).ToList();
        var nameMap = await _db.Users.AsNoTracking()
            .Where(u => allIds.Contains(u.UserId))
            .ToDictionaryAsync(u => u.UserId, u => u.FullName);

        // Group by user
        var grouped = rows
            .GroupBy(m => m.UserId)
            .Select(g =>
            {
                var first = g.OrderBy(m => m.CreatedAt).First();
                var last  = g.OrderByDescending(m => m.UpdatedAt).First();
                bool allActive = g.All(m => m.IsActive);
                return new UserMappingItemVM
                {
                    UserId           = g.Key,
                    UserName         = first.User.FullName,
                    UserEmail        = first.User.Email,
                    AvatarInitials   = first.User.AvatarInitials,
                    RequestTypeNames = g.OrderBy(m => m.RequestType.TypeName).Select(m => m.RequestType.TypeName).ToList(),
                    IsActive         = allActive,
                    SoftDeleted      = false,
                    CreatedByName    = nameMap.GetValueOrDefault(first.CreatedBy, ""),
                    CreatedAt        = first.CreatedAt,
                    ModifiedByName   = last.ModifiedBy.HasValue ? nameMap.GetValueOrDefault(last.ModifiedBy.Value) : null,
                    UpdatedAt        = last.UpdatedAt,
                };
            })
            .AsQueryable();

        // Apply search filter
        if (!string.IsNullOrWhiteSpace(search))
            grouped = grouped.Where(x => x.UserName.Contains(search, StringComparison.OrdinalIgnoreCase));

        // Apply status filter
        if (!string.IsNullOrWhiteSpace(status))
        {
            bool active = status == "active";
            grouped = grouped.Where(x => x.IsActive == active);
        }

        return new UserMappingListVM
        {
            Items        = grouped.OrderBy(x => x.UserName).ToList(),
            SearchTerm   = search,
            FilterStatus = status,
        };
    }

    // Build form for create (userId=null) or edit/view (userId>0).
    public async Task<UserMappingFormVM> BuildFormAsync(int? userId)
    {
        // Users who are active and not yet mapped (for create dropdown)
        // For edit, we also include the current user even if they already have a mapping.
        var allActiveUsers = await _db.Users
            .AsNoTracking()
            .Where(u => u.Status == "active")
            .OrderBy(u => u.FullName)
            .ToListAsync();

        var allRequestTypes = await _db.RequestTypes
            .AsNoTracking()
            .Where(t => t.IsActive)
            .OrderBy(t => t.TypeName)
            .ToListAsync();

        var vm = new UserMappingFormVM
        {
            RequestTypes = allRequestTypes,
            Users        = allActiveUsers.Select(u => new SelectListItem
            {
                Value = u.UserId.ToString(),
                Text  = $"{u.FullName} ({u.Email})",
            }).ToList(),
        };

        if (userId is null or 0) return vm;

        // Load existing mappings for this user
        var existing = await _db.UserRequestTypeMappings
            .AsNoTracking()
            .Where(m => m.UserId == userId && !m.SoftDeleted)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync();

        if (!existing.Any()) return vm;

        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == userId);

        var creatorIds = existing.Select(m => m.CreatedBy).Distinct()
            .Union(existing.Where(m => m.ModifiedBy.HasValue).Select(m => m.ModifiedBy!.Value))
            .ToList();
        var nameMap = await _db.Users.AsNoTracking()
            .Where(u => creatorIds.Contains(u.UserId))
            .ToDictionaryAsync(u => u.UserId, u => u.FullName);

        var first = existing.First();
        var last  = existing.OrderByDescending(m => m.UpdatedAt).First();

        vm.UserId               = userId.Value;
        vm.UserName             = user?.FullName;
        vm.SelectedRequestTypeIds = existing.Select(m => m.RequestTypeId).ToList();
        vm.IsActive             = existing.All(m => m.IsActive);
        vm.CreatedByName        = nameMap.GetValueOrDefault(first.CreatedBy, "");
        vm.CreatedAt            = first.CreatedAt;
        vm.ModifiedByName       = last.ModifiedBy.HasValue ? nameMap.GetValueOrDefault(last.ModifiedBy.Value) : null;
        vm.UpdatedAt            = last.UpdatedAt;

        return vm;
    }

    // Save mapping: begin transaction, delete old rows for user, insert new rows, commit.
    public async Task<(bool ok, string? err)> SaveAsync(UserMappingFormVM vm, int createdBy)
    {
        if (vm.UserId <= 0)
            return (false, "Please select a user.");

        // Server-side duplicate guard: block create if a mapping already exists for this user
        if (vm.IsCreate && await _db.UserRequestTypeMappings
                .AnyAsync(m => m.UserId == vm.UserId && !m.SoftDeleted))
        {
            var user = await _db.Users.AsNoTracking()
                .Where(u => u.UserId == vm.UserId)
                .Select(u => u.FullName)
                .FirstOrDefaultAsync() ?? "This user";
            return (false, $"\"{user}\" already has a Request Type Mapping. Please use the Edit option to modify the existing mapping.");
        }

        // Deduplicate — prevents count mismatch if hidden inputs are accidentally doubled
        vm.SelectedRequestTypeIds = vm.SelectedRequestTypeIds.Distinct().ToList();

        if (!vm.SelectedRequestTypeIds.Any())
            return (false, "Please select at least one request type.");

        // Verify user exists
        if (!await _db.Users.AnyAsync(u => u.UserId == vm.UserId))
            return (false, "Selected user not found.");

        // Verify all request type IDs are valid
        var validTypeIds = await _db.RequestTypes
            .Where(t => t.IsActive && vm.SelectedRequestTypeIds.Contains(t.RequestTypeId))
            .Select(t => t.RequestTypeId)
            .ToListAsync();
        if (validTypeIds.Count != vm.SelectedRequestTypeIds.Count)
            return (false, "One or more selected request types are invalid.");

        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                bool isEdit = await _db.UserRequestTypeMappings
                    .AnyAsync(m => m.UserId == vm.UserId && !m.SoftDeleted);

                int? modifiedBy = isEdit ? createdBy : (int?)null;

                // Remove existing non-deleted rows for this user
                var existing = await _db.UserRequestTypeMappings
                    .Where(m => m.UserId == vm.UserId && !m.SoftDeleted)
                    .ToListAsync();
                _db.UserRequestTypeMappings.RemoveRange(existing);

                // Insert new rows
                foreach (var typeId in vm.SelectedRequestTypeIds)
                {
                    _db.UserRequestTypeMappings.Add(new UserRequestTypeMapping
                    {
                        UserId        = vm.UserId,
                        RequestTypeId = typeId,
                        IsActive      = vm.IsActive,
                        SoftDeleted   = false,
                        CreatedBy     = createdBy,
                        ModifiedBy    = modifiedBy,
                    });
                }

                await _db.SaveChangesAsync();
                await tx.CommitAsync();
                _log.LogInformation("UserMapping saved for UserId={UserId} by {CreatedBy}", vm.UserId, createdBy);
                return (true, (string?)null);
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                var inner = ex.InnerException?.Message ?? ex.Message;
                _log.LogError(ex, "UserMapping save failed for UserId={UserId}: {Error}", vm.UserId, inner);
                return (false, inner);
            }
        });
    }

    // Soft-delete all mappings for a user.
    public async Task<(bool ok, string? err)> DeleteAsync(int userId, int deletedBy)
    {
        var rows = await _db.UserRequestTypeMappings
            .Where(m => m.UserId == userId && !m.SoftDeleted)
            .ToListAsync();

        if (!rows.Any()) return (false, "No mapping found for this user.");

        var now = IstClock.Now;
        foreach (var r in rows)
        {
            r.SoftDeleted = true;
            r.DeletedBy   = deletedBy;
            r.DeletedAt   = now;
        }

        await _db.SaveChangesAsync();
        _log.LogInformation("UserMapping soft-deleted for UserId={UserId} by {DeletedBy}", userId, deletedBy);
        return (true, null);
    }

    // Activate or deactivate all mappings for a user.
    public async Task<(bool ok, string? err)> ToggleStatusAsync(int userId, bool isActive)
    {
        var rows = await _db.UserRequestTypeMappings
            .Where(m => m.UserId == userId && !m.SoftDeleted)
            .ToListAsync();

        if (!rows.Any()) return (false, "No mapping found for this user.");

        foreach (var r in rows) r.IsActive = isActive;
        await _db.SaveChangesAsync();
        return (true, null);
    }

    // Returns true if an active (non-soft-deleted) mapping exists for the given user.
    public async Task<bool> UserMappingExistsAsync(int userId) =>
        userId > 0 && await _db.UserRequestTypeMappings
            .AnyAsync(m => m.UserId == userId && !m.SoftDeleted);

    // Returns the userId (same value) when a mapping exists, so the controller
    // can build the Edit URL without an extra DB round-trip.
    public async Task<int?> GetMappedUserIdAsync(int userId) =>
        await UserMappingExistsAsync(userId) ? userId : null;

    // STRICT mapped request types for a user — used by the New Request / Edit form and
    // by server-side Save/Submit validation. Unlike GetAllowedRequestTypesAsync, this
    // returns an EMPTY list when the user has no active mapping (no backward-compatible
    // fallback), so an unmapped user cannot raise any request type.
    public async Task<List<RequestType>> GetMappedRequestTypesAsync(int userId)
    {
        if (userId <= 0) return new List<RequestType>();

        var mappedIds = await _db.UserRequestTypeMappings
            .AsNoTracking()
            .Where(m => m.UserId == userId && !m.SoftDeleted && m.IsActive)
            .Select(m => m.RequestTypeId)
            .ToListAsync();

        if (!mappedIds.Any()) return new List<RequestType>();

        return await _db.RequestTypes
            .AsNoTracking()
            .Where(t => t.IsActive && mappedIds.Contains(t.RequestTypeId))
            .OrderBy(t => t.TypeName)
            .ToListAsync();
    }

    // STRICT server-side check: the given request type must be an active type that is
    // actively mapped to the user. Never trust a posted RequestTypeId without this.
    public async Task<bool> IsRequestTypeAllowedAsync(int userId, int requestTypeId)
    {
        if (userId <= 0 || requestTypeId <= 0) return false;

        return await _db.UserRequestTypeMappings
            .AsNoTracking()
            .AnyAsync(m => m.UserId == userId
                        && m.RequestTypeId == requestTypeId
                        && !m.SoftDeleted && m.IsActive
                        && m.RequestType.IsActive);
    }

    // Returns the allowed request types for a user.
    // If the user has active mappings → return only mapped types.
    // If no mappings exist → return all active request types (backward-compatible).
    public async Task<List<RequestType>> GetAllowedRequestTypesAsync(int userId)
    {
        if (userId <= 0)
            return await _db.RequestTypes.AsNoTracking().Where(t => t.IsActive).OrderBy(t => t.TypeName).ToListAsync();

        var mappedIds = await _db.UserRequestTypeMappings
            .AsNoTracking()
            .Where(m => m.UserId == userId && !m.SoftDeleted && m.IsActive)
            .Select(m => m.RequestTypeId)
            .ToListAsync();

        if (!mappedIds.Any())
            return await _db.RequestTypes.AsNoTracking().Where(t => t.IsActive).OrderBy(t => t.TypeName).ToListAsync();

        return await _db.RequestTypes
            .AsNoTracking()
            .Where(t => t.IsActive && mappedIds.Contains(t.RequestTypeId))
            .OrderBy(t => t.TypeName)
            .ToListAsync();
    }
}
