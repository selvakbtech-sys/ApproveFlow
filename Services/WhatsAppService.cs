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
    Task SendClarificationPromptAsync(string toPhone, int requestId, int approverId);
}

public class WhatsAppService : IWhatsAppService
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _cfg;
    private readonly IHttpClientFactory _http;
    private readonly ILogger<WhatsAppService> _log;

    private readonly IWebHostEnvironment _env;

    public WhatsAppService(AppDbContext db, IConfiguration cfg, IHttpClientFactory http, ILogger<WhatsAppService> log, IWebHostEnvironment env)
    {
        _db = db; _cfg = cfg; _http = http; _log = log; _env = env;
    }

    private string GetStoragePath()
    {
        var configured = _cfg["AppSettings:FileStoragePath"] ?? "uploads";
        if (System.IO.Path.IsPathRooted(configured))
            return configured;
        return System.IO.Path.Combine(_env.ContentRootPath, configured);
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
        await SendTemplateWithButtonsAsync(phone, "new_approval_request1", new[]
        {
            requestTypeLabel,            // {{1}} Request Type (prefixed with "Re-submitted" if applicable)
            r.RequestNumber,             // {{2}} ID
            r.Title,                     // {{3}} Title
            amount,                      // {{4}} Amount
            priority,                    // {{5}} Priority
            raisedBy,                    // {{6}} Raised By
            dateTime,                    // {{7}} Date & Time
            r.Description ?? r.Title,    // {{8}} Justification
            lastRemark,                  // {{9}} Latest Remarks
            attachmentText               // {{10}} Attachments
        }, requestId, l1.ApproverUserId, doc, docs);
    }

    // ── 1b. Re-submission after clarification → Approver at CurrentStepLevel ──
    // Sends to whichever level requested the clarification, not always L1.
    public async Task SendResubmitToApproverAsync(int requestId, int approverId, int stepLevel)
    {
        var r = await LoadRequestAsync(requestId);
        if (r == null) return;

        var phone = await GetPhoneAsync(approverId);
        if (string.IsNullOrEmpty(phone)) return;

        var lastRemark = GetLastRemark(r);
        var dateTime   = FormatDateTime(r.SubmittedAt ?? r.CreatedAt);
        var raisedBy   = $"{r.Requestor.FullName} ({r.Department.DeptName})";
        var priority   = FormatPriority(r.Priority);
        var amount     = r.Amount.HasValue ? r.Amount.Value.ToString("N0") : "0";
        var docs = await _db.RequestDocuments.AsNoTracking()
                        .Where(d => d.RequestId == requestId)
                        .OrderBy(d => d.DocumentId)
                        .ToListAsync();
        var doc = docs.FirstOrDefault();
        var attachmentText = docs.Any()
            ? string.Join(", ", docs.Select(d => $"📎 {d.FileName}"))
            : "No attachments";

        var requestTypeLabel = $"Re-submitted {r.RequestType.TypeName}";

        _log.LogInformation("WA-RESUBMIT: RequestId={Id} StepLevel={Level} ApproverId={Aid}",
            requestId, stepLevel, approverId);

        if (stepLevel == 1)
        {
            // L1 template: {{1}}=Type {{2}}=ID {{3}}=Title {{4}}=Amount {{5}}=Priority
            // {{6}}=RaisedBy {{7}}=DateTime {{8}}=Justification {{9}}=Remarks {{10}}=Attachments
            await SendTemplateWithButtonsAsync(phone, "new_approval_request1", new[]
            {
                requestTypeLabel,
                r.RequestNumber,
                r.Title,
                amount,
                priority,
                raisedBy,
                dateTime,
                r.Description ?? r.Title,
                lastRemark,
                attachmentText
            }, requestId, approverId, doc, docs);
        }
        else
        {
            // L2+ template: {{1}}=Level {{2}}=Type {{3}}=ID ... (one extra param for level)
            await SendTemplateWithButtonsAsync(phone, "approval_required_level2", new[]
            {
                stepLevel.ToString(),
                requestTypeLabel,
                r.RequestNumber,
                r.Title,
                amount,
                priority,
                raisedBy,
                dateTime,
                r.Description ?? r.Title,
                lastRemark,
                attachmentText
            }, requestId, approverId, doc, docs);
        }
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

        // Notify creator
        var creatorPhone = await GetPhoneAsync(r.RequestorId);
        if (!string.IsNullOrEmpty(creatorPhone))
        {
            if (r.Status == "Approved")
            {
                // Use Case 5: Final Approval
                await SendTemplateAsync(creatorPhone, "request_fully_approved", new[]
                {
                    r.RequestType.TypeName,      // {{1}} Request Type
                    r.RequestNumber,             // {{2}} ID
                    r.Title,                     // {{3}} Title
                    amount,                      // {{4}} Amount
                    priority,                    // {{5}} Priority
                    raisedBy,                    // {{6}} Raised By
                    dateTime,                    // {{7}} Date & Time
                    r.Description ?? r.Title,    // {{8}} Justification
                    lastRemark,                  // {{9}} Latest Remarks
                    attachmentText               // {{10}} Attachments
                }, doc, docs);
            }
            else
            {
                // Progress update to creator
                await SendTemplateAsync(creatorPhone, "approval_progress_update", new[]
                {
                    r.RequestType.TypeName,      // {{1}} Request Type
                    r.RequestNumber,             // {{2}} ID
                    r.Title,                     // {{3}} Title
                    amount,                      // {{4}} Amount
                    priority,                    // {{5}} Priority
                    raisedBy,                    // {{6}} Raised By
                    dateTime,                    // {{7}} Date & Time
                    r.Description ?? r.Title,    // {{8}} Justification
                    lastRemark,                  // {{9}} Latest Remarks
                    attachmentText               // {{10}} Attachments
                }, doc, docs);
            }
        }

        // Notify next approver with buttons + actual document
        if (r.Status != "Approved")
        {
            // Find the next pending approver — check "pending" status (set by ProcessActionAsync)
            // If not found by status, fall back to next step level (handles EF tracker timing)
            var nextStep = r.Approvers
                .Where(a => a.StepLevel > actingStep.StepLevel && a.Status == "pending")
                .OrderBy(a => a.StepLevel).FirstOrDefault()
                ?? r.Approvers
                   .Where(a => a.StepLevel > actingStep.StepLevel)
                   .OrderBy(a => a.StepLevel).FirstOrDefault();

            if (nextStep != null)
            {
                var nextPhone = await GetPhoneAsync(nextStep.ApproverUserId);
                if (!string.IsNullOrEmpty(nextPhone))
                {
                    // Use Case 2: Mid-Level Approval
                    // Detect re-submission after clarification
                    var isResubmitL2 = r.ActionLogs?.Any(l => l.Action == "clarification") == true;
                    var requestTypeLabelL2 = isResubmitL2
                        ? $"Re-submitted {r.RequestType.TypeName}"
                        : r.RequestType.TypeName;

                    await SendTemplateWithButtonsAsync(nextPhone, "approval_required_level2", new[]
                    {
                        actingStep.StepLevel.ToString(),     // {{1}} Level
                        requestTypeLabelL2,                  // {{2}} Request Type
                        r.RequestNumber,                     // {{3}} ID
                        r.Title,                             // {{4}} Title
                        amount,                              // {{5}} Amount
                        priority,                            // {{6}} Priority
                        raisedBy,                            // {{7}} Raised By
                        dateTime,                            // {{8}} Date & Time
                        r.Description ?? r.Title,            // {{9}} Justification
                        lastRemark,                          // {{10}} Latest Remarks
                        attachmentText                       // {{11}} Attachments
                    }, requestId, nextStep.ApproverUserId, doc, docs);
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

        // Use Case 3: Clarification
        await SendTemplateAsync(creatorPhone, "clarification_needed", new[]
        {
            r.RequestType.TypeName,      // {{1}} Request Type
            r.RequestNumber,             // {{2}} ID
            r.Title,                     // {{3}} Title
            amount,                      // {{4}} Amount
            priority,                    // {{5}} Priority
            raisedBy,                    // {{6}} Raised By
            dateTime,                    // {{7}} Date & Time
            r.Description ?? r.Title,    // {{8}} Justification
            remark,                      // {{9}} Latest Remarks
            "N/A"                        // {{10}} Attachments — not applicable for clarification
        });
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
        var doc = docsRej.FirstOrDefault();
        var attachmentText = docsRej.Any()
            ? string.Join(", ", docsRej.Select(d => $"📎 {d.FileName}"))
            : "No attachments";

        // Use Case 6: Rejected
        await SendTemplateAsync(creatorPhone, "request_rejected", new[]
        {
            r.RequestType.TypeName,      // {{1}} Request Type
            r.RequestNumber,             // {{2}} ID
            r.Title,                     // {{3}} Title
            amount,                      // {{4}} Amount
            priority,                    // {{5}} Priority
            raisedBy,                    // {{6}} Raised By
            dateTime,                    // {{7}} Date & Time
            r.Description ?? r.Title,    // {{8}} Justification
            remark,                      // {{9}} Latest Remarks
            attachmentText               // {{10}} Attachments
        }, doc, docsRej);
    }

    // ── 5. Clarification Prompt → Approver ───────────────────
    public async Task SendClarificationPromptAsync(string toPhone, int requestId, int approverId)
    {
        if (requestId == 0) return; // confirmation call, skip

        var r = await LoadRequestAsync(requestId);
        if (r == null) return;

        await SendTextAsync(toPhone,
            $"💬 *Clarification Required*\n\n" +
            $"Request No : *{r.RequestNumber}*\n" +
            $"Title      : {r.Title}\n\n" +
            $"Please type your clarification comment and send it as a reply.\n" +
            $"Your message will be automatically saved and sent to the requestor.");
    }

    // ── 6. Aging Reminder ─────────────────────────────────────
    public async Task SendAgingReminderAsync()
    {
        var pendingApprovers = await _db.RequestApprovers
            .Include(a => a.Request).ThenInclude(r => r.RequestType)
            .Include(a => a.Request).ThenInclude(r => r.Location)
            .Include(a => a.Request).ThenInclude(r => r.Department)
            .Include(a => a.ApproverUser)
            .Where(a => a.Status == "pending")
            .ToListAsync();

        if (!pendingApprovers.Any()) return;

        var grouped = pendingApprovers
            .GroupBy(a => new { a.ApproverUserId, a.StepLevel }).ToList();

        foreach (var grp in grouped)
        {
            var phone = await GetPhoneAsync(grp.Key.ApproverUserId);
            if (string.IsNullOrEmpty(phone)) continue;

            var approverName = grp.First().ApproverUser?.FullName ?? "Approver";
            var items = grp.OrderByDescending(a =>
                IstClock.DaysSince(a.Request.SubmittedAt ?? a.Request.CreatedAt)).ToList();

            await SendTemplateAsync(phone, "pending_approval_reminder", new[]
            {
                approverName,
                items.Count.ToString(),
                grp.Key.StepLevel.ToString()
            });
        }
    }

    // ── Send Template WITH 3 Buttons ─────────────────────────
    // Sends template (body + buttons), then file as separate message for all formats.
    private async Task SendTemplateWithButtonsAsync(
        string toPhone, string templateName, string[] parameters,
        int requestId, int approverId, RequestDocument? doc = null,
        IList<RequestDocument>? allDocs = null)
    {
        var token = _cfg["WhatsApp:BearerToken"];
        var phoneNumId = _cfg["WhatsApp:PhoneNumberId"];
        var apiVersion = _cfg["WhatsApp:ApiVersion"] ?? "v19.0";
        var language = _cfg["WhatsApp:TemplateLanguage"] ?? "en";

        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(phoneNumId)) return;

        var normalised = toPhone.Replace(" ", "").Replace("-", "").TrimStart('+');

        // Upload doc to get media_id — embed as header inside template
        string? mediaId = null;
        if (doc != null)
        {
            var fullPath = System.IO.Path.Combine(GetStoragePath(), doc.StoredFileName);
            if (File.Exists(fullPath))
            {
                try
                {
                    var mimeType = doc.ContentType ?? GetMimeType(doc.FileName);
                    var fileBytes = await File.ReadAllBytesAsync(fullPath);
                    using var uploadClient = new System.Net.Http.HttpClient();
                    uploadClient.DefaultRequestHeaders.Authorization =
                        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                    using var form = new MultipartFormDataContent();
                    form.Add(new StringContent("whatsapp"), "messaging_product");
                    var fc = new ByteArrayContent(fileBytes);
                    fc.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mimeType);
                    form.Add(fc, "file", doc.FileName);
                    var uploadResp = await uploadClient.PostAsync(
                        $"https://graph.facebook.com/{apiVersion}/{phoneNumId}/media", form);
                    var uploadBody = await uploadResp.Content.ReadAsStringAsync();
                    _log.LogInformation("WA-DOC: Upload {Code}: {Body}", (int)uploadResp.StatusCode, uploadBody);
                    if (uploadResp.IsSuccessStatusCode)
                    {
                        using var j = System.Text.Json.JsonDocument.Parse(uploadBody);
                        mediaId = j.RootElement.GetProperty("id").GetString();
                        _log.LogInformation("WA-DOC: media_id={Id} file={File}", mediaId, doc.FileName);
                    }
                }
                catch (Exception ex) { _log.LogError(ex, "WA-DOC: Upload exception {File}", doc.FileName); }
            }
            else _log.LogWarning("WA-DOC: File not found {Path}", System.IO.Path.Combine(GetStoragePath(), doc.StoredFileName));
        }

        var components = new List<object>();
        if (mediaId != null)
        {
            components.Add(new
            {
                type = "header",
                parameters = new[] { new { type = "document", document = new { id = mediaId, filename = doc!.FileName } } }
            });
        }
        components.Add(new { type = "body", parameters = parameters.Select(p => new { type = "text", text = p }).ToArray() });
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

        await PostToWhatsAppAsync(normalised, payload, token, phoneNumId, apiVersion);

        // Send remaining documents (2nd, 3rd...) as separate messages after template
        if (allDocs != null && allDocs.Count > 1)
        {
            foreach (var extraDoc in allDocs.Skip(1))
                await SendDocumentAsync(normalised, extraDoc);
        }
    }

    // ── Send Template WITHOUT Buttons ─────────────────────────
    // Sends template (body only), then file as separate message for all formats.
    private async Task SendTemplateAsync(string toPhone, string templateName, string[] parameters,
        RequestDocument? doc = null, IList<RequestDocument>? allDocs = null)
    {
        var token = _cfg["WhatsApp:BearerToken"];
        var phoneNumId = _cfg["WhatsApp:PhoneNumberId"];
        var apiVersion = _cfg["WhatsApp:ApiVersion"] ?? "v19.0";
        var language = _cfg["WhatsApp:TemplateLanguage"] ?? "en";

        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(phoneNumId)) return;

        var normalised = toPhone.Replace(" ", "").Replace("-", "").TrimStart('+');

        // Upload doc to get media_id — embed as header inside template
        string? mediaId = null;
        if (doc != null)
        {
            var fullPath = System.IO.Path.Combine(GetStoragePath(), doc.StoredFileName);
            if (File.Exists(fullPath))
            {
                try
                {
                    var mimeType = doc.ContentType ?? GetMimeType(doc.FileName);
                    var fileBytes = await File.ReadAllBytesAsync(fullPath);
                    using var uploadClient = new System.Net.Http.HttpClient();
                    uploadClient.DefaultRequestHeaders.Authorization =
                        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                    using var form = new MultipartFormDataContent();
                    form.Add(new StringContent("whatsapp"), "messaging_product");
                    var fc = new ByteArrayContent(fileBytes);
                    fc.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mimeType);
                    form.Add(fc, "file", doc.FileName);
                    var uploadResp = await uploadClient.PostAsync(
                        $"https://graph.facebook.com/{apiVersion}/{phoneNumId}/media", form);
                    var uploadBody = await uploadResp.Content.ReadAsStringAsync();
                    if (uploadResp.IsSuccessStatusCode)
                    {
                        using var j = System.Text.Json.JsonDocument.Parse(uploadBody);
                        mediaId = j.RootElement.GetProperty("id").GetString();
                    }
                }
                catch (Exception ex) { _log.LogError(ex, "WA-DOC: Upload exception {File}", doc.FileName); }
            }
        }

        var components = new List<object>();
        if (mediaId != null)
        {
            components.Add(new
            {
                type = "header",
                parameters = new[] { new { type = "document", document = new { id = mediaId, filename = doc!.FileName } } }
            });
        }
        components.Add(new { type = "body", parameters = parameters.Select(p => new { type = "text", text = p }).ToArray() });

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

        await PostToWhatsAppAsync(normalised, payload, token, phoneNumId, apiVersion);

        // Send remaining documents (2nd, 3rd...) as separate messages after template
        if (allDocs != null && allDocs.Count > 1)
        {
            foreach (var extraDoc in allDocs.Skip(1))
                await SendDocumentAsync(normalised, extraDoc);
        }
    }

    // ── Send Plain Text ───────────────────────────────────────
    private async Task SendTextAsync(string toPhone, string message)
    {
        var token = _cfg["WhatsApp:BearerToken"];
        var phoneNumId = _cfg["WhatsApp:PhoneNumberId"];
        var apiVersion = _cfg["WhatsApp:ApiVersion"] ?? "v19.0";

        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(phoneNumId)) return;

        var normalised = toPhone.Replace(" ", "").Replace("-", "").TrimStart('+');
        var payload = new
        {
            messaging_product = "whatsapp",
            to = normalised,
            type = "text",
            text = new { preview_url = false, body = message }
        };

        await PostToWhatsAppAsync(normalised, payload, token, phoneNumId, apiVersion);
    }

    // ── Core HTTP Post ────────────────────────────────────────
    private async Task PostToWhatsAppAsync(string normalised, object payload, string token, string phoneNumId, string apiVersion)
    {
        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var client = _http.CreateClient("WhatsApp");
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var url = $"https://graph.facebook.com/{apiVersion}/{phoneNumId}/messages";
        try
        {
            var resp = await client.PostAsync(url, content);
            var body = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
                _log.LogError("WhatsApp API error {Code}: {Body}", (int)resp.StatusCode, body);
            else
                _log.LogInformation("WhatsApp message sent to {Phone}.", normalised);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "WhatsApp send failed for {Phone}.", normalised);
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

    private string GetLastRemark(Request r)
    {
        var last = r.ActionLogs?
            .OrderByDescending(l => l.CreatedAt)
            .FirstOrDefault();
        if (last == null) return "No remarks yet";
        var actor = last.ActorUser?.FullName ?? "System";
        var date = FormatDateTime(last.CreatedAt);
        return string.IsNullOrEmpty(last.Comment)
            ? $"Action: {last.Action} — {actor} | {date}"
            : $"{last.Comment} — {actor} | {date}";
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

            var normalised = toPhone.Replace(" ", "").Replace("-", "").TrimStart('+');
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