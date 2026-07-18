namespace ApproveFlow.MVC.Services;

public interface IApplicationUrlService
{
    string GetBaseUrl();
    string GetRequestViewUrl(string secureToken);
    string GetRequestViewLinksText(string secureToken);
    (string inOffice, string outOfOffice) GetRequestViewUrlPair(string secureToken);
}

// Centralized public URL builder for links sent in WhatsApp messages (Notification Alert,
// approval reminders, etc.). Resolution order:
//   1. Application:BaseUrl from configuration — the authoritative source. ASP.NET Core layers
//      appsettings.{Environment}.json over appsettings.json, so this automatically resolves to
//      the localhost URL in Development and the public IP in Production. No host is hardcoded in
//      code; switching environments needs no code change or redeploy — only the config differs.
//   2. NGROK_URL environment variable — optional local-tunnelling escape hatch, used only when
//      Application:BaseUrl is left blank.
//   3. The host that actually served the current request — but never localhost/127.0.0.1,
//      since a link like that is useless to an external WhatsApp recipient.
public class ApplicationUrlService : IApplicationUrlService
{
    private readonly IConfiguration _cfg;
    private readonly IHttpContextAccessor _http;
    private readonly ILogger<ApplicationUrlService> _log;

    public ApplicationUrlService(IConfiguration cfg, IHttpContextAccessor http, ILogger<ApplicationUrlService> log)
    { _cfg = cfg; _http = http; _log = log; }

    public string GetBaseUrl()
    {
        var configuredUrl = _cfg["Application:BaseUrl"];
        if (!string.IsNullOrWhiteSpace(configuredUrl))
        {
            _log.LogInformation("ApplicationUrlService: using Application:BaseUrl={Url}", configuredUrl);
            return configuredUrl.TrimEnd('/');
        }

        var ngrokUrl = Environment.GetEnvironmentVariable("NGROK_URL");
        if (!string.IsNullOrWhiteSpace(ngrokUrl))
        {
            _log.LogInformation("ApplicationUrlService: using NGROK_URL={Url}", ngrokUrl);
            return ngrokUrl.TrimEnd('/');
        }

        var req = _http.HttpContext?.Request;

        // Tunnels/proxies (ngrok included) forward the client's original hostname via
        // X-Forwarded-Host even when the underlying connection to Kestrel/IIS Express
        // itself is over "localhost" — prefer that over the raw Host header.
        var fwdHost = req?.Headers["X-Forwarded-Host"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(fwdHost) && !IsLocalHost(fwdHost.Split(':')[0]))
        {
            var fwdProto = req!.Headers["X-Forwarded-Proto"].FirstOrDefault();
            var resolved = $"{(string.IsNullOrWhiteSpace(fwdProto) ? req.Scheme : fwdProto)}://{fwdHost}";
            _log.LogInformation("ApplicationUrlService: using X-Forwarded-Host={Url}", resolved);
            return resolved;
        }

        var reqHost = req?.Host.Host;
        if (req != null && !IsLocalHost(req.Host.Host))
        {
            var resolved = $"{req.Scheme}://{req.Host}";
            _log.LogInformation("ApplicationUrlService: using request host={Url}", resolved);
            return resolved;
        }

        _log.LogWarning("ApplicationUrlService: No public base URL could be resolved (NGROK_URL={Ngrok}, Application:BaseUrl={Configured}, X-Forwarded-Host={FwdHost}, request host={ReqHost}, HttpContext present={HasCtx}). View Request links will be omitted.",
            ngrokUrl ?? "(not set)", configuredUrl ?? "(not set)", fwdHost ?? "(none)", reqHost ?? "(none)", _http.HttpContext != null);
        return "";
    }

    public string GetRequestViewUrl(string secureToken)
    {
        var baseUrl = GetBaseUrl();
        return string.IsNullOrEmpty(baseUrl) ? "" : $"{baseUrl}/r/{secureToken}";
    }

    // Builds the "View Request" block for WhatsApp. When both LAN and public base URLs are
    // configured (typical Production), it emits two clearly-labelled links so the recipient can
    // tap the one that matches their network — office/WhatsApp-Web users hit the LAN IP, mobile
    // users hit the public IP. When those keys are blank (e.g. Development), it falls back to the
    // single URL from GetRequestViewUrl. All hosts come from configuration — none are hardcoded.
    public string GetRequestViewLinksText(string secureToken)
    {
        var lan = _cfg["Application:LanBaseUrl"];
        var pub = _cfg["Application:PublicBaseUrl"];

        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(lan))
            lines.Add($"🏢 In-Office / HO (WhatsApp Web):\n{lan.TrimEnd('/')}/r/{secureToken}");
        if (!string.IsNullOrWhiteSpace(pub))
            lines.Add($"📱 Out of Office / Mobile:\n{pub.TrimEnd('/')}/r/{secureToken}");

        // No environment-specific links configured — fall back to single auto/config URL.
        return lines.Count > 0 ? string.Join("\n\n", lines) : GetRequestViewUrl(secureToken);
    }

    // The two view links as SEPARATE values, for the notification_alert Meta template
    // ({{8}} = In-Office / HO, {{9}} = Out of Office / Mobile). GetRequestViewLinksText
    // returns one newline-joined block, which cannot be a template parameter — Meta
    // rejects parameters containing newlines (#132018), and the sender collapses
    // whitespace, so the block would arrive as one unreadable line.
    //
    // Neither value may be empty either: Meta rejects a blank parameter and the whole
    // alert fails to send. So each side falls back to the auto-resolved URL, and finally
    // to a plain-text notice, guaranteeing both parameters always carry something.
    public (string inOffice, string outOfOffice) GetRequestViewUrlPair(string secureToken)
    {
        var lan = _cfg["Application:LanBaseUrl"];
        var pub = _cfg["Application:PublicBaseUrl"];
        var auto = GetRequestViewUrl(secureToken); // NGROK_URL / Application:BaseUrl / request host

        const string unavailable = "(link unavailable)";

        var inOffice = !string.IsNullOrWhiteSpace(lan)
            ? $"{lan.TrimEnd('/')}/r/{secureToken}"
            : (!string.IsNullOrWhiteSpace(auto) ? auto : unavailable);

        var outOfOffice = !string.IsNullOrWhiteSpace(pub)
            ? $"{pub.TrimEnd('/')}/r/{secureToken}"
            : (!string.IsNullOrWhiteSpace(auto) ? auto : unavailable);

        return (inOffice, outOfOffice);
    }

    private static bool IsLocalHost(string host) =>
        string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
        host == "127.0.0.1" || host == "::1";
}
