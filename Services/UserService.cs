namespace ApproveFlow.MVC.Services;

public interface IUserService
{
    Task<UserListVM> GetListAsync(string? role, string? status);
    Task<UserFormVM> BuildFormAsync(int? id);
    Task<(bool ok, string? err)> SaveAsync(UserFormVM vm);
    Task<(bool ok, string? err)> ToggleStatusAsync(int id, string newStatus);
    Task<(bool ok, string? err)> DeleteAsync(int id);
}

public class UserService : IUserService
{
    private readonly AppDbContext _db;
    public UserService(AppDbContext db) { _db = db; }

    public async Task<UserListVM> GetListAsync(string? role, string? status)
    {
        var q = _db.Users
            .AsNoTracking()
            .Include(u => u.Role)
            .Include(u => u.UserLocations).ThenInclude(ul => ul.Location)
            .Include(u => u.UserDepartments).ThenInclude(ud => ud.Department)
            .AsQueryable();
        if (!string.IsNullOrEmpty(role)) q = q.Where(u => u.Role.RoleName == role);
        if (!string.IsNullOrEmpty(status)) q = q.Where(u => u.Status == status);
        var users = await q.OrderBy(u => u.FullName).ToListAsync();
        return new UserListVM
        {
            FilterRole = role,
            FilterStatus = status,
            Roles = await _db.Roles.Where(r => r.IsActive).OrderBy(r => r.RoleName).ToListAsync(),
            Items = users.Select(u => new UserItemVM
            {
                UserId = u.UserId,
                FullName = u.FullName,
                Email = u.Email,
                RoleName = u.Role.RoleName,
                BadgeColor = u.Role.BadgeColor,
                Locations = u.UserLocations.Select(ul => ul.Location.LocationName).ToList(),
                Departments = u.UserDepartments.Select(ud => ud.Department.DeptName).ToList(),
                Status = u.Status,
                AvatarInitials = u.AvatarInitials,
                CreatedAt = u.CreatedAt,
                UpdatedAt = u.UpdatedAt,
            }).ToList()
        };
    }

    public async Task<UserFormVM> BuildFormAsync(int? id)
    {
        var vm = new UserFormVM
        {
            Roles = await _db.Roles.Where(r => r.IsActive).OrderBy(r => r.RoleName).ToListAsync(),
            Locations = await _db.Locations.Where(l => l.IsActive).OrderBy(l => l.LocationName).ToListAsync(),
            Departments = await _db.Departments.Where(d => d.IsActive).OrderBy(d => d.DeptName).ToListAsync(),
        };
        if (id is null or 0) return vm;
        var u = await _db.Users
            .Include(u => u.UserLocations).Include(u => u.UserDepartments)
            .FirstOrDefaultAsync(u => u.UserId == id);
        if (u == null) return vm;
        vm.UserId = u.UserId;
        vm.FullName = u.FullName;
        vm.Email = u.Email;
        vm.RoleId = u.RoleId;
        vm.WhatsAppNumber = u.WhatsAppNumber;
        vm.LocationIds = u.UserLocations.Select(l => l.LocationId).ToList();
        vm.DepartmentIds = u.UserDepartments.Select(d => d.DepartmentId).ToList();
        return vm;
    }

    public async Task<(bool ok, string? err)> SaveAsync(UserFormVM vm)
    {
        if (vm.UserId == 0)
        {
            if (string.IsNullOrEmpty(vm.Password)) return (false, "Password is required.");
            if (await _db.Users.AnyAsync(u => u.Email == vm.Email)) return (false, "Email already in use.");
            var u = new User
            {
                FullName = vm.FullName,
                Email = vm.Email,
                RoleId = vm.RoleId,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(vm.Password, 12),
                AvatarInitials = string.Concat(vm.FullName.Split(' ').Take(2).Select(n => n[0])).ToUpper(),
                MustChangePassword = true,
                WhatsAppNumber = vm.WhatsAppNumber,
            };
            _db.Users.Add(u);
            await _db.SaveChangesAsync();
            foreach (var lid in vm.LocationIds)
                _db.UserLocations.Add(new UserLocation { UserId = u.UserId, LocationId = lid });
            foreach (var did in vm.DepartmentIds)
                _db.UserDepartments.Add(new UserDepartment { UserId = u.UserId, DepartmentId = did });
        }
        else
        {
            var u = await _db.Users
                .Include(u => u.UserLocations).Include(u => u.UserDepartments)
                .FirstOrDefaultAsync(u => u.UserId == vm.UserId);
            if (u == null) return (false, "User not found.");
            u.FullName = vm.FullName; u.Email = vm.Email; u.RoleId = vm.RoleId;
            u.WhatsAppNumber = vm.WhatsAppNumber;
            if (!string.IsNullOrEmpty(vm.Password))
                u.PasswordHash = BCrypt.Net.BCrypt.HashPassword(vm.Password, 12);
            _db.UserLocations.RemoveRange(u.UserLocations);
            _db.UserDepartments.RemoveRange(u.UserDepartments);
            foreach (var lid in vm.LocationIds)
                _db.UserLocations.Add(new UserLocation { UserId = u.UserId, LocationId = lid });
            foreach (var did in vm.DepartmentIds)
                _db.UserDepartments.Add(new UserDepartment { UserId = u.UserId, DepartmentId = did });
        }
        await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool ok, string? err)> ToggleStatusAsync(int id, string newStatus)
    {
        var u = await _db.Users.FindAsync(id);
        if (u == null) return (false, "Not found.");
        u.Status = newStatus; await _db.SaveChangesAsync();
        return (true, null);
    }

    public async Task<(bool ok, string? err)> DeleteAsync(int id)
    {
        try { await _db.Database.ExecuteSqlRawAsync("EXEC dbo.sp_DeleteUser @UserId={0}", id); return (true, null); }
        catch (Exception ex) { return (false, ex.Message); }
    }
}
