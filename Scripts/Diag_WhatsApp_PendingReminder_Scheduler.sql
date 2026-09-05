/*==============================================================================
  ApproveFlow - DIAGNOSTIC: WhatsApp PENDING-APPROVAL REMINDER scheduler
  ------------------------------------------------------------------------------
  READ-ONLY. No INSERT / UPDATE / DELETE. Safe to run on LIVE.

  Checks the daily reminder driven by AgingReminderBackgroundService
  (Services/AgingReminderBackgroundService.cs) + WhatsAppService.SendAgingReminderAsync:

      - did each configured slot actually fire today (and on recent days)?
      - which approvers SHOULD have been reminded (live pending state)?
      - which approvers WERE reminded in the last slot, and who was missed?
      - do the reminder counts match the Pending Approval page?
      - is a slot at risk right now (no WhatsApp number, app pool down, etc.)?

  HOW TO USE
      1. Set @Slot1/@Slot2/@Slot3 to match appsettings WhatsApp:PendingReminderTimes.
         Shipped default: 08:59, 13:00, 18:00 (IST). Unused slots -> NULL.
      2. Set @Template to match WhatsApp:PendingReminderTemplate
         (default 'pending_approval_summary').
      3. Run. Everything is reported in IST.

  HOW THE REMINDER WORKS (so the output reads correctly)
      - Recipients come from IPendingApprovalSummaryService: RequestApprovers rows
        with Status = 'pending' whose parent Request.Status is one of
        'Open' / 'Pending Approval' / 'Partial Approved' - the SAME filter as
        /Requests/Approval?status=Pending, so the numbers must agree.
      - Approvers with zero pending, or with no WhatsApp number, are skipped by
        design - they appear here as "SKIPPED", not as failures.
      - Each reminder is logged in WhatsAppMessageHistory with RequestId = NULL,
        MessageType = 'template:<template>' and CreatedBy = the approver's UserId.
        That row is also the scheduler's own duplicate guard.
      - The service catches up a slot missed by an app-pool recycle, but only for
        TODAY; a slot missed on an earlier day is never resent.

  NOTE
      Table names are the EXACT SQL names from the EF DbContext, including the
      SINGULAR WhatsAppMessageHistory.
==============================================================================*/

SET NOCOUNT ON;
SET QUOTED_IDENTIFIER ON;   -- required: the script uses XML data type methods

DECLARE @Slot1    VARCHAR(5)   = '08:59';                     -- WhatsApp:PendingReminderTimes[0]
DECLARE @Slot2    VARCHAR(5)   = '13:00';                     -- [1]  (NULL if unused)
DECLARE @Slot3    VARCHAR(5)   = '18:00';                     -- [2]  (NULL if unused)
DECLARE @Template VARCHAR(100) = 'pending_approval_summary';  -- WhatsApp:PendingReminderTemplate
DECLARE @Days     INT          = 7;                           -- history window, in days

DECLARE @MsgType VARCHAR(150) = 'template:' + @Template;
DECLARE @NowIst  DATETIME2    = CAST(SYSDATETIMEOFFSET() AT TIME ZONE 'India Standard Time' AS DATETIME2);
DECLARE @Today   DATE         = CAST(@NowIst AS DATE);

-- The configured slots, expanded over the reporting window.
DECLARE @Slots TABLE (SlotTime VARCHAR(5) NOT NULL);
INSERT INTO @Slots (SlotTime)
SELECT v FROM (VALUES (@Slot1), (@Slot2), (@Slot3)) s(v) WHERE v IS NOT NULL;

DECLARE @Expected TABLE (SlotDate DATE, SlotTime VARCHAR(5), SlotStart DATETIME2, SlotEnd DATETIME2);

;WITH d AS (
    SELECT DATEADD(DAY, -@Days + 1, @Today) AS SlotDate
    UNION ALL
    SELECT DATEADD(DAY, 1, SlotDate) FROM d WHERE SlotDate < @Today
)
INSERT INTO @Expected (SlotDate, SlotTime, SlotStart, SlotEnd)
SELECT d.SlotDate,
       s.SlotTime,
       CAST(CONVERT(VARCHAR(10), d.SlotDate, 120) + ' ' + s.SlotTime AS DATETIME2),
       -- Window end = the next configured slot that day, else midnight. This mirrors
       -- the service's duplicate-protection window exactly. ("HH:mm" sorts correctly
       -- as text, so the next slot is just the smallest greater string.)
       CASE WHEN nx.NextSlotTime IS NULL
            THEN CAST(CONVERT(VARCHAR(10), DATEADD(DAY, 1, d.SlotDate), 120) + ' 00:00' AS DATETIME2)
            ELSE CAST(CONVERT(VARCHAR(10), d.SlotDate, 120) + ' ' + nx.NextSlotTime AS DATETIME2)
       END
