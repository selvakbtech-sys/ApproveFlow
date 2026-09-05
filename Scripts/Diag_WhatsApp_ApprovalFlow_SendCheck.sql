/*==============================================================================
  ApproveFlow - DIAGNOSTIC: was the approval-flow WhatsApp message sent or not?
  ------------------------------------------------------------------------------
  READ-ONLY. No INSERT / UPDATE / DELETE. Safe to run on LIVE.

  Answers, for one request (or for every request touched in the last N days):
      - which approval-flow WhatsApp messages left the app,
      - which ones FAILED at the Graph API,
      - which ones are still sitting in the resend outbox,
      - which approval ACTIONS produced no WhatsApp message at all,
      - which approvers cannot be messaged because they have no WhatsApp number.

  HOW TO USE
      Set ONE of @RequestNumber / @RequestId below and run.
      Leave both NULL to sweep every request touched in the last @Days days.

  READING THE RESULT
      Status = 'Sent'    -> the Graph API accepted it (MessageId = wamid...).
                           "Accepted" is NOT "delivered/read" - see note (A).
      Status = 'Failed'  -> Meta rejected the send (bad template, 24h window,
                           number not on WhatsApp).
      Outbox 'Pending'   -> never reached Meta (TLS reset / network); will resend.
      Outbox 'Abandoned' -> gave up after WhatsApp:OutboxMaxAttempts attempts.
      No row at all      -> the app never attempted a send. Check the request has
                           a workflow, the approver has a number, and the app was up.

  NOTES
      (A) WhatsAppMessageHistory records what the APP did. WhatsApp delivery/read
          receipts are not stored, so 'Sent' means "accepted by Meta", not "seen".
      (B) Free-form messages ('text', 'prompt:*') only reach a person who messaged
          the bot within the last 24 hours. Templates ('template:*') always reach.
      (C) Table names are the EXACT SQL names from the EF DbContext, including the
          SINGULAR RequestActionLog and WhatsAppMessageHistory.
==============================================================================*/

SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;   -- required: the script uses XML data type methods

DECLARE @RequestNumber VARCHAR(50) = NULL;   -- e.g. 'REQ-26-000123'
DECLARE @RequestId     INT         = NULL;   -- e.g. 123
DECLARE @Days          INT         = 7;      -- used only when both above are NULL

IF @RequestId IS NULL AND @RequestNumber IS NOT NULL
    SELECT @RequestId = RequestId FROM dbo.Requests WHERE RequestNumber = @RequestNumber;

DECLARE @From DATETIMEOFFSET = DATEADD(DAY, -@Days, SYSDATETIMEOFFSET());

PRINT '=== ApproveFlow WhatsApp send check ===';
PRINT 'RequestId filter : ' + ISNULL(CONVERT(VARCHAR(20), @RequestId), '(all requests)');
PRINT 'Window from      : ' + CONVERT(VARCHAR(35), @From);


/*------------------------------------------------------------------------------
  1) The request(s) in scope - status, current level, who should be acting now.
------------------------------------------------------------------------------*/
SELECT
    [Section]        = '1. Requests in scope',
    r.RequestId,
    r.RequestNumber,
    r.Status,
    r.CurrentStepLevel,
    RequestType      = rt.TypeName,
    Requestor        = ru.FullName,
    RequestorWA      = ru.WhatsAppNumber,
    r.WorkflowId,
    SubmittedAtIST   = CONVERT(VARCHAR(19), r.SubmittedAt AT TIME ZONE 'India Standard Time', 120),
    CurrentApprovers = STUFF((
        SELECT ', ' + au.FullName + ISNULL(' (' + au.WhatsAppNumber + ')', ' (NO WA NUMBER)')
        FROM dbo.RequestApprovers ra
        JOIN dbo.Users au ON au.UserId = ra.ApproverUserId
        WHERE ra.RequestId = r.RequestId
          AND ra.StepLevel = r.CurrentStepLevel
          AND ra.Status    = 'pending'
        FOR XML PATH(''), TYPE).value('.', 'NVARCHAR(MAX)'), 1, 2, '')
FROM dbo.Requests r
JOIN dbo.RequestTypes rt ON rt.RequestTypeId = r.RequestTypeId
JOIN dbo.Users        ru ON ru.UserId        = r.RequestorId
WHERE (@RequestId IS NOT NULL AND r.RequestId = @RequestId)
   OR (@RequestId IS NULL AND r.CreatedAt >= @From)
