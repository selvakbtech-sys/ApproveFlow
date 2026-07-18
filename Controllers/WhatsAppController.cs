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
    private readonly IWebHostEnvironment _env;

    public WhatsAppController(
        AppDbContext db,
        IConfiguration cfg,
        IRequestService svc,
        IWhatsAppService wa,
        ILogger<WhatsAppController> log,
        IWebHostEnvironment env)
    {
        _db = db; _cfg = cfg; _svc = svc; _wa = wa; _log = log; _env = env;
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

            // ── Delivery-status callbacks (sent / delivered / read / failed) ──────
            // Meta reports an outbound message's FATE here, in "statuses" — NOT in
            // "messages". Until now these were dropped by the check below, so the history's
            // Status only ever recorded that the Graph API ACCEPTED the send. Meta returns
            // 200 + a wamid even for a message it will never deliver (recipient not on the
            // WABA's allowed-recipient list, not a WhatsApp user, handset unreachable...),
            // which makes an undelivered notification indistinguishable from a delivered
            // one. Recording the callback is what turns "X didn't receive it" from
            // guesswork into a logged reason from Meta itself.
            if (value.TryGetProperty("statuses", out var statuses) &&
                statuses.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                await HandleStatusCallbacksAsync(statuses);
                return Ok();
            }

            if (!value.TryGetProperty("messages", out var messages))
            {
                _log.LogInformation("WA-WEBHOOK: No messages in payload — skipping.");
                return Ok();
            }

            var message = messages[0];
            var msgType = message.GetProperty("type").GetString();
            var fromPhone = message.GetProperty("from").GetString() ?? "";
            var inboundMsgId = message.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;

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
                        // Only ONE action is allowed per request. A later tap on any button —
                        // including the now-inactive Approve button on the original card — must
                        // not process. Reply with a friendly notice instead of silently dropping
                        // it, so the approver understands why nothing happened.
                        var normDonePhone = fromPhone.Replace(" ", "").Replace("-", "").TrimStart('+');
                        await SendConfirmationAsync(normDonePhone, approverRecord.Status == "approved"
                            ? "This request has already been approved. No further action is required."
                            : "This request has already been processed. No further actions are allowed.");
                        return Ok();
                    }

                    _log.LogInformation("WA-WEBHOOK: Processing Action={Action} RequestId={Id} UserId={Uid}",
                        action, requestId, userId);

                    // Map the tapped button to a workflow action. ALL actions — Approve,
                    // Reject, Clarification — now open the same reply screen and use the same
                    // pending-reply flow, so the UX is identical at every level (L1/L2/L3…).
                    var actionType = action switch
                    {
                        "APPROVE"       => "approved",
                        "REJECT"        => "rejected",
                        "CLARIFICATION" => "clarification",
                        _               => null
                    };
                    if (actionType == null) return Ok();

                    var normBtnPhone = fromPhone.Replace(" ", "").Replace("-", "").TrimStart('+');
                    var replyWindowSec = int.TryParse(_cfg["WhatsApp:ApprovalReplyWindowSeconds"], out var rws) ? rws : 30;

                    // wamid of the interactive card this button belongs to. Stored on the
                    // approve session as ParentMessageId so a reply to the ORIGINAL card —
                    // not just to our prompt — still resolves to THIS request.
                    string? parentMsgId = null;
                    if (message.TryGetProperty("context", out var btnCtxNode) &&
                        btnCtxNode.TryGetProperty("id", out var btnCtxIdNode))
                        parentMsgId = btnCtxIdNode.GetString();

                    // NOTE: there is deliberately NO per-approver concurrency check here. An
                    // approver may tap Approve on any number of requests back to back; each
                    // opens its own independent session (below) and is never blocked by, and
                    // never counted against, any other session that is waiting for a reply.

                    // One action per request. Reject/Clarification don't commit immediately —
                    // they wait for a typed reason, so the approver's row stays "pending" and the
                    // "already actioned" check above can't catch a follow-up tap. If the approver
                    // has already chosen Reject or Clarification for THIS request, refuse a
                    // DIFFERENT action (e.g. Approve after Reject) so only the first choice counts.
                    // Re-tapping the SAME button is allowed (it just re-sends the reason prompt).
                    var pendingChoice = await _db.WhatsAppPendingClarifications
                        .Where(p => p.Phone == normBtnPhone && p.RequestId == requestId && p.ApproverId == userId
                                 && (p.Action == "rejected" || p.Action == "clarification"))
                        .OrderByDescending(p => p.CreatedAt)
                        .Select(p => p.Action)
                        .FirstOrDefaultAsync();
                    if (pendingChoice != null && !string.Equals(actionType, pendingChoice, StringComparison.Ordinal))
                    {
                        _log.LogWarning("WA-WEBHOOK: Second differing action blocked. RequestId={Id} Chosen={Chosen} New={New}",
                            requestId, pendingChoice, actionType);
                        await SendConfirmationAsync(normBtnPhone,
                            $"You have already chosen to {(pendingChoice == "rejected" ? "reject" : "request clarification on")} this request. " +
                            "Please type your reason to complete it — no other action is allowed.");
                        return Ok();
                    }

                    if (actionType == "approved")
                    {
                        // ── APPROVE = DEFERRED, ONE INDEPENDENT SESSION PER REQUEST ───────
                        // Do NOT execute the workflow now. Open a WaitingForReply session
                        // scoped to THIS request and start its own reply window. The approval
                        // runs later — when the approver replies with an optional remark
                        // (webhook text branch) OR when the window expires (timeout sweep) —
                        // so the requester / next approver receive the final approval
                        // notification WITH the remark. Claiming the session
                        // (WaitingForReply → Completed) is the atomic guarantee that the
                        // approval executes exactly once. Sessions for this approver's OTHER
                        // requests are untouched and keep running in parallel.
                        var sessionOpen = await _db.WhatsAppPendingClarifications
                            .AnyAsync(p => p.RequestId == requestId && p.ApproverId == userId
                                        && p.Action == "approved" && p.Status == "WaitingForReply");
                        if (sessionOpen)
                        {
                            // Duplicate tap / duplicate webhook delivery for a session that is
                            // already open. Ignore it: a second session could approve twice, and
                            // re-opening the window would silently extend the approver's timer.
                            _log.LogInformation("WA-WEBHOOK: duplicate APPROVE ignored — session already open RequestId={Id} UserId={Uid}",
                                requestId, userId);
                            return Ok();
                        }

                        // IST like every other stored timestamp (SaveChanges stamps CreatedAt
                        // the same way), so ExpiresOn and CreatedAt read consistently. All are
                        // DateTimeOffset, so window comparisons are instant-based regardless.
                        var openedAt = IstClock.Now;
                        var session = new WhatsAppPendingClarification
                        {
                            Phone = normBtnPhone, RequestId = requestId, ApproverId = userId,
                            Action = "approved", Status = "WaitingForReply",
                            ParentMessageId = parentMsgId,
                            CreatedAt = openedAt, ExpiresOn = openedAt.AddSeconds(replyWindowSec)
                        };
                        _db.WhatsAppPendingClarifications.Add(session);
                        await _db.SaveChangesAsync();

                        // Prompt AFTER the session exists, then record the prompt's wamid on it.
                        // A swipe-reply to the prompt carries that wamid as context.id, which is
                        // how the reply is matched back to this exact request with no guessing.
                        var promptMsgId = await _wa.SendApproveReplyPromptAsync(
                            normBtnPhone, requestId, userId, replyWindowSec);
                        if (!string.IsNullOrEmpty(promptMsgId))
                            await _db.WhatsAppPendingClarifications
                                .Where(p => p.Id == session.Id)
                                .ExecuteUpdateAsync(s => s.SetProperty(p => p.WhatsAppMessageId, promptMsgId));

                        _log.LogInformation("WA-WEBHOOK: approve session opened SessionId={Sid} RequestId={Id} UserId={Uid} PromptMsgId={Mid} ParentMsgId={Pid} ExpiresOn={Exp}",
                            session.Id, requestId, userId, promptMsgId, parentMsgId, session.ExpiresOn);
                    }
                    else
                    {
                        // ── REJECT / CLARIFICATION = mandatory remark. Create the pending
                        // WaitingForReply state, prompt, and wait for the reply (unchanged).
                        // The reset is scoped to this phone's OWN reject/clarification pendings:
                        // approve sessions are excluded so choosing Reject on one request can
                        // never silently cancel approvals still awaiting a reply on others.
                        await _db.WhatsAppPendingClarifications
                            .Where(p => p.Phone == normBtnPhone && p.Action != "approved").ExecuteDeleteAsync();
                        _db.WhatsAppPendingClarifications.Add(new WhatsAppPendingClarification
                        {
                            Phone = normBtnPhone, RequestId = requestId, ApproverId = userId,
                            Action = actionType, CreatedAt = DateTimeOffset.UtcNow
                        });
                        await _db.SaveChangesAsync();
                        _log.LogInformation("WA-WEBHOOK: pending created (WaitingForReply) Action={Action} RequestId={Id} Phone={Phone}",
                            actionType, requestId, normBtnPhone);

                        await _wa.SendActionPromptAsync(normBtnPhone, requestId, userId, actionType);

                        try { await _wa.LogInboundMessageAsync(requestId, normBtnPhone, actionType, buttonPayload, inboundMsgId, userId); }
                        catch (Exception logEx) { _log.LogError(logEx, "WA-WEBHOOK: inbound button log failed (non-fatal) RequestId={Id}", requestId); }
                    }
                }
                return Ok();
            }

            // ── Inbound media reply (document/image) → attach to the approval ──
            // Only the optional post-approval reply ("approved_remark" pending) accepts
            // attachments. A caption on the media is stored as the approval remark, so a
            // single media message covers text-only, file-only, and file+text.
            if (msgType == "image" || msgType == "document")
            {
                var normMediaPhone = fromPhone.Replace(" ", "").Replace("-", "").TrimStart('+');
                var mediaNode = message.GetProperty(msgType);
                var mediaId  = mediaNode.TryGetProperty("id", out var midEl) ? midEl.GetString() : null;
                var caption  = mediaNode.TryGetProperty("caption", out var capEl) ? capEl.GetString() : null;
                var origName = mediaNode.TryGetProperty("filename", out var fnEl) ? fnEl.GetString() : null; // documents only
                var mimeType = mediaNode.TryGetProperty("mime_type", out var mmEl) ? mmEl.GetString() : null;

                // Completed/expired approve sessions are retained as audit records, so they
                // are filtered out here — only a still-open pending can take a media reply.
                var mediaPending = await _db.WhatsAppPendingClarifications
                    .Where(p => p.Phone == normMediaPhone && p.Status == "WaitingForReply")
                    .OrderByDescending(p => p.CreatedAt)
                    .FirstOrDefaultAsync();

                if (mediaPending == null || string.IsNullOrEmpty(mediaId))
                    return Ok();

                if (mediaPending.Action != "approved_remark")
                {
                    // Reject/Clarification require a typed reason — attachments not accepted there.
                    await SendConfirmationAsync(normMediaPhone, "⚠️ Please type your reply as a text message.");
                    return Ok();
                }

                var windowMin = int.TryParse(_cfg["WhatsApp:ApprovalRemarkWindowMinutes"], out var wm) ? wm : 10;
                if ((DateTimeOffset.UtcNow - mediaPending.CreatedAt).TotalMinutes > windowMin)
                {
                    _db.WhatsAppPendingClarifications.Remove(mediaPending);
                    await _db.SaveChangesAsync();
                    _log.LogInformation("WA-WEBHOOK: media reply ignored (outside {Win}-min window) RequestId={Id}", windowMin, mediaPending.RequestId);
                    return Ok();
                }

                var allowedExts = new[] { ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".png", ".jpg", ".jpeg" };
                var ext = !string.IsNullOrEmpty(origName)
                    ? System.IO.Path.GetExtension(origName).ToLowerInvariant()
                    : MimeToExt(mimeType);
                if (string.IsNullOrEmpty(ext) || !allowedExts.Contains(ext))
                {
                    await SendConfirmationAsync(normMediaPhone, "⚠️ Unsupported file type. Allowed: PDF, DOC, DOCX, XLS, XLSX, JPG, PNG.");
                    return Ok();
                }

                var (dok, bytes, dmime, derr) = await _wa.DownloadInboundMediaAsync(mediaId);
                if (!dok || bytes == null)
                {
                    _log.LogError("WA-WEBHOOK: media download failed RequestId={Id} err={Err}", mediaPending.RequestId, derr);
                    await SendConfirmationAsync(normMediaPhone, "Unable to upload attachment.");
                    return Ok();
                }

                var maxMb = int.TryParse(_cfg["AppSettings:MaxFileUploadMB"], out var mb) ? mb : 20;
                if (bytes.Length > maxMb * 1024L * 1024L)
                {
                    await SendConfirmationAsync(normMediaPhone, $"⚠️ Attachment exceeds {maxMb} MB.");
                    return Ok();
                }

                try
                {
                    var webRoot = _env.WebRootPath ?? System.IO.Path.Combine(_env.ContentRootPath, "wwwroot");
                    var dir = System.IO.Path.Combine(webRoot, "uploads", "whatsapp");
                    System.IO.Directory.CreateDirectory(dir);
                    var stored = $"{Guid.NewGuid()}{ext}";
                    await System.IO.File.WriteAllBytesAsync(System.IO.Path.Combine(dir, stored), bytes);
                    var url = $"/uploads/whatsapp/{stored}";
                    var displayName = !string.IsNullOrEmpty(origName) ? origName : $"attachment{ext}";

                    try { await _wa.LogInboundMessageAsync(mediaPending.RequestId, normMediaPhone, "approval_reply_media", caption ?? displayName, inboundMsgId, mediaPending.ApproverId); }
                    catch (Exception logEx) { _log.LogError(logEx, "WA-WEBHOOK: inbound media log failed (non-fatal) RequestId={Id}", mediaPending.RequestId); }

                    var (rok, rerr) = await _svc.AttachApprovalReplyAsync(
                        mediaPending.RequestId, mediaPending.ApproverId, caption, displayName, url, inboundMsgId);

                    _db.WhatsAppPendingClarifications.Remove(mediaPending);
                    await _db.SaveChangesAsync();

                    await SendConfirmationAsync(normMediaPhone, rok ? "📝 Remark added to your approval." : $"⚠️ {rerr}");
                    _log.LogInformation("WA-WEBHOOK: media reply saved RequestId={Id} ok={Ok} file={File}", mediaPending.RequestId, rok, displayName);
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "WA-WEBHOOK: saving media reply failed RequestId={Id}", mediaPending.RequestId);
                    await SendConfirmationAsync(normMediaPhone, "Unable to upload attachment.");
                }
                return Ok();
            }

            if (msgType == "text")
            {
                var textBody  = message.GetProperty("text").GetProperty("body").GetString() ?? "";
                var normPhone = fromPhone.Replace(" ", "").Replace("-", "").TrimStart('+');
                _log.LogInformation("WA-WEBHOOK: Text from={Phone} body={Body}", normPhone, textBody);

                // context.id is present when the approver REPLIED to a specific message
                // (the approval card, or our optional-remark prompt). It is the authoritative
                // link from a reply back to the request it came from.
                string? contextId = null;
                if (message.TryGetProperty("context", out var ctxNode) &&
                    ctxNode.TryGetProperty("id", out var ctxIdNode))
                    contextId = ctxIdNode.GetString();

                // ── 0) Clarification Response via swipe-reply (REQUESTOR) ────────────────
                // A requestor replying to the "Clarification Needed" card. Resolved by
                // context.id → the outbound message → RequestId; only fires when the sender
                // IS that request's requestor. Runs FIRST so a requestor's clarification
                // response is never mis-handled as an approver reply/command. Reuses the web
                // Clarification-Submit logic (RequestService.SubmitClarificationResponseAsync).
                // Returns true only when it has taken ownership of the message.
                if (!string.IsNullOrEmpty(contextId) &&
                    await TryHandleClarificationReplyAsync(contextId!, textBody, normPhone, inboundMsgId))
                    return Ok();

                // ── 1) Deferred-approve reply matched by MESSAGE ID (authoritative) ───────
                // Any number of approve sessions may be open for this approver, so the request
                // is resolved from the message id the reply quotes — never from the phone, the
                // newest session, or how many are open.
                if (!string.IsNullOrEmpty(contextId))
                {
                    var (byId, stop) = await MatchApproveSessionByMessageIdAsync(normPhone, contextId!);
                    if (stop) return Ok();
                    if (byId != null)
                    {
                        await ExecuteApproveReplyAsync(byId, textBody, normPhone, inboundMsgId);
                        return Ok();
                    }
                }

                // ── 2) Reject / Clarification pending — matched by phone, UNCHANGED ───────
                // Approve sessions are excluded so that concurrent approvals can never shadow
                // a mandatory rejection/clarification reason typed by the same approver.
                var pending = await _db.WhatsAppPendingClarifications
                    .Where(p => p.Phone == normPhone && p.Action != "approved")
                    .OrderByDescending(p => p.CreatedAt)
                    .FirstOrDefaultAsync();

                _log.LogInformation("WA-WEBHOOK: PendingClarification found={Found} for Phone={Phone}",
                    pending != null, normPhone);

                if (pending != null)
                {
                    var replyAction = pending.Action; // approved_remark | rejected | clarification

                    if (replyAction == "approved_remark")
                    {
                        // OPTIONAL post-approval remark. The approval already completed on the
                        // button tap and the next approver was already notified — this text is
                        // only attached as a remark if it arrives within the configured window.
                        var windowMin = int.TryParse(_cfg["WhatsApp:ApprovalRemarkWindowMinutes"], out var m) ? m : 10;
                        var withinWindow = (DateTimeOffset.UtcNow - pending.CreatedAt).TotalMinutes <= windowMin;

                        _db.WhatsAppPendingClarifications.Remove(pending);
                        await _db.SaveChangesAsync();

                        try { await _wa.LogInboundMessageAsync(pending.RequestId, normPhone, "approval_remark", textBody, inboundMsgId, pending.ApproverId); }
                        catch (Exception logEx) { _log.LogError(logEx, "WA-WEBHOOK: inbound remark log failed (non-fatal) RequestId={Id}", pending.RequestId); }

                        if (withinWindow && !string.IsNullOrWhiteSpace(textBody))
                        {
                            var (rok, rerr) = await _svc.AttachApprovalRemarkAsync(pending.RequestId, pending.ApproverId, textBody);
                            await SendConfirmationAsync(normPhone, rok ? "📝 Remark added to your approval." : $"⚠️ {rerr}");
                            _log.LogInformation("WA-WEBHOOK: approval remark attached RequestId={Id} ok={Ok}", pending.RequestId, rok);
                        }
                        else
                        {
                            _log.LogInformation("WA-WEBHOOK: post-approval text ignored (outside {Win}-min window) RequestId={Id}", windowMin, pending.RequestId);
                        }
                    }
                    else
                    {
                        // REJECT / CLARIFICATION = mandatory remark → process the reply.
                        // If ProcessActionAsync THROWS, keep the pending so the approver can resend.
                        bool ok; string? err;
                        try
                        {
                            (ok, err) = await _svc.ProcessActionAsync(
                                pending.RequestId, pending.ApproverId, replyAction, textBody);
                        }
                        catch (Exception procEx)
                        {
                            _log.LogError(procEx, "WA-WEBHOOK: ProcessAction threw — pending kept for retry. RequestId={Id}", pending.RequestId);
                            await SendConfirmationAsync(normPhone, "⚠️ Something went wrong saving your reply. Please send it again.");
                            return Ok(); // pending intentionally NOT cleared
                        }

                        if (ok)
                        {
                            // Only NOW — after a successful process — clear the pending state.
                            _db.WhatsAppPendingClarifications.Remove(pending);
                            await _db.SaveChangesAsync();

                            var reqInfo = await _db.Requests.AsNoTracking()
                                .Where(r => r.RequestId == pending.RequestId)
                                .Select(r => new { r.RequestNumber, r.Title })
                                .FirstOrDefaultAsync();
                            var reqLabel = $"*{reqInfo?.RequestNumber}*-{reqInfo?.Title}";
                            var confirmText = replyAction == "rejected"
                                ? $"❌ You have rejected ({reqLabel}) this request."
                                : $"💬 Clarification sent for ({reqLabel})! Your message has been sent to the requester.";
                            await SendConfirmationAsync(normPhone, confirmText);
                        }
                        else
                        {
                            // Mandatory-comment validation failed — keep the pending so the
                            // approver can retry, and echo the exact validation message.
                            await SendConfirmationAsync(normPhone, $"⚠️ {err}");
                        }

                        // Best-effort inbound log — must NEVER block reply processing.
                        try { await _wa.LogInboundMessageAsync(pending.RequestId, normPhone, replyAction, textBody, inboundMsgId, pending.ApproverId); }
                        catch (Exception logEx) { _log.LogError(logEx, "WA-WEBHOOK: inbound reply log failed (non-fatal) RequestId={Id}", pending.RequestId); }

                        _log.LogInformation("WA-WEBHOOK: {Action} reply processed RequestId={Id} ok={Ok} err={Err}",
                            replyAction, pending.RequestId, ok, err);
                    }
                }
                else
                {
                    // ── 3) Plain reply, no context id → lone-active-session rule ──────
                    // WhatsApp omits context.id unless the approver explicitly replied to a
                    // message, so a plainly typed remark carries no id to match on.
                    if (string.IsNullOrEmpty(contextId))
                    {
                        var (lone, stop) = await MatchLoneActiveApproveSessionAsync(normPhone);
                        if (stop) return Ok();
                        if (lone != null)
                        {
                            await ExecuteApproveReplyAsync(lone, textBody, normPhone, inboundMsgId);
                            return Ok();
                        }
                    }

                    // ── 4) WhatsApp Reply-Command feature ─────────────────────────────
                    // The approver did NOT tap a button (no pending state) but REPLIED to
                    // an approval message with a text command (A / R / C + optional remark).
                    // Route it to the SAME approval business logic used by the buttons.
                    // Silently ignored if the replied-to message is not a tracked approval.
                    if (!string.IsNullOrEmpty(contextId))
                        await HandleReplyCommandAsync(contextId!, textBody, normPhone, inboundMsgId);
                }
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "WA-WEBHOOK: ❌ Unhandled exception.");
        }

        return Ok();
    }

    // ── Outbound delivery-status tracking ─────────────────────────────────────
    // Applies Meta's callbacks to the outbound WhatsAppMessageHistory row so Status
    // reflects what actually happened to the message rather than merely that the API
    // accepted it. A "failed" callback carries Meta's own error — that error text is the
    // definitive answer to "why didn't this person receive the notification".
    private async Task HandleStatusCallbacksAsync(System.Text.Json.JsonElement statuses)
    {
        foreach (var st in statuses.EnumerateArray())
        {
            var wamid = st.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
            var state = st.TryGetProperty("status", out var stEl) ? stEl.GetString() : null;
            var to    = st.TryGetProperty("recipient_id", out var rcEl) ? rcEl.GetString() : null;
            if (string.IsNullOrEmpty(wamid) || string.IsNullOrEmpty(state)) continue;

            var mapped = state switch
            {
                "sent"      => "Sent",
                "delivered" => "Delivered",
                "read"      => "Read",
                "failed"    => "Failed",
                _           => state
            };

            if (mapped == "Failed")
            {
                // Meta's error object says exactly WHY (e.g. 131026 "Message undeliverable",
                // 131047 re-engagement, recipient not in the allowed list). Logged at Error
                // so it stands out — this is the line to read when an alert never arrives.
                var err = st.TryGetProperty("errors", out var errs) &&
                          errs.ValueKind == System.Text.Json.JsonValueKind.Array &&
                          errs.GetArrayLength() > 0
                    ? errs[0].ToString()
                    : "(no error detail supplied)";
                _log.LogError("WA-STATUS: ❌ DELIVERY FAILED to={To} wamid={Wamid} error={Err}", to, wamid, err);
            }
            else
            {
                _log.LogInformation("WA-STATUS: {State} to={To} wamid={Wamid}", mapped, to, wamid);
            }

            var row = await _db.WhatsAppMessageHistories
                .FirstOrDefaultAsync(h => h.MessageId == wamid && h.Direction == "Outbound");
            if (row == null) continue; // transient confirmations aren't written to history

            // Callbacks can arrive out of order (a "read" ahead of its "delivered"), so a
            // row is never downgraded to a weaker state.
            if (StatusRank(mapped) >= StatusRank(row.Status))
            {
                row.Status = mapped;
                await _db.SaveChangesAsync();
            }
        }
    }

    // Progression Sent → Delivered → Read. Failed outranks all, so a real failure can
    // never be masked by a late, weaker callback.
    private static int StatusRank(string? s) => s switch
    {
        "Sent"      => 1,
        "Delivered" => 2,
        "Read"      => 3,
        "Failed"    => 4,
        _           => 0
    };

    // ── Deferred-approve reply matching ───────────────────────────────────────
    // An approver may hold any number of open approve sessions at once, so a reply is
    // resolved to its request by WhatsApp message id and NEVER by phone alone, by the
    // oldest/newest session, or by how many are open.

    // Has this session's optional-reply window closed? Sessions opened before ExpiresOn
    // existed fall back to CreatedAt + the configured window, so in-flight rows from the
    // previous build still behave correctly.
    private bool IsSessionExpired(WhatsAppPendingClarification s)
    {
        var windowSec = int.TryParse(_cfg["WhatsApp:ApprovalReplyWindowSeconds"], out var ws) ? ws : 30;
        var expiresOn = s.ExpiresOn ?? s.CreatedAt.AddSeconds(windowSec);
        return expiresOn <= IstClock.Now;
    }

    // Resolves a reply from the message id it quotes: context.id is either our
    // optional-remark prompt (WhatsAppMessageId) or the approval card the button came
    // from (ParentMessageId). The phone must match too, so a message id can never be
    // replayed from another number. stop=true means the id DID resolve to a session that
    // must not be processed — already completed, or past its window — so the reply is
    // dropped and logged rather than overwriting a remark or re-approving.
    private async Task<(WhatsAppPendingClarification? session, bool stop)> MatchApproveSessionByMessageIdAsync(
        string normPhone, string contextId)
    {
        var session = await _db.WhatsAppPendingClarifications.AsNoTracking()
            .Where(p => p.Action == "approved" && p.Phone == normPhone
                     && (p.WhatsAppMessageId == contextId || p.ParentMessageId == contextId))
            .OrderByDescending(p => p.CreatedAt)
            .FirstOrDefaultAsync();

        if (session == null) return (null, false);

        if (session.Status != "WaitingForReply")
        {
            _log.LogInformation("WA-WEBHOOK: approve reply ignored — session {Sid} already {Status}. RequestId={Id} ContextId={Ctx}",
                session.Id, session.Status, session.RequestId, contextId);
            return (null, true);
        }

        if (IsSessionExpired(session))
        {
            _log.LogInformation("WA-WEBHOOK: approve reply ignored — session {Sid} expired at {Exp}. RequestId={Id}; auto-approval will complete it without a remark.",
                session.Id, session.ExpiresOn, session.RequestId);
            return (null, true);
        }

        return (session, false);
    }

    // Fallback for a plainly typed reply, which carries no context id at all. With exactly
    // ONE active session there is a single candidate, so attaching the remark is
    // deterministic rather than a guess. With more than one the reply is dropped and
    // logged — every session still auto-approves on its own timeout, just without a
    // remark. The approver is never asked to disambiguate and is never blocked.
    private async Task<(WhatsAppPendingClarification? session, bool stop)> MatchLoneActiveApproveSessionAsync(string normPhone)
    {
        var open = await _db.WhatsAppPendingClarifications.AsNoTracking()
            .Where(p => p.Phone == normPhone && p.Action == "approved" && p.Status == "WaitingForReply")
            .ToListAsync();

        var active = open.Where(p => !IsSessionExpired(p)).ToList();

        if (active.Count == 1) return (active[0], false);

        if (active.Count > 1)
        {
            _log.LogInformation("WA-WEBHOOK: plain reply dropped — {Count} approve sessions active for Phone={Phone} with no context id (RequestIds={Ids}). Each will auto-approve without a remark.",
                active.Count, normPhone, string.Join(",", active.Select(p => p.RequestId)));
            return (null, true);
        }

        return (null, false);
    }

    // Executes a deferred approval from the approver's in-window reply, with the reply
    // text as the optional remark. The claim inside TryExecuteDeferredApproveAsync settles
    // the race against the timeout sweep, so the approval runs exactly once either way.
    private async Task ExecuteApproveReplyAsync(
        WhatsAppPendingClarification session, string textBody, string normPhone, string? inboundMsgId)
    {
        var (claimed, ok, err) = await _svc.TryExecuteDeferredApproveAsync(
            session.Id, session.RequestId, session.ApproverId, textBody);

        if (!claimed)
        {
            // The timeout sweep won the race and already auto-approved (no remark).
            _log.LogInformation("WA-WEBHOOK: approve reply lost claim — session {Sid} already completed. RequestId={Id}",
                session.Id, session.RequestId);
            return;
        }

        // Best-effort inbound log so Request History shows the WhatsApp-origin approval
        // and its remark (matched to the "approved" action entry).
        try { await _wa.LogInboundMessageAsync(session.RequestId, normPhone, "approved", textBody, inboundMsgId, session.ApproverId); }
        catch (Exception logEx) { _log.LogError(logEx, "WA-WEBHOOK: inbound approve-remark log failed (non-fatal) RequestId={Id}", session.RequestId); }

        var reqInfo = await _db.Requests.AsNoTracking()
            .Where(r => r.RequestId == session.RequestId)
            .Select(r => new { r.RequestNumber, r.Title })
            .FirstOrDefaultAsync();
        var reqLabel = $"*{reqInfo?.RequestNumber}* - {reqInfo?.Title}";
        await SendConfirmationAsync(normPhone, ok
            ? $"✅ {reqLabel} approved. Your remark has been sent to the requester."
            : $"⚠️ {err}");

        _log.LogInformation("WA-WEBHOOK: approve reply processed session {Sid} RequestId={Id} ok={Ok} err={Err}",
            session.Id, session.RequestId, ok, err);
    }

    // Maps a WhatsApp media MIME type to a file extension. Used for inbound images,
    // which (unlike documents) arrive without a filename.
    private static string MimeToExt(string? mime) => (mime ?? "").ToLowerInvariant() switch
    {
        "image/jpeg" => ".jpg",
        "image/png" => ".png",
        "application/pdf" => ".pdf",
        "application/msword" => ".doc",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => ".docx",
        "application/vnd.ms-excel" => ".xls",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" => ".xlsx",
        _ => ""
    };

    // ── WhatsApp Clarification-Response handler (Requestor swipe-reply) ────────
    // Turns a requestor's reply to the "Clarification Needed" WhatsApp card into a
    // clarification response, IDENTICAL to submitting it from the web app. It contains NO
    // workflow logic of its own — it resolves the reply back to the request and calls the
    // EXISTING IRequestService.SubmitClarificationResponseAsync (same method the web
    // "Submit Clarification" panel uses), which saves the response, appends the
    // clarification_response history record, resumes the workflow and re-notifies the
    // current approver (in-app + the WhatsApp resubmit card). One business logic only.
    //
    // Returns TRUE when this handler owns the message (processed, rejected, or a duplicate)
    // so the webhook stops; FALSE when it is NOT a requestor clarification reply, so the
    // normal approver flow continues untouched.
    private async Task<bool> TryHandleClarificationReplyAsync(
        string contextId, string textBody, string normPhone, string? inboundMsgId)
    {
        try
        {
            // 1) Resolve the replied-to OUTBOUND message → RequestId (authoritative link;
            //    context.id is the wamid of the clarification card we sent the requestor).
            var requestId = await _db.WhatsAppMessageHistories.AsNoTracking()
                .Where(h => h.MessageId == contextId && h.Direction == "Outbound" && h.RequestId != null)
                .OrderByDescending(h => h.CreatedDate)
                .Select(h => h.RequestId)
                .FirstOrDefaultAsync();
            if (requestId == null || requestId == 0)
                return false; // not a reply to a tracked request message → not ours

            // 2) Load the request + its requestor's WhatsApp number.
            var r = await _db.Requests.AsNoTracking()
                .Where(x => x.RequestId == requestId.Value)
                .Select(x => new { x.RequestId, x.RequestNumber, x.Status, x.RequestorId, Phone = x.Requestor.WhatsAppNumber })
                .FirstOrDefaultAsync();
            if (r == null) return false;

            // 3) SECURITY — the sender MUST be this request's original requestor. If not, this
            //    is not a clarification response (likely an approver reply) → defer, don't own.
            //    This blocks anyone else from replying on the requestor's behalf.
            if (!PhonesMatch(r.Phone, normPhone))
                return false;

            // From here we OWN the message (it is the requestor replying to their request).

            // 3a) Duplicate guard — the same inbound WhatsApp message id already processed.
            //     Prevents a duplicate webhook delivery from being handled twice (and avoids a
            //     confusing "no longer active" reply once the status has already moved on).
            if (!string.IsNullOrEmpty(inboundMsgId))
            {
                var already = await _db.WhatsAppMessageHistories.AsNoTracking()
                    .AnyAsync(h => h.MessageId == inboundMsgId && h.Direction == "Inbound");
                if (already)
                {
                    _log.LogInformation("WA-CLARIFY: duplicate inbound msg {Mid} ignored RequestId={Id}", inboundMsgId, r.RequestId);
                    return true;
                }
            }

            // 3b) The request must still be awaiting clarification. Any other status
            //     (Approved / Rejected / Revoked / Deleted / Pending Approval / …) means the
            //     clarification is no longer active — reject WITHOUT touching the database.
            if (r.Status != RequestStatus.ClarificationNeeded)
            {
                _log.LogInformation("WA-CLARIFY: RequestId={Id} Number={Num} not awaiting clarification (status={Status}) — reply rejected. From={Phone} Ctx={Ctx} InMsg={Mid}",
                    r.RequestId, r.RequestNumber, r.Status, normPhone, contextId, inboundMsgId);
                await SendConfirmationAsync(normPhone,
                    "This clarification request is no longer active or cannot be processed.");
                return true;
            }

            // 4) Reuse the EXACT web Clarification-Submit business logic. This saves the
            //    response, writes the clarification_response history entry, sets the request
            //    back to Pending Approval, and re-notifies the current approver (in-app +
            //    WhatsApp resubmit card) — everything the web flow does, no duplication.
            bool ok; string? err;
            try
            {
                (ok, err) = await _svc.SubmitClarificationResponseAsync(r.RequestId, r.RequestorId, textBody);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "WA-CLARIFY: SubmitClarificationResponse threw RequestId={Id}", r.RequestId);
                await SendConfirmationAsync(normPhone,
                    "This clarification request is no longer active or cannot be processed.");
                return true;
            }

            // 5) Persist the inbound reply as a clarification_response history record
            //    (Source = WhatsApp). Best-effort — must never block the response.
            try { await _wa.LogInboundMessageAsync(r.RequestId, normPhone, "clarification_response", textBody, inboundMsgId, r.RequestorId); }
            catch (Exception logEx) { _log.LogError(logEx, "WA-CLARIFY: inbound log failed (non-fatal) RequestId={Id}", r.RequestId); }

            // 6) Confirm to the requestor.
            await SendConfirmationAsync(normPhone, ok
                ? $"✅ Your clarification for *{r.RequestNumber}* has been submitted. The approver has been notified."
                : $"⚠️ {err}");

            _log.LogInformation("WA-CLARIFY: RequestId={Id} Number={Num} From={Phone} Ctx={Ctx} InMsg={Mid} ok={Ok} err={Err} reply={Reply}",
                r.RequestId, r.RequestNumber, normPhone, contextId, inboundMsgId, ok, err, textBody);

            return true;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "WA-CLARIFY: Unhandled exception handling clarification reply. context.id={Ctx}", contextId);
            return false;
        }
    }

    // ── WhatsApp Reply-Command handler ────────────────────────────────────────
    // Executes a workflow action from a text reply the approver sent to an approval
    // notification. It does NOT contain any workflow logic of its own — it resolves the
    // reply back to the correct approval record and then calls the EXISTING
    // IRequestService.ProcessActionAsync, the very same method the interactive
    // Approve / Reject / Clarification buttons use. One business logic only.
    private async Task HandleReplyCommandAsync(string contextId, string textBody, string normPhone, string? inboundMsgId)
    {
        try
        {
            // 1) Resolve the replied-to OUTBOUND message → RequestId. Both the approval
            //    template and its follow-up reply-instructions text are logged against the
            //    request, so a reply to EITHER resolves here. We only need the RequestId;
            //    the acting approver is identified by the sender's phone in step 3, which
            //    also works for notifications sent before this feature was deployed.
            var requestId = await _db.WhatsAppMessageHistories.AsNoTracking()
                .Where(h => h.MessageId == contextId && h.Direction == "Outbound" && h.RequestId != null)
                .OrderByDescending(h => h.CreatedDate)
                .Select(h => h.RequestId)
                .FirstOrDefaultAsync();

            if (requestId == null || requestId == 0)
            {
                // Not a reply to a tracked request message → ignore (per spec).
                _log.LogInformation("WA-REPLY: context.id={Ctx} is not a tracked request message — ignoring.", contextId);
                return;
            }

            // 2) Parse the command (first token) and remark (everything after it).
            var (action, remark) = ParseReplyCommand(textBody);
            if (action == null)
            {
                await SendConfirmationAsync(normPhone,
                    "Invalid command.\n\nUse:\nA = Approve\nR = Reject\nC = Clarification");
                _log.LogInformation("WA-REPLY: Invalid command from={Phone} RequestId={Id} body={Body}", normPhone, requestId, textBody);
                return;
            }

            // 3) SECURITY + routing — the sender must be an approver ON THIS REQUEST. Match the
            //    inbound number against each approver's stored WhatsAppNumber: this both picks
            //    the acting approver and blocks replies from unknown/unauthorised numbers.
            //    Prefer an approver whose step is currently actionable (pending/clarification).
            var approvers = await _db.RequestApprovers.AsNoTracking()
                .Where(a => a.RequestId == requestId)
                .Select(a => new { a.ApproverUserId, a.Status, a.StepLevel, Phone = a.ApproverUser.WhatsAppNumber })
                .ToListAsync();

            var acting = approvers
                .Where(a => PhonesMatch(a.Phone, normPhone))
                .OrderBy(a => (a.Status == "pending" || a.Status == "clarification") ? 0 : 1)
                .ThenBy(a => a.StepLevel)
                .FirstOrDefault();

            if (acting == null)
            {
                _log.LogWarning("WA-REPLY: Unauthorised sender rejected. RequestId={Id} From={From}", requestId, normPhone);
                await SendConfirmationAsync(normPhone,
                    "You are not authorized to action this request from this number.");
                return;
            }

            var approverId = acting.ApproverUserId;

            // 4) Guard against acting on an already-completed step (same rule as the button flow).
            var alreadyActioned = acting.Status != "pending" && acting.Status != "clarification";
            if (alreadyActioned)
            {
                await SendConfirmationAsync(normPhone, "This approval has already been processed.");
                _log.LogInformation("WA-REPLY: Already processed RequestId={Id} ApproverId={Aid} Status={Status}",
                    requestId, approverId, acting.Status);
                return;
            }

            // 5) Execute the EXISTING approval business logic — no duplicate workflow engine.
            //    This updates the request, approval history, audit trail and fires all the
            //    existing notifications (next approver / requester) automatically.
            bool ok; string? err;
            try
            {
                (ok, err) = await _svc.ProcessActionAsync(requestId.Value, approverId, action, remark);
            }
            catch (Exception procEx)
            {
                _log.LogError(procEx, "WA-REPLY: ProcessAction threw RequestId={Id} Action={Action}", requestId, action);
                await SendConfirmationAsync(normPhone, "⚠️ Something went wrong saving your reply. Please try again.");
                return;
            }

            // 6) Persist the inbound reply (RequestId, ApproverId, MessageId, text, action,
            //    mobile, date) via the central history writer. MessageType = the action, so
            //    Request History automatically shows the entry with the "via WhatsApp" badge.
            try { await _wa.LogInboundMessageAsync(requestId.Value, normPhone, action, textBody, inboundMsgId, approverId); }
            catch (Exception logEx) { _log.LogError(logEx, "WA-REPLY: inbound reply log failed (non-fatal) RequestId={Id}", requestId); }

            // 7) Confirm back to the approver.
            if (ok)
            {
                var confirm = action switch
                {
                    "approved" => "✅ Approval recorded via WhatsApp reply. The workflow will continue automatically.",
                    "rejected" => "❌ Rejection recorded. The requester has been notified.",
                    _          => "💬 Clarification saved and sent to the requester."
                };
                await SendConfirmationAsync(normPhone, confirm);
            }
            else
            {
                // e.g. mandatory-remark validation ("Rejection remarks are required.").
                await SendConfirmationAsync(normPhone, $"⚠️ {err}");
            }

            _log.LogInformation("WA-REPLY: {Action} processed via reply RequestId={Id} ApproverId={Aid} ok={Ok} err={Err}",
                action, requestId, approverId, ok, err);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "WA-REPLY: Unhandled exception handling reply command. context.id={Ctx}", contextId);
        }
    }

    // Maps the FIRST token of an approver's text reply to a workflow action; everything
    // after the first token is the remark. Comparison is case-insensitive. Returns
    // (null, "") when the first token is not a recognised command.
    private static (string? action, string remark) ParseReplyCommand(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return (null, "");

        var trimmed  = text.Trim();
        var spaceIdx = trimmed.IndexOfAny(new[] { ' ', '\t', '\r', '\n' });
        var token    = (spaceIdx < 0 ? trimmed : trimmed.Substring(0, spaceIdx)).ToUpperInvariant();
        var remark   = spaceIdx < 0 ? "" : trimmed.Substring(spaceIdx + 1).Trim();

        return token switch
        {
            "A" or "APPROVE" or "@APPROVE"                     => ("approved", remark),
            "R" or "REJECT" or "@REJECT"                       => ("rejected", remark),
            "C" or "CLARIFY" or "CLARIFICATION" or "@CLARIFY"  => ("clarification", remark),
            _                                                  => (null, "")
        };
    }

    // Sender verification for reply commands. Compares the WhatsApp "from" number with the
    // approver's stored WhatsAppNumber using digits only, with a trailing-digits fallback so
    // a number stored with or without country code still matches while genuinely different
    // numbers are rejected.
    private static bool PhonesMatch(string? approverNumber, string senderNumber)
    {
        if (string.IsNullOrWhiteSpace(approverNumber) || string.IsNullOrWhiteSpace(senderNumber))
            return false;

        static string Digits(string s) => new string(s.Where(char.IsDigit).ToArray());
        var a = Digits(approverNumber);
        var b = Digits(senderNumber);
        if (a.Length == 0 || b.Length == 0) return false;
        if (a == b) return true;

        var tail = Math.Min(10, Math.Min(a.Length, b.Length));
        return a.Substring(a.Length - tail) == b.Substring(b.Length - tail);
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