FROM d
CROSS JOIN @Slots s
OUTER APPLY (SELECT NextSlotTime = MIN(s2.SlotTime)
             FROM @Slots s2
             WHERE s2.SlotTime > s.SlotTime) nx
OPTION (MAXRECURSION 400);

-- Every reminder row actually written, stamped with its IST time.
DECLARE @Sent TABLE (Id INT, ApproverUserId INT, MobileNumber VARCHAR(50), Status VARCHAR(30),
                     MessageId VARCHAR(200), MessageText NVARCHAR(MAX), SentIst DATETIME2);
INSERT INTO @Sent
SELECT h.Id, h.CreatedBy, h.MobileNumber, h.Status, h.MessageId, h.MessageText,
       CAST(h.CreatedDate AT TIME ZONE 'India Standard Time' AS DATETIME2)
FROM dbo.WhatsAppMessageHistory h
WHERE h.Direction = 'Outbound'
  AND h.MessageType IN (@MsgType, 'pending_reminder', 'template:pending_approval_reminder')
  AND h.CreatedDate >= DATEADD(DAY, -@Days - 1, SYSDATETIMEOFFSET());

PRINT '=== ApproveFlow pending-reminder scheduler check (IST) ===';
PRINT 'Now (IST)  : ' + CONVERT(VARCHAR(19), @NowIst, 120);
PRINT 'Template   : ' + @MsgType;


/*------------------------------------------------------------------------------
  1) SLOT LEDGER - did each configured slot fire, on each day of the window?
     PENDING (future)  : not due yet today.
     NOT FIRED         : slot passed with no reminder logged -> app pool was down,
                         the hosted service is not registered, or nobody had
                         anything pending (cross-check with section 3).
------------------------------------------------------------------------------*/
SELECT
    [Section]     = '1. Slot ledger',
    e.SlotDate,
    e.SlotTime,
    Recipients    = COUNT(s.Id),
    Sent          = SUM(CASE WHEN s.Status =  'Sent' THEN 1 ELSE 0 END),
    Failed        = SUM(CASE WHEN s.Status <> 'Sent' THEN 1 ELSE 0 END),
    FirstSendIST  = CONVERT(VARCHAR(19), MIN(s.SentIst), 120),
    LastSendIST   = CONVERT(VARCHAR(19), MAX(s.SentIst), 120),
    LagMinutes    = DATEDIFF(MINUTE, e.SlotStart, MIN(s.SentIst)),
    Verdict       = CASE
                        WHEN COUNT(s.Id) > 0 AND SUM(CASE WHEN s.Status <> 'Sent' THEN 1 ELSE 0 END) = 0
                            THEN 'FIRED - all accepted'
                        WHEN COUNT(s.Id) > 0
                            THEN 'FIRED - some sends failed'
                        WHEN e.SlotStart > @NowIst
                            THEN 'PENDING - not due yet'
                        ELSE 'NOT FIRED - no reminder logged for this slot'
                    END
FROM @Expected e
LEFT JOIN @Sent s ON s.SentIst >= e.SlotStart AND s.SentIst < e.SlotEnd
GROUP BY e.SlotDate, e.SlotTime, e.SlotStart
ORDER BY e.SlotDate DESC, e.SlotTime DESC;


/*------------------------------------------------------------------------------
  2) NEXT SLOT - what the scheduler will do next, and whether today is complete.
------------------------------------------------------------------------------*/
SELECT
    [Section]      = '2. Next slot',
    NowIST         = CONVERT(VARCHAR(19), @NowIst, 120),
    NextSlotIST    = CONVERT(VARCHAR(19), n.NextSlot, 120),
    MinutesToGo    = DATEDIFF(MINUTE, @NowIst, n.NextSlot),
    SlotsToday     = (SELECT COUNT(*) FROM @Slots),
    SlotsDueToday  = (SELECT COUNT(*) FROM @Expected WHERE SlotDate = @Today AND SlotStart <= @NowIst),
    SlotsFiredToday= (SELECT COUNT(DISTINCT e.SlotTime)
                      FROM @Expected e
                      JOIN @Sent s ON s.SentIst >= e.SlotStart AND s.SentIst < e.SlotEnd
                      WHERE e.SlotDate = @Today),
    CatchUpRisk    = CASE WHEN (SELECT COUNT(*) FROM @Expected WHERE SlotDate = @Today AND SlotStart <= @NowIst)
                             > (SELECT COUNT(DISTINCT e.SlotTime)
                                FROM @Expected e
                                JOIN @Sent s ON s.SentIst >= e.SlotStart AND s.SentIst < e.SlotEnd
                                WHERE e.SlotDate = @Today)
                          THEN 'A due slot has NOT fired today - restart the app pool to trigger catch-up, or check section 3'
                          ELSE 'OK' END
