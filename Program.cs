using ApproveFlow.MVC.Data;
using ApproveFlow.MVC.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var cfg = builder.Configuration;

builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseSqlServer(cfg.GetConnectionString("DefaultConnection"),
        sql => { sql.CommandTimeout(30); sql.EnableRetryOnFailure(3); }));

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/login";
        o.LogoutPath = "/logout";
        o.AccessDeniedPath = "/login";
        // Idle-session timeout: the cookie is only valid for 5 minutes since the
        // last request. SlidingExpiration renews it on activity, so an expired
        // (idle) session can no longer reach secured pages and is bounced to Login.
        o.ExpireTimeSpan = TimeSpan.FromMinutes(5);
        o.SlidingExpiration = true;
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Lax;
        // For AJAX/API calls, return 401/403 instead of the 302-to-Login redirect
        // (which would hand back login HTML). The client redirects to Login on these.
        o.Events.OnRedirectToLogin = ctx =>
        {
            if (IsAjaxRequest(ctx.Request)) ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            else ctx.Response.Redirect(ctx.RedirectUri);
            return Task.CompletedTask;
        };
        o.Events.OnRedirectToAccessDenied = ctx =>
        {
            if (IsAjaxRequest(ctx.Request)) ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            else ctx.Response.Redirect(ctx.RedirectUri);
            return Task.CompletedTask;
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddHttpContextAccessor();

// ── Data Protection ───────────────────────────────────────
// Persist the key ring to a stable folder and pin a fixed application name so that
// antiforgery tokens AND the auth cookie survive app restarts / redeploys. Without
// this the default key store is often ephemeral on a hosted server, so every restart
// regenerates keys and invalidates every previously issued token — surfacing as
// "The antiforgery token could not be decrypted / key {…} was not found in the key
// ring". Keys land next to the app by default; point AppSettings:DataProtectionKeysPath
// at an absolute path outside the deploy folder (or a shared folder for multi-instance)
// to guarantee they persist across deployments.
//
// This is BEST-EFFORT: if the folder can't be created or written (e.g. the app-pool
// identity lacks Modify permission), we log and fall back to the framework default key
// store rather than letting a startup exception crash the whole site. Persistence — and
// therefore stable antiforgery/login across restarts — still requires a writable path,
// so grant the app-pool identity Modify on the configured folder.
var dpBuilder = builder.Services.AddDataProtection().SetApplicationName("ApproveFlow.MVC");
try
{
    var dpKeysConfigured = cfg["AppSettings:DataProtectionKeysPath"] ?? "dp-keys";
    var dpKeysPath = Path.IsPathRooted(dpKeysConfigured)
        ? dpKeysConfigured
        : Path.Combine(builder.Environment.ContentRootPath, dpKeysConfigured);
    Directory.CreateDirectory(dpKeysPath);
    // Probe write access up front so a read-only folder falls back cleanly instead of
    // failing later when the first key is generated.
    var probe = Path.Combine(dpKeysPath, ".write-test");
    File.WriteAllText(probe, "ok");
    File.Delete(probe);
    dpBuilder.PersistKeysToFileSystem(new DirectoryInfo(dpKeysPath));
    Console.WriteLine($"[DataProtection] Persisting keys to: {dpKeysPath}");
}
catch (Exception ex)
{
    Console.Error.WriteLine(
        "[DataProtection] Key persistence DISABLED — using the in-memory/default store. " +
        "Antiforgery tokens and logins will not survive an app restart until a writable " +
        $"keys folder is configured. Reason: {ex.Message}");
}

builder.Services.AddControllersWithViews()
    .AddRazorRuntimeCompilation();

builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<IRequestService, RequestService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<INotifService, NotifService>();
builder.Services.AddScoped<IUserMappingService, UserMappingService>();
builder.Services.AddScoped<IApplicationUrlService, ApplicationUrlService>();

// WhatsApp integration
builder.Services.AddHttpClient("WhatsApp");
builder.Services.AddScoped<IWhatsAppService, WhatsAppService>();
builder.Services.AddHostedService<AgingReminderBackgroundService>();
builder.Services.AddHostedService<WhatsAppPendingApproveTimeoutService>();

Directory.CreateDirectory(cfg["AppSettings:FileStoragePath"] ?? "uploads");

var app = builder.Build();

if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler("/Home/Error");

// Skip ngrok browser warning for all requests
app.Use(async (context, next) =>
{
    context.Response.Headers["ngrok-skip-browser-warning"] = "true";
    await next();
});

app.UseStaticFiles();

// WhatsApp Webhook routes - MUST be before Authentication
// so Meta can verify without login
app.MapControllerRoute("webhook-get", "WhatsApp/Webhook",
    new { controller = "Requests", action = "WebhookVerify" })
    .AllowAnonymous();

app.MapControllerRoute("webhook-post", "WhatsApp/Webhook",
    new { controller = "Requests", action = "WebhookReceive" })
    .AllowAnonymous();

app.UseAuthentication();
app.UseAuthorization();

// Stop the browser from caching authenticated pages, so the Back button cannot
// re-display a secured page after logout/timeout (it re-requests and hits Login).
app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        if (context.User?.Identity?.IsAuthenticated == true)
        {
            context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate, max-age=0";
            context.Response.Headers.Pragma = "no-cache";
            context.Response.Headers.Expires = "0";
        }
        return Task.CompletedTask;
    });
    await next();
});

app.MapControllerRoute("default", "{controller=Dashboard}/{action=Index}/{id?}");

// Redirect root to dashboard
app.MapGet("/", () => Results.Redirect("/Dashboard"));

app.Run();

// Treat a request as AJAX/API (rather than a page navigation) when it is an
// XMLHttpRequest or does not accept HTML. Such callers get 401/403 status codes
// instead of a redirect, letting the client handle re-login cleanly.
static bool IsAjaxRequest(HttpRequest request) =>
    request.Headers["X-Requested-With"] == "XMLHttpRequest" ||
    !request.Headers.Accept.Any(h => h != null && h.Contains("text/html"));