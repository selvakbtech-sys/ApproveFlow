using System.Text;
using System.Text.Json;
using ApproveFlow.MVC.Data;
using ApproveFlow.MVC.Models.Domain;
using Microsoft.EntityFrameworkCore;

namespace ApproveFlow.MVC.Services;

public interface IWhatsAppService
{
    Task SendNewRequestToL1Async(int requestId);
    Task SendResubmitToApproverAsync(int requestId, int approverId, int stepLevel);
    Task SendApprovalProgressAsync(int requestId, int actingApproverId);
    Task SendClarificationToCreatorAsync(int requestId, int actingApproverId, string comment);
    Task SendRejectionToCreatorAsync(int requestId, int actingApproverId, string comment);
    Task SendAgingReminderAsync();
    Task SendActionPromptAsync(string toPhone, int requestId, int approverId, string action);
    Task<string?> SendApproveReplyPromptAsync(string toPhone, int requestId, int approverId, int windowSeconds);
    Task SendNotificationRoleAlertAsync(int requestId, int userId, string eventLabel, string inOfficeUrl, string outOfOfficeUrl, int approvedLevel);
    Task LogInboundMessageAsync(int requestId, string mobileNumber, string messageType, string messageText, string? messageId = null, int? approverUserId = null);
    Task SendPlainTextAsync(string toPhone, string message);
    Task<(bool ok, byte[]? bytes, string? mime, string? err)> DownloadInboundMediaAsync(string mediaId);
    Task ProcessOutboxOnceAsync(CancellationToken ct = default);
}

