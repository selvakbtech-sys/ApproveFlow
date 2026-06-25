namespace ApproveFlow.MVC.Controllers;

[AllowAnonymous]
[Route("WhatsApp")]
public class WhatsAppController : Controller
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _cfg;
    private readonly IRequestService _svc;
    private readonly IWhatsAppService _wa;
    private readonly ILogger<WhatsAppController> _log;

    public WhatsAppController(
        AppDbContext db,
        IConfiguration cfg,
        IRequestService svc,
        IWhatsAppService wa,
        ILogger<WhatsAppController> log)
    {
        _db = db; _cfg = cfg; _svc = svc; _wa = wa; _log = log;
    }

    [HttpGet("Webhook")]
    public IActionResult Webhook(
        [FromQuery(Name = "hub.mode")] string? mode,
        [FromQuery(Name = "hub.verify_token")] string? token,
        [FromQuery(Name = "hub.challenge")] string? challenge)
    {
        var verifyToken = _cfg["WhatsApp:WebhookVerifyToken"] ?? "approveflow2026";
        _log.LogInformation("WA-VERIFY: mode={Mode} token={Token} challenge={Challenge}", mode, token, challenge);

        if (mode == "subscribe" && token == verifyToken && !string.IsNullOrEmpty(challenge))
        {
            _log.LogInformation("WA-VERIFY: ✅ Verification success.");
            return Content(challenge, "text/plain");
        }

        _log.LogWarning("WA-VERIFY: ❌ Failed. Expected token={Expected}, got={Got}", verifyToken, token);
        return Unauthorized();
    }

    [HttpPost("Webhook")]
    public async Task<IActionResult> Webhook([FromBody] System.Text.Json.JsonElement body)
    {
        try
        {
            _log.LogInformation("WA-WEBHOOK: RAW={Json}", body.ToString());

            var entry = body.GetProperty("entry")[0];
            var changes = entry.GetProperty("changes")[0];
            var value = changes.GetProperty("value");

            if (!value.TryGetProperty("messages", out var messages))
            {
                _log.LogInformation("WA-WEBHOOK: No messages in payload — skipping.");
                return Ok();
            }

            var message = messages[0];
            var msgType = message.GetProperty("type").GetString();
            var fromPhone = message.GetProperty("from").GetString() ?? "";

            _log.LogInformation("WA-WEBHOOK: msgType={Type} fromPhone={Phone}", msgType, fromPhone);
            _log.LogInformation("WA-WEBHOOK: RawPayload={Json}", body.ToString());

            string? buttonPayload = null;

            if (msgType == "interactive")
            {
                var interactive = message.GetProperty("interactive");
                var iType = interactive.GetProperty("type").GetString();
                if (iType == "button_reply")
                    buttonPayload = interactive
                        .GetProperty("button_reply")
                        .GetProperty("id").GetString();
            }

            if (msgType == "button")
                buttonPayload = message.GetProperty("button")
                                       .GetProperty("payload").GetString();

            if (!string.IsNullOrEmpty(buttonPayload))
            {
                _log.LogInformation("WA-WEBHOOK: Button Payload={Payload} From={Phone}", buttonPayload, fromPhone);

                var parts = buttonPayload.Split("__");
                if (parts.Length == 3 &&
                    int.TryParse(parts[1], out var requestId) &&
                    int.TryParse(parts[2], out var userId))
                {
                    var action = parts[0].ToUpper();

                    var approverRecord = await _db.RequestApprovers
                        .AsNoTracking()
                        .FirstOrDefaultAsync(a => a.RequestId == requestId && a.ApproverUserId == userId);
                    if (approverRecord == null) { _log.LogWarning("WA-WEBHOOK: No approver record RequestId={Id} UserId={Uid}", requestId, userId); return Ok(); }

                    var isAlreadyActioned = approverRecord.Status != "pending" && approverRecord.Status != "clarification";
                    var isClarificationButtonOnClarification = action == "CLARIFICATION" && approverRecord.Status == "clarification";
                    if (isAlreadyActioned || (approverRecord.Status == "clarification" && action != "CLARIFICATION"))
                    {
                        _log.LogWarning("WA-WEBHOOK: Already actioned — ignoring. RequestId={Id} Status={Status} Action={Action}", requestId, approverRecord.Status, action);
                        return Ok();
                    }

                    _log.LogInformation("WA-WEBHOOK: Processing Action={Action} RequestId={Id} UserId={Uid}",
                        action, requestId, userId);

                    if (action == "APPROVE")
                    {
                        var (ok, err) = await _svc.ProcessActionAsync(
                            requestId, userId, "approved", "Approved via WhatsApp");
                        _log.LogInformation("WA-WEBHOOK: APPROVE done RequestId={Id} ok={Ok} err={Err}", requestId, ok, err);
                    }
                    else if (action == "REJECT")
                    {
                        var (ok, err) = await _svc.ProcessActionAsync(
                            requestId, userId, "rejected", "Rejected via WhatsApp");
                        _log.LogInformation("WA-WEBHOOK: REJECT done RequestId={Id} ok={Ok} err={Err}", requestId, ok, err);
                    }
                    else if (action == "CLARIFICATION")
                    {
                        var normPhone = fromPhone.Replace(" ", "").Replace("-", "").TrimStart('+');

                        var existing = _db.WhatsAppPendingClarifications
                            .Where(p => p.Phone == normPhone);
                        _db.WhatsAppPendingClarifications.RemoveRange(existing);

                        var pending = new WhatsAppPendingClarification
                        {
                            Phone      = normPhone,
                            RequestId  = requestId,
                            ApproverId = userId,
                            CreatedAt  = DateTimeOffset.UtcNow
                        };
                        _db.WhatsAppPendingClarifications.Add(pending);
                        await _db.SaveChangesAsync();

                        await _wa.SendClarificationPromptAsync(normPhone, requestId, userId);
                        _log.LogInformation("WA-WEBHOOK: CLARIFICATION prompt sent RequestId={Id} Phone={Phone}", requestId, normPhone);
                    }
                }
                return Ok();
            }

            if (msgType == "text")
            {
                var textBody  = message.GetProperty("text").GetProperty("body").GetString() ?? "";
                var normPhone = fromPhone.Replace(" ", "").Replace("-", "").TrimStart('+');
                _log.LogInformation("WA-WEBHOOK: Text from={Phone} body={Body}", normPhone, textBody);

                var pending = await _db.WhatsAppPendingClarifications
                    .Where(p => p.Phone == normPhone)
                    .OrderByDescending(p => p.CreatedAt)
                    .FirstOrDefaultAsync();

                _log.LogInformation("WA-WEBHOOK: PendingClarification found={Found} for Phone={Phone}",
                    pending != null, normPhone);

                if (pending != null)
                {
                    var (ok, err) = await _svc.ProcessActionAsync(
                        pending.RequestId, pending.ApproverId, "clarification", textBody);

                    _db.WhatsAppPendingClarifications.Remove(pending);
                    await _db.SaveChangesAsync();

                    _log.LogInformation("WA-WEBHOOK: Clarification saved RequestId={Id} ok={Ok} err={Err}",
                        pending.RequestId, ok, err);

                    await SendConfirmationAsync(normPhone,
                        "✅ Clarification saved!Your comment has been sent to the requestor.");
                }
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "WA-WEBHOOK: ❌ Unhandled exception.");
        }

        return Ok();
    }

    private async Task SendConfirmationAsync(string toPhone, string text)
    {
        try
        {
            var token = _cfg["WhatsApp:BearerToken"];
            var phoneNumId = _cfg["WhatsApp:PhoneNumberId"];
            var apiVersion = _cfg["WhatsApp:ApiVersion"] ?? "v19.0";

            var payload = new
            {
                messaging_product = "whatsapp",
                to = toPhone,
                type = "text",
                text = new { body = text }
            };

            var json = System.Text.Json.JsonSerializer.Serialize(payload);
            var content = new System.Net.Http.StringContent(json, System.Text.Encoding.UTF8, "application/json");

            using var http = new System.Net.Http.HttpClient();
            http.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            await http.PostAsync(
                $"https://graph.facebook.com/{apiVersion}/{phoneNumId}/messages", content);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "WA-WEBHOOK: Failed to send confirmation to {Phone}", toPhone);
        }
    }
}
