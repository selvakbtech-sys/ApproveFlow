namespace ApproveFlow.MVC.Helpers;

// Single definition of the claims an authenticated session carries.
//
// Extracted from AuthController.Login so that the same set can be re-issued WITHOUT a
// fresh sign-in — needed since the permission matrix became binding for every role: an
// administrator who edits their own role has to see the effect immediately rather than
// after signing out and back in.
//
// "perm"  = screen-level View; drives menu visibility and page access.
// "permx" = granular per-action rights, "<Screen>|<Action>"; enforced on controller actions.
// Both are read back in BaseCtrl.HasPerm.
public static class UserClaimsFactory
{
    // The user must be loaded with Role.Permissions; a user whose permissions were not
    // included would silently produce a session with no rights at all.
    public static List<Claim> Build(User user)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.UserId.ToString()),
            new("Username",                user.Username),
            new(ClaimTypes.Name,           user.FullName),
            new(ClaimTypes.Role,           user.Role.RoleName),
            new("Avatar", user.AvatarInitials ?? user.FullName[..1].ToUpper()),
        };
        // Email is optional; only add the claim when present.
        if (!string.IsNullOrWhiteSpace(user.Email))
            claims.Add(new Claim(ClaimTypes.Email, user.Email));

        if (user.Role?.Permissions != null)
            foreach (var p in user.Role.Permissions)
            {
                if (p.CanView)    { claims.Add(new Claim("perm", p.ScreenName));
                                    claims.Add(new Claim("permx", $"{p.ScreenName}|View")); }
                if (p.CanCreate)  claims.Add(new Claim("permx", $"{p.ScreenName}|Create"));
                if (p.CanEdit)    claims.Add(new Claim("permx", $"{p.ScreenName}|Edit"));
                if (p.CanDelete)  claims.Add(new Claim("permx", $"{p.ScreenName}|Delete"));
                if (p.CanApprove) claims.Add(new Claim("permx", $"{p.ScreenName}|Approve"));
            }
        return claims;
    }

    public static ClaimsPrincipal BuildPrincipal(User user) =>
        new(new ClaimsIdentity(Build(user), CookieAuthenticationDefaults.AuthenticationScheme));
}