public class WhatsAppService : IWhatsAppService
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _cfg;
    private readonly IHttpClientFactory _http;
    private readonly ILogger<WhatsAppService> _log;

    private readonly IWebHostEnvironment _env;
    private readonly IApplicationUrlService _url;
    private readonly IPendingApprovalSummaryService _pending;

    public WhatsAppService(AppDbContext db, IConfiguration cfg, IHttpClientFactory http, ILogger<WhatsAppService> log, IWebHostEnvironment env,
        IApplicationUrlService url, IPendingApprovalSummaryService pending)
    {
        _db = db; _cfg = cfg; _http = http; _log = log; _env = env; _url = url; _pending = pending;
    }

    private string GetStoragePath()
    {
        var configured = _cfg["AppSettings:FileStoragePath"] ?? "uploads";
        if (System.IO.Path.IsPathRooted(configured))
            return configured;
        return System.IO.Path.Combine(_env.ContentRootPath, configured);
    }

    // The Meta templates new_approval_request1 / approval_required_level2 REQUIRE a
    // DOCUMENT header. When a request has no user attachment we must still supply one,
    // or Meta rejects the entire send with error 132012 and the approver is never
    // notified. We fall back to a generated one-page "Request Summary" PDF so the
    // notification (and its Approve/Reject/Clarification buttons) still goes out.
    private RequestDocument GetHeaderDocOrDefault(Request r, RequestDocument? doc)
    {
        // Path A — the request already has an attachment: use it as the header, generate nothing.
        if (doc != null)
        {
            _log.LogInformation("WA-DOC: existing attachment found for RequestId={Id} — using '{File}' as the header.",
                r.RequestId, doc.FileName);
            return doc;
        }

        // Path B — no attachment: auto-generate the summary PDF header (unchanged behaviour).
        _log.LogInformation("WA-DOC: no attachments for RequestId={Id} — auto-generating summary document.", r.RequestId);
        var path = GetStoragePath();
        var stored = $"_summary_{r.RequestId}.pdf";
        var fullPath = System.IO.Path.Combine(path, stored);
        try
        {
            Directory.CreateDirectory(path);
            File.WriteAllBytes(fullPath, BuildRequestSummaryPdf(r));
            _log.LogInformation("WA-DOC: document generation successful for RequestId={Id} — '{Path}' ({Bytes} bytes).",
                r.RequestId, fullPath, new FileInfo(fullPath).Length);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "WA-DOC: document generation FAILED for RequestId={Id} at '{Path}'.", r.RequestId, fullPath);
        }
        return new RequestDocument { StoredFileName = stored, FileName = $"{r.RequestNumber}.pdf", ContentType = "application/pdf" };
    }

    // Minimal, self-contained single-page PDF (correct xref offsets) — no external
    // library. Used only as the WhatsApp header when a request has no attachment.
    private static byte[] BuildRequestSummaryPdf(Request r)
    {
        static string Esc(string? s) => (s ?? "").Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
        var amount = r.Amount.HasValue ? r.Amount.Value.ToString("N0") : "0";
        var lines = new (string t, int size)[]
        {
            ("Request Summary", 18),
            ($"Request No : {r.RequestNumber}", 12),
            ($"Type       : {r.RequestType?.TypeName}", 12),
            ($"Title      : {r.Title}", 12),
            ($"Amount     : {amount}", 12),
            ($"Priority   : {r.Priority}", 12),
            ($"Raised By  : {r.Requestor?.FullName}", 12),
            ("Note: No file attachment was provided with this request.", 11),
        };
        var content = new StringBuilder();
        int y = 800;
        foreach (var (t, size) in lines)
        {
            content.Append($"BT /F1 {size} Tf 50 {y} Td ({Esc(t)}) Tj ET\n");
            y -= 26;
        }
        var stream = content.ToString();
        var objs = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>",
            "<< /Length " + Encoding.ASCII.GetByteCount(stream) + " >>\nstream\n" + stream + "endstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"
        };
        var pdf = new StringBuilder("%PDF-1.4\n");
        var offsets = new int[objs.Count + 1];
        for (int i = 0; i < objs.Count; i++)
        {
            offsets[i + 1] = Encoding.ASCII.GetByteCount(pdf.ToString());
            pdf.Append((i + 1) + " 0 obj\n" + objs[i] + "\nendobj\n");
        }
        int xref = Encoding.ASCII.GetByteCount(pdf.ToString());
        pdf.Append("xref\n0 " + (objs.Count + 1) + "\n0000000000 65535 f \n");
        for (int i = 1; i <= objs.Count; i++)
            pdf.Append(offsets[i].ToString("D10") + " 00000 n \n");
        pdf.Append("trailer\n<< /Size " + (objs.Count + 1) + " /Root 1 0 R >>\nstartxref\n" + xref + "\n%%EOF");
        return Encoding.ASCII.GetBytes(pdf.ToString());
    }

    // ── 1. New Request → L1 Approver ─────────────────────────
    public async Task SendNewRequestToL1Async(int requestId)
    {
        var r = await LoadRequestAsync(requestId);
        if (r == null) return;

        var l1 = r.Approvers.Where(a => a.StepLevel == 1).FirstOrDefault();
        if (l1 == null) return;

        var phone = await GetPhoneAsync(l1.ApproverUserId);
        if (string.IsNullOrEmpty(phone)) return;

        var lastRemark = GetLastRemark(r);
        var dateTime = FormatDateTime(r.SubmittedAt ?? r.CreatedAt);
        var raisedBy = $"{r.Requestor.FullName} ({r.Department.DeptName})";
        var priority = FormatPriority(r.Priority);
        var amount = r.Amount.HasValue ? r.Amount.Value.ToString("N0") : "0";
        var docs = await _db.RequestDocuments.AsNoTracking()
                        .Where(d => d.RequestId == requestId)
                        .OrderBy(d => d.DocumentId)
                        .ToListAsync();
        var doc = docs.FirstOrDefault(); // first doc goes in template header
        var attachmentText = docs.Any()
            ? string.Join(", ", docs.Select(d => $"📎 {d.FileName}"))
            : "No attachments";

        // Detect re-submission after clarification by checking action logs
        var isResubmit = r.ActionLogs?.Any(l => l.Action == "clarification") == true;
        var requestTypeLabel = isResubmit
            ? $"Re-submitted {r.RequestType.TypeName}"
            : r.RequestType.TypeName;

        // Pass doc so it is embedded as a header inside the template message
        // new_approval_request1 (8): {{1}}=Type {{2}}=ID {{3}}=Title {{4}}=Priority {{5}}=RaisedBy {{6}}=Description {{7}}=Amount {{8}}=LocationCode
        await SendTemplateWithButtonsAsync(phone, "new_approval_request1", new[]
        {
            requestTypeLabel,            // {{1}} Request Type
            r.RequestNumber,             // {{2}} ID
            r.Title,                     // {{3}} Title
            priority,                    // {{4}} Priority
            raisedBy,                    // {{5}} Raised By
            r.Description ?? r.Title,     // {{6}} Description
            amount,                      // {{7}} Amount
            r.Location.Code              // {{8}} Location Code
        }, requestId, l1.ApproverUserId, GetHeaderDocOrDefault(r, doc), docs);
    }

    // ── 1b. Re-submission after clarification → Approver at CurrentStepLevel ──
    // Sends to whichever level requested the clarification, not always L1.
    public async Task SendResubmitToApproverAsync(int requestId, int approverId, int stepLevel)
    {
        var r = await LoadRequestAsync(requestId);
        if (r == null) return;

        var phone = await GetPhoneAsync(approverId);
        if (string.IsNullOrEmpty(phone)) return;

        var raisedBy   = $"{r.Requestor.FullName} ({r.Department.DeptName})";
        var priority   = FormatPriority(r.Priority);
        var amount     = r.Amount.HasValue ? r.Amount.Value.ToString("N0") : "0";
        var docs = await _db.RequestDocuments.AsNoTracking()
                        .Where(d => d.RequestId == requestId)
                        .OrderBy(d => d.DocumentId)
                        .ToListAsync();
        var doc = docs.FirstOrDefault();

        // The requester's resubmission remark — their "clarification_response" action-log
        // entry, written by RequestService.SubmitClarificationResponseAsync. It gets its own
        // Remarks line ({{8}}) on this card. A resubmission can carry no remark (the
        // edit-then-submit path), and Meta rejects an empty template parameter, so it falls
        // back to a visible placeholder.
        var resubmitRemark = r.ActionLogs?
            .Where(l => l.Action == "clarification_response" && !string.IsNullOrWhiteSpace(l.Comment))
            .OrderByDescending(l => l.CreatedAt)
            .Select(l => l.Comment)
            .FirstOrDefault();
        var remarks = string.IsNullOrWhiteSpace(resubmitRemark) ? "No remarks" : resubmitRemark!.Trim();

        var latestAction = $"Resubmitted by {r.Requestor.FullName} | {FormatDateTime(IstClock.Now)}";

        _log.LogInformation("WA-RESUBMIT: RequestId={Id} StepLevel={Level} ApproverId={Aid} HasRemark={HasRemark}",
            requestId, stepLevel, approverId, !string.IsNullOrWhiteSpace(resubmitRemark));

        // request_resubmitted (10): {{1}}=Type {{2}}=ID {{3}}=Title {{4}}=Priority
        // {{5}}=RaisedBy {{6}}=Description {{7}}=LatestAction {{8}}=Remarks
        // {{9}}=Amount {{10}}=LocationCode.
        //
        // A dedicated template for resubmissions, replacing BOTH previous branches:
        // new_approval_request1 (L1) and approval_required_level2 (L2+). Those carry Meta's
        // STATIC "A new {{1}} has been submitted…", which forced the awkward "A new
        // Re-submitted Budget Approval…" — unfixable from code, since editing the shared
        // template would also change the New Request card. This template says "has been
        // Resubmitted" statically, so {{1}} is the PLAIN type name (no "Re-submitted"
        // prefix, which would now read twice), and it has no Level field, so one card
        // serves every level.
        await SendTemplateWithButtonsAsync(phone, "request_resubmitted", new[]
        {
            r.RequestType.TypeName,          // {{1}} Type (plain — "Resubmitted" is static)
            r.RequestNumber,                 // {{2}} ID
            r.Title,                         // {{3}} Title
            priority,                        // {{4}} Priority
            raisedBy,                        // {{5}} Raised By
            r.Description ?? r.Title,        // {{6}} Description — the ORIGINAL request text
            latestAction,                    // {{7}} Latest Action
            remarks,                         // {{8}} Remarks — the requester's resubmission note
            amount,                          // {{9}} Amount
            r.Location.Code                  // {{10}} Location Code
        }, requestId, approverId, GetHeaderDocOrDefault(r, doc), docs);
    }

    // ── 2. Approved → Creator + Next Approver ────────────────
    public async Task SendApprovalProgressAsync(int requestId, int actingApproverId)
    {
        var r = await LoadRequestAsync(requestId);
        if (r == null) return;

        var actingStep = r.Approvers.FirstOrDefault(a => a.ApproverUserId == actingApproverId);
        if (actingStep == null) return;

        var lastRemark = GetLastRemark(r);
        var dateTime = FormatDateTime(r.SubmittedAt ?? r.CreatedAt);
        var raisedBy = $"{r.Requestor.FullName} ({r.Department.DeptName})";
        var priority = FormatPriority(r.Priority);
        var amount = r.Amount.HasValue ? r.Amount.Value.ToString("N0") : "0";
        var docs = await _db.RequestDocuments.AsNoTracking()
                        .Where(d => d.RequestId == requestId)
                        .OrderBy(d => d.DocumentId)
                        .ToListAsync();
        var doc = docs.FirstOrDefault();
        var attachmentText = docs.Any()
            ? string.Join(", ", docs.Select(d => $"📎 {d.FileName}"))
            : "No attachments";

        _log.LogInformation("WA-PROGRESS: RequestId={Id} Status={Status} docs={Count}",
            requestId, r.Status, docs.Count);

        // Notify the requester (approval notification). ONLY the FINAL approval sends a
        // WhatsApp template — request_fully_approved — which is the genuine approval
        // notification. Intermediate (partial) approvals deliberately send NOTHING here:
        // the only "progress" template available is approval_progress_update, which in Meta
        // is the clarification-worded, buttoned "Request - Clarification Provided" card, and
        // it must NEVER be emitted by an approve. Removing the call (not guarding it after a
        // send) is the root-cause fix. On a partial approve the requester is still informed
        // by the in-app notification raised in ProcessActionAsync.
        var creatorPhone = await GetPhoneAsync(r.RequestorId);
        if (!string.IsNullOrEmpty(creatorPhone) && r.Status == "Approved")
        {
            // Final Approval — request_fully_approved (8) has a TEXT header in Meta, so no doc.
            // {{1}}=Type {{2}}=ID {{3}}=Title {{4}}=Priority {{5}}=RaisedBy {{6}}=Description {{7}}=LatestAction {{8}}=Remarks
            var latestAction = $"Approved by {actingStep.ApproverUser?.FullName ?? "Approver"} (L{actingStep.StepLevel}) | {FormatDateTime(DateTimeOffset.UtcNow)}";
            await SendTemplateAsync(creatorPhone, "request_fully_approved", new[]
            {
                r.RequestType.TypeName,      // {{1}} Type
                r.RequestNumber,             // {{2}} ID
                r.Title,                     // {{3}} Title
                priority,                    // {{4}} Priority
                raisedBy,                    // {{5}} Raised By
                r.Description ?? r.Title,     // {{6}} Description
                latestAction,                // {{7}} Latest Action
                lastRemark                   // {{8}} Latest Remarks
            }, ctx: new WaMsgContext(requestId, r.WorkflowId, "approved", $"Fully approved — {lastRemark}", actingStep.StepLevel, actingApproverId));
        }

        // ── Multi-level progression: notify the NEXT-level approver ───────────────────
        // When an intermediate level approves, ProcessActionAsync has already advanced the
        // next approver to "pending". Send THAT approver the approval request so the chain
        // continues automatically through L2, L3, … until final approval. This uses the
        // level-aware approval_required_level2 template (re-authored as a proper approval card
        // with Approve/Reject/Clarification buttons + document header), so the L2+ approver sees
        // their review level and the latest action — not the generic L1 "new request" wording.
        if (r.Status != "Approved")
        {
            var nextStep = r.Approvers
                .Where(a => a.StepLevel > actingStep.StepLevel && a.Status == "pending")
                .OrderBy(a => a.StepLevel).FirstOrDefault()
                ?? r.Approvers
                   .Where(a => a.StepLevel > actingStep.StepLevel)
                   .OrderBy(a => a.StepLevel).FirstOrDefault();

            _log.LogInformation(
                "WA-PROGRESS: RequestId={Id} CurrentLevel=L{CurLvl} CurrentApprover={CurAppr} NextLevel={NextLvl} NextApprover={NextAppr}",
                requestId, actingStep.StepLevel, actingApproverId,
                nextStep is null ? "none" : $"L{nextStep.StepLevel}",
                nextStep?.ApproverUserId.ToString() ?? "none");

            if (nextStep != null)
            {
                var nextPhone = await GetPhoneAsync(nextStep.ApproverUserId);
                if (string.IsNullOrEmpty(nextPhone))
                {
                    _log.LogWarning("WA-PROGRESS: L{Lvl} approver (User={Uid}) has no WhatsApp number — cannot send approval request. RequestId={Id}",
                        nextStep.StepLevel, nextStep.ApproverUserId, requestId);
                }
                else
                {
                    // approval_required_level2 (11): {{1}}=Type {{2}}=Level {{3}}=ID {{4}}=Title
                    // {{5}}=Priority {{6}}=RaisedBy {{7}}=Description {{8}}=LatestAction
                    // {{9}}=LatestRemarks {{10}}=Amount {{11}}=LocationCode.
                    // Sent to the next-level (L2+) approver so the card is level-aware; it carries the
                    // same Approve/Reject/Clarification buttons + document header as new_approval_request1.
                    var progressAction = $"Approved by {actingStep.ApproverUser?.FullName ?? "Approver"} (L{actingStep.StepLevel}) | {FormatDateTime(DateTimeOffset.UtcNow)}";
                    var sent = await SendTemplateWithButtonsAsync(nextPhone, "approval_required_level2", new[]
                    {
                        r.RequestType.TypeName,       // {{1}} Type
                        nextStep.StepLevel.ToString(),// {{2}} Level (the reviewing approver's level)
                        r.RequestNumber,              // {{3}} ID
                        r.Title,                      // {{4}} Title
                        priority,                     // {{5}} Priority
                        raisedBy,                     // {{6}} Raised By
                        r.Description ?? r.Title,      // {{7}} Description
                        progressAction,               // {{8}} Latest Action
                        lastRemark,                   // {{9}} Latest Remarks
                        amount,                       // {{10}} Amount
                        r.Location.Code               // {{11}} Location Code
                    }, requestId, nextStep.ApproverUserId, GetHeaderDocOrDefault(r, doc), docs);

                    _log.LogInformation("WA-PROGRESS: L{Lvl} approval request to User={Uid} Phone={Phone} Sent={Sent} RequestId={Id}",
                        nextStep.StepLevel, nextStep.ApproverUserId, nextPhone, sent, requestId);
                }
            }
        }
    }

    // ── 3. Clarification → Creator ────────────────────────────
    public async Task SendClarificationToCreatorAsync(int requestId, int actingApproverId, string comment)
    {
        var r = await LoadRequestAsync(requestId);
        if (r == null) return;

        var actingStep = r.Approvers.FirstOrDefault(a => a.ApproverUserId == actingApproverId);
        var approverName = $"{actingStep?.ApproverUser?.FullName ?? "Approver"} (L{actingStep?.StepLevel} Approver)";

        var creatorPhone = await GetPhoneAsync(r.RequestorId);
        if (string.IsNullOrEmpty(creatorPhone)) return;

        var dateTime = FormatDateTime(r.SubmittedAt ?? r.CreatedAt);
        var raisedBy = $"{r.Requestor.FullName} ({r.Department.DeptName})";
        var priority = FormatPriority(r.Priority);
        var amount = r.Amount.HasValue ? r.Amount.Value.ToString("N0") : "0";
        var remark = $"{comment} — {approverName}";

        // clarification_needed template has a TEXT header (not Document) in Meta —
        // do NOT pass any doc/attachment here or Meta rejects with error 132012.
        // Requestor just needs the updated remark, not the file again.

        // Use Case 3: Clarification — clarification_needed (8)
        // {{1}}=Type {{2}}=ID {{3}}=Title {{4}}=Priority {{5}}=RaisedBy {{6}}=Justification {{7}}=LatestAction {{8}}=Remarks
        var latestAction = $"Clarification Requested by {approverName} | {FormatDateTime(DateTimeOffset.UtcNow)}";
        await SendTemplateAsync(creatorPhone, "clarification_needed", new[]
        {
            r.RequestType.TypeName,      // {{1}} Type
            r.RequestNumber,             // {{2}} ID
            r.Title,                     // {{3}} Title
            priority,                    // {{4}} Priority
            raisedBy,                    // {{5}} Raised By
            r.Description ?? r.Title,     // {{6}} Justification
            latestAction,                // {{7}} Latest Action
            comment                      // {{8}} Latest Remarks
        }, ctx: new WaMsgContext(requestId, r.WorkflowId, "clarification", remark, actingStep?.StepLevel, actingApproverId));
    }

    // ── 4. Rejection → Creator ────────────────────────────────
    public async Task SendRejectionToCreatorAsync(int requestId, int actingApproverId, string comment)
    {
        var r = await LoadRequestAsync(requestId);
        if (r == null) return;

        var actingStep = r.Approvers.FirstOrDefault(a => a.ApproverUserId == actingApproverId);
        var approverName = $"{actingStep?.ApproverUser?.FullName ?? "Approver"} (L{actingStep?.StepLevel} Approver)";

        var creatorPhone = await GetPhoneAsync(r.RequestorId);
        if (string.IsNullOrEmpty(creatorPhone)) return;

        var dateTime = FormatDateTime(r.SubmittedAt ?? r.CreatedAt);
        var raisedBy = $"{r.Requestor.FullName} ({r.Department.DeptName})";
        var priority = FormatPriority(r.Priority);
        var amount = r.Amount.HasValue ? r.Amount.Value.ToString("N0") : "0";
        var remark = $"{comment} — {approverName}";
        var docsRej = await _db.RequestDocuments.AsNoTracking()
            .Where(d => d.RequestId == requestId).OrderBy(d => d.DocumentId).ToListAsync();
        var attachmentText = docsRej.Any()
            ? string.Join(", ", docsRej.Select(d => $"📎 {d.FileName}"))
            : "No attachments";

        // Use Case 6: Rejected — request_rejected (8) has a TEXT header in Meta — do NOT attach a
        // document or Meta rejects the send with 132012 when the request has an attachment.
        // {{1}}=Type {{2}}=Status {{3}}=ID {{4}}=Title {{5}}=Priority {{6}}=RaisedBy {{7}}=Description {{8}}=Remarks
        var maxLevel = r.Approvers.Any() ? r.Approvers.Max(a => a.StepLevel) : (actingStep?.StepLevel ?? 1);
        var rejectStatus = (actingStep?.StepLevel ?? 0) >= maxLevel ? "Rejected" : $"Rejected at Level {actingStep?.StepLevel}";
        await SendTemplateAsync(creatorPhone, "request_rejected", new[]
        {
            r.RequestType.TypeName,      // {{1}} Type
            rejectStatus,                // {{2}} Status
            r.RequestNumber,             // {{3}} ID
            r.Title,                     // {{4}} Title
            priority,                    // {{5}} Priority
            raisedBy,                    // {{6}} Raised By
            r.Description ?? r.Title,     // {{7}} Description
            comment                      // {{8}} Latest Remarks
        }, ctx: new WaMsgContext(requestId, r.WorkflowId, "rejected", remark, actingStep?.StepLevel, actingApproverId));
    }

    // ── 5. Action Reply Prompt → Approver ────────────────────
    // Opens the "reply screen" for any workflow action (Approve / Reject / Clarification)
    // at any level. The approver's next text reply is captured by the webhook and routed
    // to ProcessActionAsync via the pending record's Action.
    public async Task SendActionPromptAsync(string toPhone, int requestId, int approverId, string action)
    {
        if (requestId == 0) return; // confirmation call, skip

        var r = await LoadRequestAsync(requestId);
        if (r == null) return;

        var level = r.Approvers.FirstOrDefault(a => a.ApproverUserId == approverId)?.StepLevel;
        var reqLabel = $"*{r.RequestNumber}*-{r.Title}";

        var body = action switch
        {
            "approved" =>
                $"✅ You have approved ({reqLabel}) this request. Type an optional message to notify the requester.",
            "rejected" =>
                $"❌ You have rejected ({reqLabel}) this request. Please type a reason for rejection to notify the requester.",
            _ =>
                $"💬 You have sent ({reqLabel}) this request back for clarification. Type a message to notify the requester."
        };

        await SendTextAsync(toPhone, body,
            ctx: new WaMsgContext(requestId, r.WorkflowId, $"prompt:{action}", body, level, approverId));
    }

    // ── 5b. Deferred-Approve Reply Prompt → Approver ─────────
    // Opens the optional-remark window after an APPROVE tap. Returns the wamid of the
    // prompt so the caller can store it on that request's pending session: a swipe-reply
    // arrives carrying this id as context.id, which is what resolves the reply back to
    // THIS request when several approve sessions are open at once. Approve-only —
    // Reject / Clarification keep using SendActionPromptAsync unchanged.
    public async Task<string?> SendApproveReplyPromptAsync(string toPhone, int requestId, int approverId, int windowSeconds)
    {
        if (requestId == 0) return null;

        var r = await LoadRequestAsync(requestId);
        if (r == null) return null;

        var level = r.Approvers.FirstOrDefault(a => a.ApproverUserId == approverId)?.StepLevel;
        var reqLabel = $"*{r.RequestNumber}*-{r.Title}";

        var body =
            $"✅ Approval selected for ({reqLabel}).\n\n" +
            $"Reply to this message within {windowSeconds} seconds to add an optional remark for the requester.\n\n" +
            "If no reply is received, the request will be approved automatically.";

        return await SendTextAsync(toPhone, body,
            ctx: new WaMsgContext(requestId, r.WorkflowId, "prompt:approved", body, level, approverId));
    }

    // ── Notification Alert → Observer Role Users ──────────────
    // Sent as the APPROVED "notification_alert" Meta template, NOT as free-form text.
    // This is the whole point: WhatsApp only delivers free-form messages inside the
    // 24-hour customer-service window, which opens when the recipient messages the
    // business. Observers never message the bot, so their window is always shut and a
    // text alert is accepted by the Graph API (200 + wamid) yet never delivered —
    // indistinguishable from success. A template has no such restriction and always
    // reaches the recipient.
    //
    // notification_alert (9): {{1}}=ID {{2}}=Title {{3}}=Priority {{4}}=RaisedBy
    // {{5}}=Description {{6}}=LatestAction {{7}}=Remarks {{8}}=In-Office/HO link
    // {{9}}=Out-of-Office/Mobile link. Every parameter must be non-empty — Meta rejects
    // a blank one and the whole send fails — so each falls back to a placeholder.
    public async Task SendNotificationRoleAlertAsync(
        int requestId, int userId, string eventLabel, string inOfficeUrl, string outOfOfficeUrl, int approvedLevel)
    {
        var r = await LoadRequestAsync(requestId);
        if (r == null) return;

        var phone = await GetPhoneAsync(userId);
        if (string.IsNullOrEmpty(phone)) return;

        var priority = FormatPriority(r.Priority);
        var raisedBy = $"{r.Requestor.FullName} ({r.Department.DeptName}) {FormatDateTime(r.SubmittedAt ?? r.CreatedAt)}";

        // Latest action = the most recent action-log entry (this alert only fires on approve).
        var lastLog = r.ActionLogs?.OrderByDescending(l => l.CreatedAt).FirstOrDefault();
        var actor = lastLog?.ActorUser?.FullName ?? "Approver";
        var actionDate = FormatDateTime(lastLog?.CreatedAt ?? DateTimeOffset.UtcNow);
        var latestAction = $"Approved by {actor} | {actionDate}";
        var remarks = string.IsNullOrWhiteSpace(lastLog?.Comment) ? "No remarks" : lastLog!.Comment;

        await SendTemplateAsync(phone, "notification_alert", new[]
        {
            Param(r.RequestNumber,            "-"),
            Param(r.Title,                    "-"),
            Param(priority,                   "-"),
            Param(raisedBy,                   "-"),
            Param(r.Description ?? r.Title,   "-"),
            Param(latestAction,               "-"),
            Param(remarks,                    "No remarks"),
            Param(inOfficeUrl,                "(link unavailable)"),
            Param(outOfOfficeUrl,             "(link unavailable)")
        },
        ctx: new WaMsgContext(requestId, r.WorkflowId, "template:notification_alert", $"Notification Alert: {r.RequestNumber}", approvedLevel, userId));

        _log.LogInformation("WA-ALERT: notification_alert template sent RequestId={Id} UserId={Uid} Level={Lvl} Event={Event}",
            requestId, userId, approvedLevel, eventLabel);
    }

    // Meta rejects an empty template parameter and fails the entire message, so every
    // body value gets a visible placeholder rather than being sent blank.
    private static string Param(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;

    // ── 6. Pending Approval Reminder ──────────────────────────
    // Daily nudge for every approver who still has requests waiting on them. The message
    // summarises those requests grouped by Request Type and carries a deep link straight into
    // the EXISTING Pending Approval page (/Requests/Approval?status=Pending) — never the
    // dashboard, a KPI card or the home page. Approvers with nothing pending are skipped, and
    // the counts come from IPendingApprovalSummaryService, which uses the same filter as the
    // page the link opens, so the numbers can never disagree.
    public async Task SendAgingReminderAsync()
    {
        var summaries = await _pending.GetAllAsync();
        if (summaries.Count == 0) return;

        // WhatsApp:PendingReminderTemplate = the single approved Meta template used for the
        // reminder (pending_approval_summary). A template reaches EVERY approver regardless of the
        // 24-hour window. ONLY this template is sent — there is deliberately no free-form or
        // secondary-template fallback; if it is not accepted this slot, the reminder self-heals at
        // the next slot (recomputed from current pending state).
        var summaryTemplate = _cfg["WhatsApp:PendingReminderTemplate"];
        var (inOffice, outOfOffice) = _url.GetPendingApprovalsUrlPair();

        // Rule 10 tallies for the end-of-run line: how many approvers were evaluated this slot,
        // how many actually received a reminder, and how many were skipped.
        int evaluated = 0, sentCount = 0, skippedCount = 0;

        foreach (var s in summaries)
        {
            evaluated++;

            // Rules 2–4: only approvers with at least one CURRENT pending request are reminded;
            // a zero count is skipped silently (the summary query already excludes them, so this
            // is a defensive guard). Recorded as an audit line rather than dropped invisibly.
            if (s.Total == 0)
            {
                skippedCount++;
                LogReminderResult(s, sent: false, reason: "No Pending Requests");
                continue;
            }

            var phone = await GetPhoneAsync(s.ApproverUserId);
            if (string.IsNullOrEmpty(phone))
            {
                skippedCount++;
                LogReminderResult(s, sent: false, reason: "No WhatsApp number");
                continue;
            }

            // ONLY the pending_approval_summary template is sent — no free-form or secondary
            // template fallback. If Meta does not accept it this slot, the reminder is retried at
            // the next scheduled slot (it is recomputed from current pending state).
            bool sent = false;
            if (!string.IsNullOrWhiteSpace(summaryTemplate))
            {
                // pending_approval_summary (5): {{1}}=Approver  {{2}}=Total  {{3}}=Summary
                // {{4}}=In-Office link  {{5}}=Out-of-Office link. The per-type breakdown is one
                // parameter because a template body is fixed while the number of request types
                // is not, and a parameter may not carry new-lines (#132018).
                sent = await SendTemplateAsync(phone, summaryTemplate, new[]
                {
                    s.ApproverName,
                    s.Total.ToString(),
                    BuildPendingSummaryLine(s),
                    inOffice,
                    outOfOffice
                },
                ctx: new WaMsgContext(null, null, $"template:{summaryTemplate}",
                    $"Pending Approval Reminder: {s.Total} pending", null, s.ApproverUserId));

                if (!sent)
                    _log.LogWarning("WA-REMINDER: template {Template} not accepted for UserId={Uid} — will retry at next slot.",
                        summaryTemplate, s.ApproverUserId);
            }
            else
            {
                _log.LogWarning("WA-REMINDER: WhatsApp:PendingReminderTemplate not configured — nothing sent for UserId={Uid}.",
                    s.ApproverUserId);
            }

            if (sent) sentCount++; else skippedCount++;
            LogReminderResult(s, sent, sent ? "Reminder Sent" : "Send Failed");
        }

        _log.LogInformation(
            "WA-REMINDER: run complete — approvers evaluated={Evaluated}, reminders sent={Sent}, skipped={Skipped}.",
            evaluated, sentCount, skippedCount);
    }

    // Rule 10: one structured audit line per approver per scheduler run, capturing the approver,
    // their current pending count, whether a reminder went out, and why. Written at Information
    // level so a run's outcome is auditable from the logs without changing any send behaviour.
    private void LogReminderResult(PendingApprovalSummary s, bool sent, string reason)
        => _log.LogInformation(
            "WA-REMINDER-AUDIT: Approver='{Approver}' (UserId={UserId}) PendingCount={PendingCount} ReminderSent={Sent} Reason='{Reason}'",
            s.ApproverName, s.ApproverUserId, s.Total, sent ? "Yes" : "No", reason);

    // The same per-request-type breakdown as one single-line string, for the Meta template's
    // {{3}}. Uses "•" separators because new-lines are not allowed inside a parameter.
    private static string BuildPendingSummaryLine(PendingApprovalSummary s)
        => string.Join(" • ", s.ByType.Select(t => $"{RequestTypeIcon(t.TypeName)} {t.TypeName} : {t.Count}"));

    // Visual cue per request type. Types are master data that admins can add at any time, so
    // the match is by keyword with 📄 as the catch-all — a new type never breaks the message.
    private static string RequestTypeIcon(string typeName)
    {
        var n = (typeName ?? "").ToLowerInvariant();
        var words = n.Split(new[] { ' ', '-', '_', '/', '&', ',' }, StringSplitOptions.RemoveEmptyEntries);

        if (words.Contains("it") || n.Contains("software") || n.Contains("hardware") ||
            n.Contains("system") || n.Contains("laptop") || n.Contains("asset")) return "💻";
        if (n.Contains("cash") || n.Contains("payment") || n.Contains("expense") ||
            n.Contains("fund") || n.Contains("advance") || n.Contains("reimburse") ||
            n.Contains("budget")) return "💰";
        if (n.Contains("purchase") || n.Contains("procure") || n.Contains("material") ||
            n.Contains("vendor") || n.Contains("indent")) return "📦";
        if (n.Contains("travel") || n.Contains("trip") || n.Contains("tour")) return "✈️";
        if (n.Contains("hiring") || n.Contains("recruit") || n.Contains("manpower") ||
            n.Contains("candidate") || n.Contains("onboard")) return "👤";
        return "📄";
    }

    // ── Send Template WITH 3 Buttons ─────────────────────────
    // Sends template (body + buttons), then file as separate message for all formats.
    private async Task<bool> SendTemplateWithButtonsAsync(
        string toPhone, string templateName, string[] parameters,
        int requestId, int approverId, RequestDocument? doc = null,
        IList<RequestDocument>? allDocs = null, WaMsgContext? ctx = null)
    {
        var token = _cfg["WhatsApp:BearerToken"];
        var phoneNumId = _cfg["WhatsApp:PhoneNumberId"];
        var apiVersion = _cfg["WhatsApp:ApiVersion"] ?? "v19.0";
        var language = _cfg["WhatsApp:TemplateLanguage"] ?? "en";

        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(phoneNumId)) return false;

        var normalised = NormalisePhone(toPhone);
        var effectiveCtx = ctx ?? new WaMsgContext(requestId, null, $"template:{templateName}", "", null, approverId);

        // Upload doc to get media_id — embed as header inside template. The upload is now resilient
        // (named client + retry inside UploadMediaAsync): a header upload that failed on a transient
        // TLS reset used to leave mediaId null, so the template went out HEADERLESS and Meta rejected
        // it with 132012 — the exact "no-attachment message not sending" symptom.
        string? mediaId = null;
        if (doc != null)
        {
            var fullPath = System.IO.Path.Combine(GetStoragePath(), doc.StoredFileName);
            if (File.Exists(fullPath))
                mediaId = await UploadMediaAsync(phoneNumId, apiVersion, fullPath,
                    doc.FileName, doc.ContentType ?? GetMimeType(doc.FileName), token);
            else
                _log.LogWarning("WA-DOC: File not found {Path}", fullPath);

            // A required header that still could not be uploaded must NOT be dropped: sending the
            // template without it is rejected by Meta (132012). Fail cleanly and record it instead
            // of delivering a broken headerless message.
            if (mediaId == null)
            {
                _log.LogError("WA-DOC: header upload unavailable for RequestId={Req} — not sending headerless template '{Tpl}'.",
                    requestId, templateName);
                await SaveMessageHistoryAsync(effectiveCtx, normalised, "Outbound", null, "Failed", "Header document upload failed");
                return false;
            }
        }

        var components = new List<object>();
        if (mediaId != null)
        {
            components.Add(new
            {
                type = "header",
                parameters = new[] { new { type = "document", document = new { id = mediaId, filename = SanitizeParam(doc!.FileName) } } }
            });
        }
        components.Add(new { type = "body", parameters = parameters.Select(p => new { type = "text", text = SanitizeParam(p) }).ToArray() });
        components.Add(new
        {
            type = "button",
            sub_type = "quick_reply",
            index = "0",
            parameters = new[] { new { type = "payload", payload = $"APPROVE__{requestId}__{approverId}" } }
        });
        components.Add(new
        {
            type = "button",
            sub_type = "quick_reply",
            index = "1",
            parameters = new[] { new { type = "payload", payload = $"REJECT__{requestId}__{approverId}" } }
        });
        components.Add(new
        {
            type = "button",
            sub_type = "quick_reply",
            index = "2",
            parameters = new[] { new { type = "payload", payload = $"CLARIFICATION__{requestId}__{approverId}" } }
        });

        var payload = new
        {
            messaging_product = "whatsapp",
            to = normalised,
            type = "template",
            template = new
            {
                name = templateName,
                language = new { code = language },
                components = components.ToArray()
            }
        };

        // Attribute the outbound approval notification to the approver it was sent to
        // (CreatedBy = approverId). This is what lets an incoming REPLY-COMMAND resolve
        // context.id → this history row → RequestId + ApproverId, so the reply can be
        // routed to the correct approval. Does not affect the interactive-button flow.
        var (ok, _) = await PostToWhatsAppAsync(normalised, payload, token, phoneNumId, apiVersion, effectiveCtx);

        // Send remaining documents (2nd, 3rd...) as separate messages after template
        if (allDocs != null && allDocs.Count > 1)
        {
            foreach (var extraDoc in allDocs.Skip(1))
                await SendDocumentAsync(normalised, extraDoc);
        }

        return ok;
    }

    // Uploads a document to the Graph API media endpoint and returns its media_id (null on
    // failure). Uses the resilient named "WhatsApp" client (connection recycling) and retries the
    // TLS resets that were leaving header uploads null — the root cause of the headerless 132012.
    // A non-2xx (real rejection: bad media/auth) is not retried.
    private async Task<string?> UploadMediaAsync(string phoneNumId, string apiVersion,
        string fullPath, string fileName, string mimeType, string token)
    {
        const int maxAttempts = 3;
        var url = $"https://graph.facebook.com/{apiVersion}/{phoneNumId}/media";
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var fileBytes = await File.ReadAllBytesAsync(fullPath);
                var client = _http.CreateClient("WhatsApp");
                client.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                using var form = new MultipartFormDataContent();
                form.Add(new StringContent("whatsapp"), "messaging_product");
                var fc = new ByteArrayContent(fileBytes);
                fc.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mimeType);
                form.Add(fc, "file", fileName);

                var resp = await client.PostAsync(url, form);
                var body = await resp.Content.ReadAsStringAsync();
                _log.LogInformation("WA-DOC: Upload {Code}: {Body}", (int)resp.StatusCode, body);
                if (resp.IsSuccessStatusCode)
                {
                    using var j = System.Text.Json.JsonDocument.Parse(body);
                    var id = j.RootElement.GetProperty("id").GetString();
                    _log.LogInformation("WA-DOC: media_id={Id} file={File}", id, fileName);
                    return id;
                }
                return null; // real rejection — retrying won't help
            }
            catch (HttpRequestException ex) when (attempt < maxAttempts)
            {
                var delayMs = 500 * attempt;
                _log.LogWarning(ex, "WA-DOC: media upload transport failure for {File} (attempt {Attempt}/{Max}); retrying in {Delay}ms.",
                    fileName, attempt, maxAttempts, delayMs);
                await Task.Delay(delayMs);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "WA-DOC: media upload failed for {File}.", fileName);
                return null;
            }
        }
    }

    // ── Send Template WITHOUT Buttons ─────────────────────────
    // Sends template (body only), then file as separate message for all formats.
    // Returns whether Meta accepted the send, so callers that have a second option (e.g. the
    // pending reminder) can fall back instead of silently dropping the message. Existing
    // callers ignore the result and are unaffected.
    private async Task<bool> SendTemplateAsync(string toPhone, string templateName, string[] parameters,
        RequestDocument? doc = null, IList<RequestDocument>? allDocs = null, WaMsgContext? ctx = null)
    {
        var token = _cfg["WhatsApp:BearerToken"];
        var phoneNumId = _cfg["WhatsApp:PhoneNumberId"];
        var apiVersion = _cfg["WhatsApp:ApiVersion"] ?? "v19.0";
        var language = _cfg["WhatsApp:TemplateLanguage"] ?? "en";

        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(phoneNumId)) return false;

        var normalised = NormalisePhone(toPhone);

        // Upload doc to get media_id — embed as header inside template (resilient upload; header
        // added only when a doc is supplied and the upload succeeds — behaviour unchanged).
        string? mediaId = null;
        if (doc != null)
        {
            var fullPath = System.IO.Path.Combine(GetStoragePath(), doc.StoredFileName);
            if (File.Exists(fullPath))
                mediaId = await UploadMediaAsync(phoneNumId, apiVersion, fullPath,
                    doc.FileName, doc.ContentType ?? GetMimeType(doc.FileName), token);
            else
                _log.LogWarning("WA-DOC: File not found {Path}", fullPath);
        }

        var components = new List<object>();
        if (mediaId != null)
        {
            components.Add(new
            {
                type = "header",
                parameters = new[] { new { type = "document", document = new { id = mediaId, filename = SanitizeParam(doc!.FileName) } } }
            });
        }
        components.Add(new { type = "body", parameters = parameters.Select(p => new { type = "text", text = SanitizeParam(p) }).ToArray() });

        var payload = new
        {
            messaging_product = "whatsapp",
            to = normalised,
            type = "template",
            template = new
            {
                name = templateName,
                language = new { code = language },
                components = components.ToArray()
            }
        };

        var (ok, _) = await PostToWhatsAppAsync(normalised, payload, token, phoneNumId, apiVersion,
            ctx ?? new WaMsgContext(null, null, $"template:{templateName}", ""));

        // Send remaining documents (2nd, 3rd...) as separate messages after template
        if (allDocs != null && allDocs.Count > 1)
        {
            foreach (var extraDoc in allDocs.Skip(1))
                await SendDocumentAsync(normalised, extraDoc);
        }

        return ok;
    }

    // Public plain-text confirmation sender (e.g. the deferred-approve timeout notice
    // fired from the background sweep). Wraps the internal text sender; like the webhook's
    // own confirmation messages, these transient notices are not written to history.
    public Task SendPlainTextAsync(string toPhone, string message) => SendTextAsync(toPhone, message);

    // ── Send Plain Text ───────────────────────────────────────
    // Returns the wamid of the sent message (null when not sent), so callers that need
    // to correlate a later swipe-reply back to this exact message can store it.
    private async Task<string?> SendTextAsync(string toPhone, string message, bool previewUrl = false, WaMsgContext? ctx = null)
    {
        var token = _cfg["WhatsApp:BearerToken"];
        var phoneNumId = _cfg["WhatsApp:PhoneNumberId"];
        var apiVersion = _cfg["WhatsApp:ApiVersion"] ?? "v19.0";

        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(phoneNumId)) return null;

        var normalised = NormalisePhone(toPhone);
        var payload = new
        {
            messaging_product = "whatsapp",
            to = normalised,
            type = "text",
            text = new { preview_url = previewUrl, body = message }
        };

        var (_, messageId) = await PostToWhatsAppAsync(normalised, payload, token, phoneNumId, apiVersion,
            ctx ?? new WaMsgContext(null, null, "text", message));
        return messageId;
    }

    // ── Phone normalisation for the WhatsApp Cloud API ────────
    // Meta needs the full international number (country code + subscriber number) and
    // digits only. It ACCEPTS a bare national number — returning 200 and a wamid — but
    // never delivers it, so a missing country code looks like a successful send that the
    // recipient never gets. Numbers are entered by hand in the Users screen, so they
    // arrive here in mixed shapes: "+91 98765-43210", "09876543210", "9876543210".
    // Anything already carrying a country code (longer than the national length) is left
    // exactly as-is, so this cannot corrupt a correct number.
    private string NormalisePhone(string toPhone)
    {
        var digits = new string((toPhone ?? "").Where(char.IsDigit).ToArray());
        if (digits.Length == 0) return digits;

        var cc = _cfg["WhatsApp:DefaultCountryCode"] ?? "91";
        var nationalLen = int.TryParse(_cfg["WhatsApp:NationalNumberLength"], out var nl) ? nl : 10;

        // Trunk prefix: "0" before the national number (e.g. 09876543210).
        if (digits.Length == nationalLen + 1 && digits.StartsWith("0"))
            digits = digits.Substring(1);

        // Bare national number → prepend the country code.
        if (digits.Length == nationalLen)
            digits = cc + digits;

        return digits;
    }

    // Lightweight context so a single save path can record who/what a message was about.
    // Passed through the send wrappers to PostToWhatsAppAsync where the API response
    // (message id + status) becomes available. Null = don't log (e.g. raw doc uploads).
    private sealed record WaMsgContext(int? RequestId, int? WorkflowId, string MessageType, string MessageText, int? WorkflowLevel = null, int? CreatedBy = null);

    // ── Core HTTP Post ────────────────────────────────────────
    // Returns the send result and the wamid Meta assigned to the message (null when the
    // send failed or the response carried no id).
    private async Task<(bool ok, string? messageId)> PostToWhatsAppAsync(string normalised, object payload, string token, string phoneNumId, string apiVersion, WaMsgContext? ctx = null)
    {
        var json = JsonSerializer.Serialize(payload);

        var client = _http.CreateClient("WhatsApp");
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var url = $"https://graph.facebook.com/{apiVersion}/{phoneNumId}/messages";

        // A dropped TLS connection to graph.facebook.com surfaces as HttpRequestException and is
        // thrown during the handshake — before any request bytes reach Meta — so retrying it can
        // never double-send. Only that transport failure is retried; a non-2xx response is a real
        // API rejection (bad template, param, etc.) and is returned immediately, never retried.
        // Just ONE quick inline retry (after 1s) so the caller isn't blocked: anything still
        // failing is handed to the durable WhatsAppOutbox, which resends in the background and
        // rides out outages lasting minutes without holding up the user's action.
        const int maxAttempts = 2;
        for (var attempt = 1; ; attempt++)
        {
            // StringContent is single-use (consumed by PostAsync), so rebuild it each attempt.
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            try
            {
                var resp = await client.PostAsync(url, content);
                var body = await resp.Content.ReadAsStringAsync();
                var ok = resp.IsSuccessStatusCode;
                if (!ok)
                    _log.LogError("WhatsApp API error {Code}: {Body} | Payload sent: {Payload}", (int)resp.StatusCode, body, json);
                else
                    _log.LogInformation("WhatsApp message sent to {Phone}{Attempt}.", normalised,
                        attempt > 1 ? $" (attempt {attempt})" : "");

                var messageId = ExtractMessageId(ok ? body : null);

                // Central outbound history save — every Approve / Reject / Clarification
                // (and other) notification funnels through here, so there is exactly one
                // save path and no per-action duplication.
                if (ctx != null)
                    await SaveMessageHistoryAsync(ctx, normalised, "Outbound", messageId, ok ? "Sent" : "Failed",
                        ok ? null : BuildErrorSummary((int)resp.StatusCode, body));

                return (ok, messageId);
            }
            catch (HttpRequestException ex) when (attempt < maxAttempts)
            {
                // Transient transport failure (connection forcibly closed / TLS reset while
                // connecting). Back off and retry — Meta received nothing. Exponential:
                // attempt 1→1s, 2→2s, 3→4s, 4→8s (capped at 8s) to span a longer bad window.
                var delayMs = Math.Min(8000, 1000 * (int)Math.Pow(2, attempt - 1));
                _log.LogWarning(ex, "WhatsApp send transport failure for {Phone} (attempt {Attempt}/{Max}); retrying in {Delay}ms.",
                    normalised, attempt, maxAttempts, delayMs);
                await Task.Delay(delayMs);
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "WhatsApp send failed for {Phone} after {Attempt} attempt(s).", normalised, attempt);
                if (ctx != null)
                {
                    await SaveMessageHistoryAsync(ctx, normalised, "Outbound", null, "Failed", DescribeException(ex));
                    // A transport failure (connection reset) never reached Meta — queue it for the
                    // background outbox to resend once connectivity recovers. Non-transport errors
                    // are not retryable and are left as a plain Failed record. Reminders are excluded:
                    // they are recomputed from current pending state every slot (self-healing) and try
                    // three fallbacks per slot, so queuing them would deliver stale DUPLICATES on top
                    // of the next slot's fresh reminder.
                    if (ex is HttpRequestException && !IsReminderMessage(ctx.MessageType))
                        await EnqueueOutboxAsync(ctx, normalised, phoneNumId, apiVersion, json);
                }
                return (false, null);
            }
        }
    }

    // True for the daily pending-approval reminder message types. These are NOT queued to the
    // outbox: the reminder is rebuilt from current pending state every slot (08:59/13:00/18:00)
    // and already tries three fallbacks per slot, so a background resend would deliver stale
    // duplicates. The summary template name is configuration-driven (WhatsApp:PendingReminderTemplate).
    private bool IsReminderMessage(string messageType)
    {
        if (messageType == "pending_reminder" || messageType == "template:pending_approval_reminder")
            return true;
        var summaryTemplate = _cfg["WhatsApp:PendingReminderTemplate"];
        return !string.IsNullOrWhiteSpace(summaryTemplate)
            && messageType == $"template:{summaryTemplate}";
    }

    // ── Outbox: durable background resend for transport failures ──────────────────────
    // Enqueues the exact payload of a send that died at the transport layer so the background
    // WhatsAppOutboxService can re-POST it verbatim once connectivity recovers. Never throws —
    // a queue failure must not break the caller.
    private async Task EnqueueOutboxAsync(WaMsgContext ctx, string toNumber, string phoneNumId, string apiVersion, string json)
    {
        try
        {
            var now = IstClock.Now;
            _db.WhatsAppOutbox.Add(new WhatsAppOutbox
            {
                CreatedDate   = now,
                PhoneNumberId = phoneNumId,
                ApiVersion    = apiVersion,
                ToNumber      = toNumber,
                PayloadJson   = json,
                MessageType   = ctx.MessageType,
                RequestId     = ctx.RequestId,
                WorkflowId    = ctx.WorkflowId,
                WorkflowLevel = ctx.WorkflowLevel,
                CreatedBy     = ctx.CreatedBy,
                MessageText   = ctx.MessageText,
                Status        = "Pending",
                Attempts      = 0,
                NextAttemptAt = now.AddSeconds(60)
            });
            await _db.SaveChangesAsync();
            _log.LogInformation("WA-OUTBOX: queued type={Type} to={To} for background resend.", ctx.MessageType, toNumber);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "WA-OUTBOX: failed to enqueue type={Type} to={To}", ctx.MessageType, toNumber);
        }
    }

    // One raw POST attempt for the outbox resender: no inline retry (the outbox IS the retry) and
    // no history write (the caller owns history + outbox state). A transport failure throws
    // HttpRequestException; a non-2xx is returned as ok=false with the status/body.
    private async Task<(bool ok, string? wamid, int status, string? body)> RawPostOnceAsync(string url, string json, string token, CancellationToken ct)
    {
        var client = _http.CreateClient("WhatsApp");
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        var resp = await client.PostAsync(url, content, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        var ok = resp.IsSuccessStatusCode;
        return (ok, ok ? ExtractMessageId(body) : null, (int)resp.StatusCode, body);
    }

    // Processes one batch of due outbox rows. Called on a timer by WhatsAppOutboxService.
    // Each row is saved individually right after its attempt so an already-delivered message is
    // never resent because of a later failure in the same batch.
    public async Task ProcessOutboxOnceAsync(CancellationToken ct = default)
    {
        var token = _cfg["WhatsApp:BearerToken"];
        if (string.IsNullOrEmpty(token)) return;

        var maxAttempts = _cfg.GetValue<int>("WhatsApp:OutboxMaxAttempts", 8);
        var now = IstClock.Now;

        var batch = await _db.WhatsAppOutbox
            .Where(o => o.Status == "Pending" && o.NextAttemptAt <= now)
            .OrderBy(o => o.NextAttemptAt)
            .Take(20)
            .ToListAsync(ct);
        if (batch.Count == 0) return;

        foreach (var o in batch)
        {
            if (ct.IsCancellationRequested) break;

            var url = $"https://graph.facebook.com/{o.ApiVersion}/{o.PhoneNumberId}/messages";
            try
            {
                var (ok, wamid, status, body) = await RawPostOnceAsync(url, o.PayloadJson, token, ct);
                if (ok)
                {
                    o.Status = "Sent";
                    // Record the delivered message exactly as a first-attempt send would.
                    _db.WhatsAppMessageHistories.Add(new WhatsAppMessageHistory
                    {
                        RequestId     = o.RequestId,
                        WorkflowId    = o.WorkflowId,
                        WorkflowLevel = o.WorkflowLevel,
                        CreatedBy     = o.CreatedBy,
                        MessageId     = wamid,
                        MobileNumber  = o.ToNumber,
                        Direction     = "Outbound",
                        MessageType   = o.MessageType,
                        MessageText   = o.MessageText,
                        Status        = "Sent",
                        CreatedDate   = IstClock.Now
                    });
                    _log.LogInformation("WA-OUTBOX: delivered id={Id} type={Type} to={To} after {N} attempt(s).",
                        o.Id, o.MessageType, o.ToNumber, o.Attempts + 1);
                }
                else
                {
                    // Non-2xx is a real API rejection — resending cannot fix it.
                    o.Status = "Abandoned";
                    o.LastError = BuildErrorSummary(status, body);
                    _log.LogWarning("WA-OUTBOX: abandoned id={Id} (API rejected): {Reason}", o.Id, o.LastError);
                }
            }
            catch (Exception ex)
            {
                // Transport failure (or unexpected error) — back off and retry until max attempts.
                o.Attempts++;
                o.LastError = DescribeException(ex);
                if (o.Attempts >= maxAttempts)
                {
                    o.Status = "Abandoned";
                    _log.LogError(ex, "WA-OUTBOX: abandoned id={Id} after {N} attempt(s): {Reason}", o.Id, o.Attempts, o.LastError);
                }
                else
                {
                    var backoffMin = Math.Min(15, (int)Math.Pow(2, o.Attempts)); // 2,4,8,15,15…
                    o.NextAttemptAt = IstClock.Now.AddMinutes(backoffMin);
                    _log.LogWarning("WA-OUTBOX: id={Id} attempt {N} failed ({Reason}); next in {Min}min.",
                        o.Id, o.Attempts, o.LastError, backoffMin);
                }
            }

            // Persist THIS row's outcome immediately so a mid-batch crash can't resend a
            // message already marked Sent.
            try { await _db.SaveChangesAsync(ct); }
            catch (Exception ex) { _log.LogError(ex, "WA-OUTBOX: failed to persist state for id={Id}", o.Id); }
        }
    }

    // Pulls messages[0].id (wamid...) out of a successful Graph API response body.
    private static string? ExtractMessageId(string? body)
    {
        if (string.IsNullOrEmpty(body)) return null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("messages", out var msgs) &&
                msgs.ValueKind == JsonValueKind.Array && msgs.GetArrayLength() > 0 &&
                msgs[0].TryGetProperty("id", out var idEl))
                return idEl.GetString();
        }
        catch { /* non-JSON / error body — leave id null */ }
        return null;
    }

    // Compact reason for a send that Meta rejected (non-2xx), stored in the history row so the
    // cause is readable in SQL: HTTP status + Meta's error code/subcode/message.
    private static string BuildErrorSummary(int httpStatus, string? body)
    {
        var reason = $"HTTP {httpStatus}";
        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("error", out var err))
                {
                    var code = err.TryGetProperty("code", out var c) ? c.ToString() : "?";
                    var sub  = err.TryGetProperty("error_subcode", out var s) ? s.ToString() : null;
                    var msg  = err.TryGetProperty("message", out var m) ? m.GetString() : null;
                    reason += $" | code={code}"
                            + (string.IsNullOrEmpty(sub) ? "" : $" subcode={sub}")
                            + (string.IsNullOrEmpty(msg) ? "" : $" | {msg}");
                }
                else reason += $" | {body}";
            }
            catch { reason += $" | {body}"; }
        }
        return Truncate(reason, 400);
    }

    // The innermost exception message (e.g. the SocketException 10054 text) for a transport-level
    // failure that never reached Meta — so a connectivity problem is self-evident in the history.
    private static string DescribeException(Exception ex)
    {
        var inner = ex;
        while (inner.InnerException != null) inner = inner.InnerException;
        return Truncate($"TRANSPORT: {inner.GetType().Name}: {inner.Message}", 400);
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s.Substring(0, max);

    // ── Shared Message-History Save (outbound + inbound) ──────
    // Single writer for the WhatsAppMessageHistory table. Never throws — a history
    // failure must never break an approval or a webhook.
    private async Task SaveMessageHistoryAsync(WaMsgContext ctx, string mobileNumber, string direction, string? messageId, string status, string? failureReason = null)
    {
        try
        {
            // On failure, append Meta's error (or the transport exception) to the stored text so
            // the cause is visible directly in SQL — no Event Viewer / log digging next time.
            var text = string.IsNullOrEmpty(failureReason)
                ? ctx.MessageText
                : (string.IsNullOrEmpty(ctx.MessageText) ? $"[FAIL] {failureReason}" : $"{ctx.MessageText} [FAIL] {failureReason}");

            _db.WhatsAppMessageHistories.Add(new WhatsAppMessageHistory
            {
                RequestId     = ctx.RequestId,
                WorkflowId    = ctx.WorkflowId,
                WorkflowLevel = ctx.WorkflowLevel,
                CreatedBy     = ctx.CreatedBy,
                MessageId     = messageId,
                MobileNumber  = mobileNumber,
                Direction     = direction,
                MessageType   = ctx.MessageType,
                MessageText   = text,
                Status        = status,
                CreatedDate   = IstClock.Now
            });
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "WA-HISTORY: Failed to save {Direction} message RequestId={Id}", direction, ctx.RequestId);
        }
    }

    // ── Inbound (webhook) history save ────────────────────────
    // Called by the webhook when an approver taps a button or replies with text, so
    // the reply is persisted in the same WhatsAppMessageHistory table as outbound.
    public async Task LogInboundMessageAsync(int requestId, string mobileNumber, string messageType, string messageText, string? messageId = null, int? approverUserId = null)
    {
        var workflowId = await _db.Requests.AsNoTracking()
            .Where(r => r.RequestId == requestId)
            .Select(r => r.WorkflowId)
            .FirstOrDefaultAsync();
        int? level = approverUserId == null ? null : await _db.RequestApprovers.AsNoTracking()
            .Where(a => a.RequestId == requestId && a.ApproverUserId == approverUserId)
            .Select(a => (int?)a.StepLevel)
            .FirstOrDefaultAsync();
        await SaveMessageHistoryAsync(
            new WaMsgContext(requestId, workflowId, messageType, messageText, level, approverUserId),
            mobileNumber, "Inbound", messageId, "Received");
    }

    // Downloads an inbound WhatsApp media item (document/image the approver sent).
    // Two-step per Meta Cloud API: (1) resolve the media id to a temporary URL,
    // (2) download the bytes (both calls require the Bearer token).
    public async Task<(bool ok, byte[]? bytes, string? mime, string? err)> DownloadInboundMediaAsync(string mediaId)
    {
        try
        {
            var token = _cfg["WhatsApp:BearerToken"];
            var apiVersion = _cfg["WhatsApp:ApiVersion"] ?? "v19.0";
            if (string.IsNullOrEmpty(token)) return (false, null, null, "WhatsApp token not configured.");

            using var client = new System.Net.Http.HttpClient();
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            // Step 1: media id → download URL
            var metaResp = await client.GetAsync($"https://graph.facebook.com/{apiVersion}/{mediaId}");
            var metaBody = await metaResp.Content.ReadAsStringAsync();
            if (!metaResp.IsSuccessStatusCode)
            {
                _log.LogError("WA-MEDIA: lookup failed {Code}: {Body}", (int)metaResp.StatusCode, metaBody);
                return (false, null, null, $"Media lookup failed ({(int)metaResp.StatusCode}).");
            }
            using var metaJson = System.Text.Json.JsonDocument.Parse(metaBody);
            var url = metaJson.RootElement.TryGetProperty("url", out var urlEl) ? urlEl.GetString() : null;
            var mime = metaJson.RootElement.TryGetProperty("mime_type", out var mimeEl) ? mimeEl.GetString() : null;
            if (string.IsNullOrEmpty(url)) return (false, null, null, "Media URL missing.");

            // Step 2: download the binary
            var fileResp = await client.GetAsync(url);
            if (!fileResp.IsSuccessStatusCode)
            {
                _log.LogError("WA-MEDIA: download failed {Code}", (int)fileResp.StatusCode);
                return (false, null, null, $"Media download failed ({(int)fileResp.StatusCode}).");
            }
            var bytes = await fileResp.Content.ReadAsByteArrayAsync();
            return (true, bytes, mime, null);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "WA-MEDIA: download exception mediaId={Id}", mediaId);
            return (false, null, null, ex.Message);
        }
    }

    // ── Helper Methods ────────────────────────────────────────
    private async Task<Request?> LoadRequestAsync(int requestId) =>
        await _db.Requests
            .AsNoTracking()
            .Include(r => r.Requestor)
            .Include(r => r.RequestType)
            .Include(r => r.Location)
            .Include(r => r.Department)
            .Include(r => r.Approvers).ThenInclude(a => a.ApproverUser)
            .Include(r => r.ActionLogs).ThenInclude(l => l.ActorUser)
            .Include(r => r.Documents)
            .FirstOrDefaultAsync(r => r.RequestId == requestId);

    private async Task<string?> GetPhoneAsync(int userId)
    {
        // Use AsNoTracking to always get fresh WhatsAppNumber from DB
        var user = await _db.Users.AsNoTracking()
            .Where(u => u.UserId == userId)
            .Select(u => new { u.WhatsAppNumber })
            .FirstOrDefaultAsync();
        return user?.WhatsAppNumber;
    }

    // Latest Remarks for notification templates. Binds the actual remark the approver
    // typed on the most recent action; when there is no remark it returns a clean
    // "No remarks" instead of a fabricated "Action: approved — actor | date" line, which
    // otherwise just duplicated the Latest Action field. Actor/date are intentionally NOT
    // repeated here — they already appear in the Latest Action line.
    private string GetLastRemark(Request r)
    {
        var lastComment = r.ActionLogs?
            .OrderByDescending(l => l.CreatedAt)
            .FirstOrDefault()?.Comment;
        return string.IsNullOrWhiteSpace(lastComment) ? "No remarks" : lastComment.Trim();
    }

    // WhatsApp rejects template body params containing new-line/tab characters or
    // more than 4 consecutive spaces (Meta error #132018). User-entered text
    // (comments, descriptions, filenames) can contain any of these, so every body
    // parameter is normalised: all whitespace runs collapse to a single space.
    private static string SanitizeParam(string? p)
    {
        if (string.IsNullOrEmpty(p)) return p ?? "";
        return System.Text.RegularExpressions.Regex.Replace(p, @"\s+", " ").Trim();
    }

    // Returns the text summary for the template parameter ({{10}} Attachments)
    // and separately returns the document record so we can send the actual file.
    private (string text, RequestDocument? doc) GetAttachment(Request r)
    {
        if (r.Documents == null || !r.Documents.Any()) return ("No attachments", null);
        // Order by DocumentId descending as fallback if UploadedAt is null
        var doc = r.Documents
            .OrderByDescending(d => d.UploadedAt)
            .ThenByDescending(d => d.DocumentId)
            .First();
        var count = r.Documents.Count;
        var text = count > 1
            ? $"📎 {count} file(s) attached. Latest: {doc.FileName}"
            : $"📎 {doc.FileName}";
        return (text, doc);
    }

    // Sends the actual document file as a WhatsApp document message.
    // Uses a fresh HttpClient (not the named "WhatsApp" client which may have BaseAddress set).
    // Step 1: Upload file to Meta media endpoint → get media_id
    // Step 2: Send document message with media_id
    private async Task SendDocumentAsync(string toPhone, RequestDocument doc)
    {
        try
        {
            var fullPath = System.IO.Path.Combine(GetStoragePath(), doc.StoredFileName);
            if (!File.Exists(fullPath))
            {
                _log.LogWarning("WA-DOC: File not found: {Path}", fullPath);
                return;
            }

            var token = _cfg["WhatsApp:BearerToken"];
            var phoneNumId = _cfg["WhatsApp:PhoneNumberId"];
            var apiVersion = _cfg["WhatsApp:ApiVersion"] ?? "v19.0";
            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(phoneNumId)) return;

            var normalised = NormalisePhone(toPhone);
            var mimeType = doc.ContentType ?? GetMimeType(doc.FileName);
            var fileBytes = await File.ReadAllBytesAsync(fullPath);

            // ── Step 1: Upload to Meta media endpoint ────────────────────────
            // Use a plain new HttpClient — named client may have BaseAddress set
            // which conflicts with absolute URLs used here.
            string mediaId;
            using (var uploadClient = new System.Net.Http.HttpClient())
            {
                uploadClient.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

                // Build multipart exactly as Meta requires:
                // field "messaging_product" = "whatsapp"
                // field "file" = binary with correct Content-Type and filename in Content-Disposition
                using var form = new MultipartFormDataContent();
                form.Add(new StringContent("whatsapp"), "messaging_product");

                var fileContent = new ByteArrayContent(fileBytes);
                fileContent.Headers.ContentType =
                    new System.Net.Http.Headers.MediaTypeHeaderValue(mimeType);
                // Meta requires the filename in Content-Disposition
                form.Add(fileContent, "file", doc.FileName);

                var uploadUrl = $"https://graph.facebook.com/{apiVersion}/{phoneNumId}/media";
                _log.LogInformation("WA-DOC: Uploading {File} ({Bytes} bytes) to {Url}", doc.FileName, fileBytes.Length, uploadUrl);

                var uploadResp = await uploadClient.PostAsync(uploadUrl, form);
                var uploadBody = await uploadResp.Content.ReadAsStringAsync();

                _log.LogInformation("WA-DOC: Upload response {Code}: {Body}", (int)uploadResp.StatusCode, uploadBody);

                if (!uploadResp.IsSuccessStatusCode)
                {
                    _log.LogError("WA-DOC: Media upload failed {Code}: {Body}", (int)uploadResp.StatusCode, uploadBody);
                    return;
                }

                using var uploadJson = System.Text.Json.JsonDocument.Parse(uploadBody);
                mediaId = uploadJson.RootElement.GetProperty("id").GetString()!;
                _log.LogInformation("WA-DOC: media_id={MediaId} file={File}", mediaId, doc.FileName);
            }

            // ── Step 2: Send as image or document based on file type ─────────
            var imageExts = new[] { ".jpg", ".jpeg", ".png", ".gif" };
            var isImage = imageExts.Contains(
                System.IO.Path.GetExtension(doc.FileName).ToLowerInvariant());

            object sendPayload;
            if (isImage)
            {
                sendPayload = new
                {
                    messaging_product = "whatsapp",
                    to = normalised,
                    type = "image",
                    image = new { id = mediaId, caption = $"📎 {doc.FileName}" }
                };
            }
            else
            {
                sendPayload = new
                {
                    messaging_product = "whatsapp",
                    to = normalised,
                    type = "document",
                    document = new { id = mediaId, filename = doc.FileName, caption = $"📎 {doc.FileName}" }
                };
            }

            await PostToWhatsAppAsync(normalised, sendPayload, token, phoneNumId, apiVersion);
            _log.LogInformation("WA-DOC: File sent to {Phone} file={File} type={Type}",
                normalised, doc.FileName, isImage ? "image" : "document");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "WA-DOC: Failed to send document to {Phone} file={File}", toPhone, doc.FileName);
            // Never throw — document failure must not crash submit
        }
    }

    // Fallback MIME type from file extension when ContentType is not stored
    private static string GetMimeType(string fileName) =>
        System.IO.Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf",
            ".doc" => "application/msword",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xls" => "application/vnd.ms-excel",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".png" => "image/png",
            ".jpg" => "image/jpeg",
            ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".zip" => "application/zip",
            _ => "application/octet-stream"
        };

    private string FormatDateTime(DateTimeOffset dt)
    {
        // TimeZone is configured in appsettings.json — "India Standard Time" for Windows IIS
        var tzId = _cfg["AppSettings:TimeZone"] ?? "India Standard Time";
        TimeZoneInfo ist;
        try { ist = TimeZoneInfo.FindSystemTimeZoneById(tzId); }
        catch { ist = TimeZoneInfo.Utc; }
        var local = TimeZoneInfo.ConvertTime(dt, ist);
        return local.ToString("dd-MMM-yyyy | hh:mm tt");
    }

    private string FormatPriority(string priority) => priority?.ToLower() switch
    {
        "critical" => "🔴 Critical",
        "high" => "🔴 High",
        "medium" => "🟡 Medium",
        "low" => "🟢 Low",
        _ => priority ?? "Medium"
    };
}