FROM (
    SELECT NextSlot = ISNULL(
        (SELECT MIN(SlotStart) FROM @Expected WHERE SlotDate = @Today AND SlotStart > @NowIst),
        (SELECT MIN(CAST(CONVERT(VARCHAR(10), DATEADD(DAY,1,@Today), 120) + ' ' + SlotTime AS DATETIME2)) FROM @Slots))
) n;


/*------------------------------------------------------------------------------
  3) WHO SHOULD BE REMINDED RIGHT NOW - live pending state per approver.
     This is the exact scope of the reminder, and the same scope as
     /Requests/Approval?status=Pending, so PendingCount must match that page.
------------------------------------------------------------------------------*/
;WITH Pending AS (
    SELECT DISTINCT ra.ApproverUserId, ra.RequestId, rt.TypeName
    FROM dbo.RequestApprovers ra
    JOIN dbo.Requests     r  ON r.RequestId     = ra.RequestId
    JOIN dbo.RequestTypes rt ON rt.RequestTypeId = r.RequestTypeId
    WHERE ra.Status = 'pending'
      AND r.Status IN ('Open', 'Pending Approval', 'Partial Approved')
)
SELECT
    [Section]     = '3. Expected recipients (live)',
    p.ApproverUserId,
    Approver      = u.FullName,
    u.WhatsAppNumber,
    u.Status,
    PendingCount  = COUNT(*),
    ByType        = STUFF((
                        SELECT ' | ' + p2.TypeName + ' : ' + CAST(COUNT(*) AS VARCHAR(10))
                        FROM Pending p2
                        WHERE p2.ApproverUserId = p.ApproverUserId
                        GROUP BY p2.TypeName
                        ORDER BY COUNT(*) DESC, p2.TypeName
                        FOR XML PATH(''), TYPE).value('.', 'NVARCHAR(MAX)'), 1, 3, ''),
    WillBeSent    = CASE WHEN u.WhatsAppNumber IS NULL OR LTRIM(RTRIM(u.WhatsAppNumber)) = ''
                         THEN 'NO - skipped, no WhatsApp number'
                         ELSE 'YES' END
FROM Pending p
JOIN dbo.Users u ON u.UserId = p.ApproverUserId
GROUP BY p.ApproverUserId, u.FullName, u.WhatsAppNumber, u.Status
ORDER BY COUNT(*) DESC;


/*------------------------------------------------------------------------------
  4) LAST FIRED SLOT - expected vs actual, per approver. The "who got missed" list.
     Compares the live pending state against what actually went out in the most
     recent slot. An approver whose pending work arrived AFTER that slot shows as
     "NOT REMINDED" and is legitimately waiting for the next slot.
------------------------------------------------------------------------------*/
DECLARE @LastSlotStart DATETIME2, @LastSlotEnd DATETIME2;
SELECT TOP 1 @LastSlotStart = e.SlotStart, @LastSlotEnd = e.SlotEnd
FROM @Expected e
WHERE e.SlotStart <= @NowIst
ORDER BY e.SlotStart DESC;

PRINT 'Last due slot (IST): ' + ISNULL(CONVERT(VARCHAR(19), @LastSlotStart, 120), '(none in window)');

;WITH Pending AS (
    SELECT ra.ApproverUserId, COUNT(DISTINCT ra.RequestId) AS PendingCount
    FROM dbo.RequestApprovers ra
    JOIN dbo.Requests r ON r.RequestId = ra.RequestId
    WHERE ra.Status = 'pending'
      AND r.Status IN ('Open', 'Pending Approval', 'Partial Approved')
    GROUP BY ra.ApproverUserId
)
SELECT
    [Section]     = '4. Last slot: expected vs actual',
    SlotIST       = CONVERT(VARCHAR(19), @LastSlotStart, 120),
    ApproverUserId= ISNULL(p.ApproverUserId, s.ApproverUserId),
    Approver      = u.FullName,
    u.WhatsAppNumber,
    PendingNow    = ISNULL(p.PendingCount, 0),
    SendStatus    = ISNULL(s.Status, '(no message)'),
    SentAtIST     = CONVERT(VARCHAR(19), s.SentIst, 120),
    MessageText   = LEFT(s.MessageText, 120),
    Verdict       = CASE
                        WHEN s.Id IS NOT NULL AND s.Status =  'Sent' THEN 'REMINDED'
                        WHEN s.Id IS NOT NULL                        THEN 'ATTEMPTED BUT FAILED - check template / number'
                        WHEN u.WhatsAppNumber IS NULL OR LTRIM(RTRIM(u.WhatsAppNumber)) = ''
                                                                     THEN 'SKIPPED - no WhatsApp number'
                        ELSE 'NOT REMINDED - either pending arrived after the slot, or the slot did not fire'
                    END
