using ApproveFlow.MVC.Data;
using ApproveFlow.MVC.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
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
        o.ExpireTimeSpan = TimeSpan.FromHours(8);
        o.SlidingExpiration = true;
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Lax;
    });

builder.Services.AddAuthorization();

builder.Services.AddControllersWithViews()
    .AddRazorRuntimeCompilation();

builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<IRequestService, RequestService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<INotifService, NotifService>();
builder.Services.AddScoped<IUserMappingService, UserMappingService>();

// WhatsApp integration
builder.Services.AddHttpClient("WhatsApp");
builder.Services.AddScoped<IWhatsAppService, WhatsAppService>();
builder.Services.AddHostedService<AgingReminderBackgroundService>();

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

app.MapControllerRoute("default", "{controller=Dashboard}/{action=Index}/{id?}");

// Redirect root to dashboard
app.MapGet("/", () => Results.Redirect("/Dashboard"));

app.Run();