ORDER BY r.RequestId DESC;


/*------------------------------------------------------------------------------
  2) Every WhatsApp message logged for the request(s) - the sent / not-sent answer.
------------------------------------------------------------------------------*/
SELECT
    [Section]     = '2. WhatsApp message log',
    h.Id,
    r.RequestNumber,
    h.Direction,                                   -- Outbound / Inbound
    h.MessageType,                                 -- approved / rejected / clarification / template:* / prompt:* / text
    h.Status,                                      -- Sent / Failed / Received
    Verdict       = CASE
                        WHEN h.Direction = 'Inbound'                       THEN 'RECEIVED from approver'
                        WHEN h.Status = 'Sent' AND h.MessageId IS NOT NULL THEN 'SENT - accepted by Meta'
                        WHEN h.Status = 'Sent'                             THEN 'SENT - but no wamid recorded'
                        ELSE 'NOT SENT - Meta rejected the send'
                    END,
    Channel       = CASE WHEN h.MessageType LIKE 'template:%'
                         THEN 'Template (always delivers)'
                         ELSE 'Free-form (needs 24h window)' END,
    h.MobileNumber,
    h.WorkflowLevel,
    Recipient     = u.FullName,
    h.MessageId,
    MessageText   = LEFT(h.MessageText, 160),
    SentAtIST     = CONVERT(VARCHAR(19), h.CreatedDate AT TIME ZONE 'India Standard Time', 120)
FROM dbo.WhatsAppMessageHistory h
LEFT JOIN dbo.Requests r ON r.RequestId = h.RequestId
LEFT JOIN dbo.Users    u ON u.UserId    = h.CreatedBy
WHERE (@RequestId IS NOT NULL AND h.RequestId = @RequestId)
   OR (@RequestId IS NULL AND h.CreatedDate >= @From)
ORDER BY h.RequestId DESC, h.CreatedDate;


/*------------------------------------------------------------------------------
  3) FAILURES ONLY - the short list to act on.
------------------------------------------------------------------------------*/
SELECT
    [Section]   = '3. Failed sends',
    r.RequestNumber,
    h.MessageType,
    h.MobileNumber,
    Recipient   = u.FullName,
    MessageText = LEFT(h.MessageText, 200),
    FailedAtIST = CONVERT(VARCHAR(19), h.CreatedDate AT TIME ZONE 'India Standard Time', 120),
    LikelyCause = CASE
                      WHEN h.MobileNumber IS NULL OR h.MobileNumber = '' THEN 'No WhatsApp number on the user'
                      WHEN h.MessageType LIKE 'template:%'               THEN 'Template not approved / wrong parameter count / wrong language'
                      ELSE 'Outside the 24-hour free-form window, or number not on WhatsApp'
                  END
FROM dbo.WhatsAppMessageHistory h
LEFT JOIN dbo.Requests r ON r.RequestId = h.RequestId
LEFT JOIN dbo.Users    u ON u.UserId    = h.CreatedBy
WHERE h.Direction = 'Outbound'
  AND h.Status   <> 'Sent'
  AND ((@RequestId IS NOT NULL AND h.RequestId = @RequestId)
    OR (@RequestId IS NULL AND h.CreatedDate >= @From))
ORDER BY h.CreatedDate DESC;


/*------------------------------------------------------------------------------
  4) Resend outbox - messages that never reached Meta (transport failure).
     Pending = will retry.  Abandoned = out of attempts, needs a manual resend.
------------------------------------------------------------------------------*/
SELECT
    [Section]      = '4. Outbox (transport failures)',
    o.Id,
    r.RequestNumber,
    o.Status,
    o.MessageType,
    o.ToNumber,
    o.Attempts,
    QueuedAtIST    = CONVERT(VARCHAR(19), o.CreatedDate   AT TIME ZONE 'India Standard Time', 120),
    NextAttemptIST = CONVERT(VARCHAR(19), o.NextAttemptAt AT TIME ZONE 'India Standard Time', 120),
    DueNow         = CASE WHEN o.Status = 'Pending' AND o.NextAttemptAt <= SYSDATETIMEOFFSET()
                          THEN 'YES - resender should pick it up' ELSE 'no' END,
    LastError      = LEFT(o.LastError, 300),
    MessageText    = LEFT(o.MessageText, 160)
