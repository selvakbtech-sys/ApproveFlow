namespace ApproveFlow.MVC.Services;

public interface IAuthService
{
    Task<User?> ValidateAsync(string email, string password);
    Task<bool> ChangePasswordAsync(int userId, string current, string next);
    Task ResetPasswordAsync(int userId, string newPw);
}

public class AuthService : IAuthService
{
    private readonly AppDbContext _db;
    public AuthService(AppDbContext db) { _db = db; }

    public async Task<User?> ValidateAsync(string email, string password)
    {
        var normalizedEmail = email.Trim().ToLower();

        var user = await _db.Users
            .AsSplitQuery()
            .Include(u => u.Role)
                .ThenInclude(r => r.Permissions)
            .Include(u => u.UserLocations)
                .ThenInclude(ul => ul.Location)
            .Include(u => u.UserDepartments)
                .ThenInclude(ud => ud.Department)
            .FirstOrDefaultAsync(u =>
                u.Email.Trim().ToLower() == normalizedEmail &&
                u.Status.Trim().ToLower() == "active");

        if (user == null) return null;

        if (!BCrypt.Net.BCrypt.Verify(password, user.PasswordHash)) return null;

        user.LastLoginAt = IstClock.Now;
        await _db.SaveChangesAsync();
        return user;
    }

    public async Task<bool> ChangePasswordAsync(int userId, string current, string next)
    {
        var u = await _db.Users.FindAsync(userId);
        if (u == null || !BCrypt.Net.BCrypt.Verify(current, u.PasswordHash)) return false;
        u.PasswordHash = BCrypt.Net.BCrypt.HashPassword(next, 12);
        u.MustChangePassword = false;
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task ResetPasswordAsync(int userId, string newPw)
    {
        var u = await _db.Users.FindAsync(userId)
            ?? throw new KeyNotFoundException("User not found.");
        u.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPw, 12);
        u.MustChangePassword = true;
        await _db.SaveChangesAsync();
    }
}