FROM Pending p
FULL OUTER JOIN (
    SELECT * FROM @Sent
    WHERE @LastSlotStart IS NOT NULL AND SentIst >= @LastSlotStart AND SentIst < @LastSlotEnd
) s ON s.ApproverUserId = p.ApproverUserId
LEFT JOIN dbo.Users u ON u.UserId = ISNULL(p.ApproverUserId, s.ApproverUserId)
ORDER BY CASE WHEN s.Id IS NULL THEN 0 ELSE 1 END, u.FullName;


/*------------------------------------------------------------------------------
  5) FAILED REMINDERS in the window - the short list to act on.
     A reminder is a TEMPLATE, so the 24-hour window is NOT the cause here.
     Usual causes: template not approved in Meta, wrong parameter count
     (pending_approval_summary takes 5), wrong language code, or a dead number.
------------------------------------------------------------------------------*/
SELECT
    [Section]   = '5. Failed reminders',
    Approver    = u.FullName,
    s.MobileNumber,
    s.Status,
    SentAtIST   = CONVERT(VARCHAR(19), s.SentIst, 120),
    MessageText = LEFT(s.MessageText, 200)
FROM @Sent s
LEFT JOIN dbo.Users u ON u.UserId = s.ApproverUserId
WHERE s.Status <> 'Sent'
ORDER BY s.SentIst DESC;


/*------------------------------------------------------------------------------
  6) Reminders stuck in the transport outbox (never reached Meta).
------------------------------------------------------------------------------*/
SELECT
    [Section]      = '6. Reminders in outbox',
    o.Id,
    o.Status,
    o.MessageType,
    o.ToNumber,
    o.Attempts,
    QueuedIST      = CONVERT(VARCHAR(19), o.CreatedDate   AT TIME ZONE 'India Standard Time', 120),
    NextAttemptIST = CONVERT(VARCHAR(19), o.NextAttemptAt AT TIME ZONE 'India Standard Time', 120),
    LastError      = LEFT(o.LastError, 300)
FROM dbo.WhatsAppOutbox o
WHERE o.MessageType IN (@MsgType, 'pending_reminder', 'template:pending_approval_reminder')
ORDER BY o.CreatedDate DESC;


/*------------------------------------------------------------------------------
  7) HEALTH SUMMARY.
------------------------------------------------------------------------------*/
SELECT
    [Section]          = '7. Health summary',
    ConfiguredSlots    = (SELECT COUNT(*) FROM @Slots),
    SlotsFiredToday    = (SELECT COUNT(DISTINCT e.SlotTime) FROM @Expected e
                          JOIN @Sent s ON s.SentIst >= e.SlotStart AND s.SentIst < e.SlotEnd
                          WHERE e.SlotDate = @Today),
    SlotsDueToday      = (SELECT COUNT(*) FROM @Expected WHERE SlotDate = @Today AND SlotStart <= @NowIst),
    RemindersSentToday = (SELECT COUNT(*) FROM @Sent WHERE CAST(SentIst AS DATE) = @Today AND Status =  'Sent'),
    RemindersFailedTdy = (SELECT COUNT(*) FROM @Sent WHERE CAST(SentIst AS DATE) = @Today AND Status <> 'Sent'),
    ApproversPendingNow= (SELECT COUNT(DISTINCT ra.ApproverUserId)
                          FROM dbo.RequestApprovers ra
                          JOIN dbo.Requests r ON r.RequestId = ra.RequestId
                          WHERE ra.Status = 'pending'
                            AND r.Status IN ('Open', 'Pending Approval', 'Partial Approved')),
    UnreachableApprovers = (SELECT COUNT(DISTINCT ra.ApproverUserId)
                          FROM dbo.RequestApprovers ra
                          JOIN dbo.Requests r ON r.RequestId = ra.RequestId
                          JOIN dbo.Users    u ON u.UserId    = ra.ApproverUserId
                          WHERE ra.Status = 'pending'
                            AND r.Status IN ('Open', 'Pending Approval', 'Partial Approved')
                            AND (u.WhatsAppNumber IS NULL OR LTRIM(RTRIM(u.WhatsAppNumber)) = '')),
    LastReminderIST    = (SELECT CONVERT(VARCHAR(19), MAX(SentIst), 120) FROM @Sent);