FROM dbo.WhatsAppOutbox o
LEFT JOIN dbo.Requests r ON r.RequestId = o.RequestId
WHERE (@RequestId IS NOT NULL AND o.RequestId = @RequestId)
   OR (@RequestId IS NULL AND o.CreatedDate >= @From)
ORDER BY o.Status, o.CreatedDate DESC;


/*------------------------------------------------------------------------------
  5) GAP CHECK - approval actions that produced NO WhatsApp message.
     One row per approve / reject / clarification action. "MISSING" means the
     action happened but nothing was logged as outbound within 5 minutes of it.
------------------------------------------------------------------------------*/
SELECT
    [Section]      = '5. Action vs message gap',
    r.RequestNumber,
    l.Action,
    l.StepLevel,
    Actor          = au.FullName,
    ActionAtIST    = CONVERT(VARCHAR(19), l.CreatedAt AT TIME ZONE 'India Standard Time', 120),
    MessagesLogged = x.MsgCount,
    Verdict        = CASE WHEN x.MsgCount > 0 THEN 'OK - message(s) logged'
                          ELSE 'MISSING - no WhatsApp message for this action' END
FROM dbo.RequestActionLog l
JOIN dbo.Requests r  ON r.RequestId = l.RequestId
JOIN dbo.Users    au ON au.UserId   = l.ActorUserId
CROSS APPLY (
    SELECT MsgCount = COUNT(*)
    FROM dbo.WhatsAppMessageHistory h
    WHERE h.RequestId   = l.RequestId
      AND h.Direction   = 'Outbound'
      AND h.CreatedDate >= DATEADD(MINUTE, -1, l.CreatedAt)
      AND h.CreatedDate <  DATEADD(MINUTE,  5, l.CreatedAt)
) x
WHERE ((@RequestId IS NOT NULL AND l.RequestId = @RequestId)
    OR (@RequestId IS NULL AND l.CreatedAt >= @From))
ORDER BY r.RequestId DESC, l.CreatedAt;


/*------------------------------------------------------------------------------
  6) People who can never be messaged - users sitting on a pending approval
     with no WhatsApp number on their profile.
------------------------------------------------------------------------------*/
SELECT DISTINCT
    [Section]   = '6. Approvers without a WhatsApp number',
    u.UserId,
    u.FullName,
    u.Username,
    u.Status,
    PendingRows = (SELECT COUNT(*)
                   FROM dbo.RequestApprovers ra2
                   JOIN dbo.Requests r2 ON r2.RequestId = ra2.RequestId
                   WHERE ra2.ApproverUserId = u.UserId
                     AND ra2.Status = 'pending'
                     AND r2.Status IN ('Open', 'Pending Approval', 'Partial Approved'))
FROM dbo.Users u
JOIN dbo.RequestApprovers ra ON ra.ApproverUserId = u.UserId AND ra.Status = 'pending'
JOIN dbo.Requests r          ON r.RequestId       = ra.RequestId
WHERE (u.WhatsAppNumber IS NULL OR LTRIM(RTRIM(u.WhatsAppNumber)) = '')
  AND r.Status IN ('Open', 'Pending Approval', 'Partial Approved')
  AND (@RequestId IS NULL OR r.RequestId = @RequestId)
ORDER BY u.FullName;


/*------------------------------------------------------------------------------
  7) One-line summary of the window.
------------------------------------------------------------------------------*/
SELECT
    [Section]   = '7. Summary',
    Outbound    = SUM(CASE WHEN h.Direction = 'Outbound' THEN 1 ELSE 0 END),
    Sent        = SUM(CASE WHEN h.Direction = 'Outbound' AND h.Status =  'Sent' THEN 1 ELSE 0 END),
    Failed      = SUM(CASE WHEN h.Direction = 'Outbound' AND h.Status <> 'Sent' THEN 1 ELSE 0 END),
    Inbound     = SUM(CASE WHEN h.Direction = 'Inbound'  THEN 1 ELSE 0 END),
    OutboxPend  = (SELECT COUNT(*) FROM dbo.WhatsAppOutbox WHERE Status = 'Pending'),
    OutboxAband = (SELECT COUNT(*) FROM dbo.WhatsAppOutbox WHERE Status = 'Abandoned')
FROM dbo.WhatsAppMessageHistory h
WHERE (@RequestId IS NOT NULL AND h.RequestId = @RequestId)
   OR (@RequestId IS NULL AND h.CreatedDate >= @From);
