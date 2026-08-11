/* =====================================================================
   ApproveFlow — WhatsApp pending-approval reminder health check
   ---------------------------------------------------------------------
   Run against the SERVER database (dev and server have separate DBs).
   Slots (IST): 08:59, 13:00, 18:00 -> template 'pending_approval_summary'.
   CreatedDate/NextAttemptAt are datetimeoffset; everything below is
   converted to India Standard Time so it reads in local wall-clock.
   ===================================================================== */

DECLARE @nowIst   datetime2 = CAST(SYSDATETIMEOFFSET() AT TIME ZONE 'India Standard Time' AS datetime2);
DECLARE @todayIst date      = CAST(@nowIst AS date);

/* -- 0) Where are we now? ------------------------------------------- */
SELECT @nowIst AS ServerNow_IST, @todayIst AS Today_IST;


/* -- 1) Today's reminder sends (raw rows) --------------------------- */
SELECT  h.Id,
        CAST(h.CreatedDate AT TIME ZONE 'India Standard Time' AS datetime2) AS SentAt_IST,
        h.MessageType,
        h.Status,
        h.MobileNumber,
        h.MessageId
FROM    dbo.WhatsAppMessageHistory h
WHERE   h.Direction  = 'Outbound'
  AND   h.MessageType = 'template:pending_approval_summary'
  AND   CAST(CAST(h.CreatedDate AT TIME ZONE 'India Standard Time' AS datetime2) AS date) = @todayIst
ORDER BY h.CreatedDate;


/* -- 2) Slot summary + PASS/FAIL per slot --------------------------- */
;WITH sent AS (
    SELECT CAST(h.CreatedDate AT TIME ZONE 'India Standard Time' AS datetime2) AS IstTime
    FROM   dbo.WhatsAppMessageHistory h
    WHERE  h.Direction  = 'Outbound'
      AND  h.MessageType = 'template:pending_approval_summary'
      AND  CAST(CAST(h.CreatedDate AT TIME ZONE 'India Standard Time' AS datetime2) AS date) = @todayIst
),
slots AS (
    /* SlotTime = when it should fire; WinStart/WinEnd = counting window */
    SELECT '08:59' AS Slot, CAST('08:59' AS time) AS SlotTime, CAST('00:00'    AS time) AS WinStart, CAST('13:00' AS time) AS WinEnd
    UNION ALL SELECT '13:00', CAST('13:00' AS time), CAST('13:00' AS time), CAST('18:00' AS time)
    UNION ALL SELECT '18:00', CAST('18:00' AS time), CAST('18:00' AS time), CAST('23:59:59.9999999' AS time)
)
SELECT  s.Slot,
        s.SlotTime,
        cnt.SentCount,
        CASE
            WHEN CAST(@nowIst AS time) < s.SlotTime THEN 'Upcoming (not due yet)'
            WHEN cnt.SentCount > 0                  THEN 'OK - sent'
            ELSE                                          '*** MISSING - not sent ***'
        END AS Result
FROM    slots s
CROSS APPLY (
    SELECT COUNT(*) AS SentCount
    FROM   sent
    WHERE  CAST(sent.IstTime AS time) >= s.WinStart
      AND  CAST(sent.IstTime AS time) <  s.WinEnd
) cnt
ORDER BY s.SlotTime;


/* -- 3) Outbox: anything stuck / retrying / abandoned --------------- */
SELECT  o.Id,
        CAST(o.CreatedDate    AT TIME ZONE 'India Standard Time' AS datetime2) AS Queued_IST,
        o.MessageType,
        o.ToNumber,
        o.Status,               -- Pending / Sent / Abandoned
        o.Attempts,
        CAST(o.NextAttemptAt  AT TIME ZONE 'India Standard Time' AS datetime2) AS NextTry_IST,
        o.LastError
FROM    dbo.WhatsAppOutbox o
ORDER BY o.Id DESC;


/* -- 4) (Optional) Approvers who currently have pending items --------
   Approximation of the reminder audience: an empty result here means a
   slot legitimately sends nothing. This filters on the approver row only
   (a.Status='pending'); the app additionally requires the parent request
   to be in a pending status, so the real audience is <= this count.      */
SELECT  a.ApproverUserId,
        u.FullName,
        COUNT(DISTINCT a.RequestId) AS PendingRequests
FROM    dbo.RequestApprovers a
JOIN    dbo.Users u ON u.Id = a.ApproverUserId
WHERE   a.Status = 'pending'
GROUP BY a.ApproverUserId, u.FullName
ORDER BY PendingRequests DESC;
