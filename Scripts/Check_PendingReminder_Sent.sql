/* ApproveFlow - was the WhatsApp PENDING-APPROVAL reminder sent? READ-ONLY. */
SET NOCOUNT ON;

DECLARE @Template VARCHAR(100) = 'pending_approval_summary';   -- WhatsApp:PendingReminderTemplate
DECLARE @Days     INT          = 3;                            -- how far back to look

DECLARE @Type VARCHAR(150) = 'template:' + @Template;
DECLARE @Ist  DATETIME2 = CAST(SYSDATETIMEOFFSET() AT TIME ZONE 'India Standard Time' AS DATETIME2);

-- 1) Did it run? one row per day+slot actually sent
SELECT  CAST(t.Ist AS DATE)                        AS SlotDate,
        CONVERT(VARCHAR(5), t.Ist, 108)            AS SlotTime,
        COUNT(*)                                   AS Recipients,
        SUM(CASE WHEN h.Status IN ('Sent','Delivered','Read') THEN 1 ELSE 0 END)     AS Sent,
        SUM(CASE WHEN h.Status NOT IN ('Sent','Delivered','Read') THEN 1 ELSE 0 END) AS Failed
FROM    WhatsAppMessageHistory h
CROSS APPLY (SELECT CAST(h.CreatedDate AT TIME ZONE 'India Standard Time' AS DATETIME2) AS Ist) t
WHERE   h.MessageType = @Type
  AND   h.CreatedDate >= DATEADD(DAY, -@Days, SYSDATETIMEOFFSET())
GROUP BY CAST(t.Ist AS DATE), CONVERT(VARCHAR(5), t.Ist, 108)
ORDER BY SlotDate DESC, SlotTime DESC;

-- 2) Who has pending approvals right now, and did they get today's reminder?
SELECT  u.UserId, u.FullName, u.WhatsAppNumber,
        COUNT(DISTINCT ra.RequestId) AS PendingCount,
        MAX(CASE WHEN h.Status IN ('Sent','Delivered','Read') THEN CONVERT(VARCHAR(5), CAST(h.CreatedDate AT TIME ZONE 'India Standard Time' AS DATETIME2), 108) END) AS LastReminderToday,
        CASE WHEN u.WhatsAppNumber IS NULL OR u.WhatsAppNumber = '' THEN 'SKIPPED - no number'
             WHEN MAX(CASE WHEN h.Status IN ('Sent','Delivered','Read') THEN 1 END) IS NULL THEN 'NOT SENT TODAY'
             ELSE 'SENT' END AS Result
FROM    RequestApprovers ra
JOIN    Requests r ON r.RequestId = ra.RequestId
JOIN    Users    u ON u.UserId = ra.ApproverUserId
LEFT JOIN WhatsAppMessageHistory h
       ON h.CreatedBy = u.UserId AND h.MessageType = @Type
      AND CAST(CAST(h.CreatedDate AT TIME ZONE 'India Standard Time' AS DATETIME2) AS DATE) = CAST(@Ist AS DATE)
WHERE   ra.Status = 'pending'
  AND   r.Status IN ('Open', 'Pending Approval', 'Partial Approved')
GROUP BY u.UserId, u.FullName, u.WhatsAppNumber
ORDER BY Result, PendingCount DESC;

-- 3) Raw rows for today (errors show up in MessageText when Status = 'Failed')
SELECT  CAST(h.CreatedDate AT TIME ZONE 'India Standard Time' AS DATETIME2) AS SentAtIst,
        h.CreatedBy AS UserId, h.MobileNumber, h.Status, h.MessageId, h.MessageText
FROM    WhatsAppMessageHistory h
WHERE   h.MessageType = @Type
  AND   CAST(CAST(h.CreatedDate AT TIME ZONE 'India Standard Time' AS DATETIME2) AS DATE) = CAST(@Ist AS DATE)
ORDER BY SentAtIst DESC;
