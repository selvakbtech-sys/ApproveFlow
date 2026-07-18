/* ===========================================================================
   ApproveFlow - reset request data back to yesterday's state
   TARGET: the SERVER's ApproveFlow database (rehearse on Development first).

   KEEPS  : REQ-26-000001, REQ-26-000002, and every master table (Users, Roles,
            Locations, Departments, RequestTypes, Workflows, Workflow Steps,
            Notification Roles, User Mappings) - none are touched.
   DELETES: every other request and ALL of its details - approvers, action log
            (History), documents, approval attachments, approval replies, view
            tokens, in-app notifications, WhatsApp message history, WhatsApp
            pending approve/clarification sessions.

   *** TAKE A DATABASE BACKUP FIRST. THIS CANNOT BE UNDONE. ***

   WHY EVERY CHILD TABLE IS DELETED EXPLICITLY
   -------------------------------------------
   Four of the foreign keys to Requests are DISABLED (is_disabled = 1):
   RequestActionLog, RequestApprovers, RequestDocuments and Notifications. A
   disabled FK does NOT cascade, so deleting a request leaves its History,
   approvers and documents behind. Because RequestNumber is derived from the
   identity, reseeding then hands the same id to a NEW request, which silently
   ADOPTS those leftovers - the new request shows an older request's History
   and attachments. So nothing here relies on ON DELETE CASCADE: every child is
   deleted by hand, and step 4 sweeps up any pre-existing orphans left behind by
   an earlier delete that did not cascade.
   =========================================================================== */

SET XACT_ABORT ON;
SET NOCOUNT ON;

/* -- Which requests survive. Edit this list if you want to keep more. ------ */
DECLARE @Keep TABLE (RequestNumber nvarchar(50) PRIMARY KEY);
INSERT INTO @Keep (RequestNumber) VALUES (N'REQ-26-000001'), (N'REQ-26-000002');

DECLARE @Doomed TABLE (RequestId int PRIMARY KEY);
INSERT INTO @Doomed (RequestId)
SELECT r.RequestId
  FROM dbo.Requests r
 WHERE r.RequestNumber NOT IN (SELECT RequestNumber FROM @Keep);

/* -- Preview: read this before you let it run ------------------------------ */
SELECT 'WILL DELETE' AS Action, r.RequestId, r.RequestNumber, r.Title, r.Status
  FROM dbo.Requests r JOIN @Doomed d ON d.RequestId = r.RequestId
 ORDER BY r.RequestId;

SELECT 'WILL KEEP' AS Action, r.RequestId, r.RequestNumber, r.Title, r.Status
  FROM dbo.Requests r
 WHERE r.RequestNumber IN (SELECT RequestNumber FROM @Keep)
 ORDER BY r.RequestId;

/* -- Safety: refuse to run unless BOTH keepers exist ----------------------- */
IF (SELECT COUNT(*) FROM dbo.Requests
     WHERE RequestNumber IN (SELECT RequestNumber FROM @Keep)) <> 2
BEGIN
    RAISERROR('ABORTED: REQ-26-000001 and/or REQ-26-000002 not found. Nothing was deleted.', 16, 1);
    RETURN;
END

BEGIN TRANSACTION;

    /* -- 1. Every child of the doomed requests, deleted EXPLICITLY --------- */

    DELETE x FROM dbo.RequestActionLog x    JOIN @Doomed d ON d.RequestId = x.RequestId;  -- History tab
    DELETE x FROM dbo.RequestDocuments x    JOIN @Doomed d ON d.RequestId = x.RequestId;  -- Documents tab
    DELETE x FROM dbo.RequestApprovers x    JOIN @Doomed d ON d.RequestId = x.RequestId;  -- Approval Chain tab
    DELETE x FROM dbo.ApprovalAttachments x JOIN @Doomed d ON d.RequestId = x.RequestId;  -- files added on approve
    DELETE x FROM dbo.ApprovalReplies x     JOIN @Doomed d ON d.RequestId = x.RequestId;  -- WhatsApp reply media
    DELETE x FROM dbo.RequestViewTokens x   JOIN @Doomed d ON d.RequestId = x.RequestId;  -- /r/{token} share links
    DELETE x FROM dbo.Notifications x       JOIN @Doomed d ON d.RequestId = x.RequestId;  -- in-app bell
    DELETE x FROM dbo.WhatsAppMessageHistory x        JOIN @Doomed d ON d.RequestId = x.RequestId;
    DELETE x FROM dbo.WhatsAppPendingClarifications x JOIN @Doomed d ON d.RequestId = x.RequestId;

    /* -- 2. The requests themselves --------------------------------------- */
    DELETE r FROM dbo.Requests r JOIN @Doomed d ON d.RequestId = r.RequestId;

    /* -- 3. Orphan sweep --------------------------------------------------
       Rows left by any EARLIER delete that did not cascade. Without this they
       would be adopted by a future request that reuses their RequestId. */

    DELETE x FROM dbo.RequestActionLog x    WHERE NOT EXISTS (SELECT 1 FROM dbo.Requests r WHERE r.RequestId = x.RequestId);
    DELETE x FROM dbo.RequestDocuments x    WHERE NOT EXISTS (SELECT 1 FROM dbo.Requests r WHERE r.RequestId = x.RequestId);
    DELETE x FROM dbo.RequestApprovers x    WHERE NOT EXISTS (SELECT 1 FROM dbo.Requests r WHERE r.RequestId = x.RequestId);
    DELETE x FROM dbo.ApprovalAttachments x WHERE NOT EXISTS (SELECT 1 FROM dbo.Requests r WHERE r.RequestId = x.RequestId);
    DELETE x FROM dbo.ApprovalReplies x     WHERE NOT EXISTS (SELECT 1 FROM dbo.Requests r WHERE r.RequestId = x.RequestId);
    DELETE x FROM dbo.RequestViewTokens x   WHERE NOT EXISTS (SELECT 1 FROM dbo.Requests r WHERE r.RequestId = x.RequestId);
    DELETE x FROM dbo.WhatsAppPendingClarifications x WHERE NOT EXISTS (SELECT 1 FROM dbo.Requests r WHERE r.RequestId = x.RequestId);
    DELETE x FROM dbo.Notifications x
     WHERE x.RequestId IS NOT NULL
       AND NOT EXISTS (SELECT 1 FROM dbo.Requests r WHERE r.RequestId = x.RequestId);
    DELETE x FROM dbo.WhatsAppMessageHistory x
     WHERE x.RequestId IS NOT NULL
       AND NOT EXISTS (SELECT 1 FROM dbo.Requests r WHERE r.RequestId = x.RequestId);

COMMIT TRANSACTION;

/* -- OPTIONAL: WhatsApp rows that carry no RequestId ----------------------
   The old build logged Notification Alerts and confirmations with RequestId
   NULL, so they cannot be tied to a deleted request and survive the sweep
   above. Uncomment to clear today's. Leaving them costs nothing but noise.

   DELETE FROM dbo.WhatsAppMessageHistory
    WHERE RequestId IS NULL
      AND CreatedDate >= CAST(CAST(SYSDATETIMEOFFSET() AS date) AS datetimeoffset);
*/

/* -- REQUIRED: next new request must be REQ-26-000003 ---------------------
   RequestNumber is built from the identity: REQ-{yy}-{RequestId:D6}. Deleting
   rows does NOT rewind the identity, so without this the next request would
   continue from the old high-water mark. Reseeding to the highest surviving id
   (2) makes the next insert id 3 -> REQ-26-000003.
   This is SAFE only because step 3 removed every orphan first - otherwise the
   new request would inherit the old id's History and Documents. */
DECLARE @MaxId int = ISNULL((SELECT MAX(RequestId) FROM dbo.Requests), 0);
DBCC CHECKIDENT ('dbo.Requests', RESEED, @MaxId);

/* -- Verify --------------------------------------------------------------- */
SELECT 'requests remaining' AS Check_, COUNT(*) AS N FROM dbo.Requests;   -- expect 2
SELECT r.RequestId, r.RequestNumber, r.Title, r.Status FROM dbo.Requests r ORDER BY r.RequestId;

SELECT 'orphan action log (History)' AS Check_, COUNT(*) AS N FROM dbo.RequestActionLog          WHERE NOT EXISTS (SELECT 1 FROM dbo.Requests r WHERE r.RequestId = RequestActionLog.RequestId)
UNION ALL SELECT 'orphan approvers',     COUNT(*) FROM dbo.RequestApprovers    WHERE NOT EXISTS (SELECT 1 FROM dbo.Requests r WHERE r.RequestId = RequestApprovers.RequestId)
UNION ALL SELECT 'orphan documents',     COUNT(*) FROM dbo.RequestDocuments    WHERE NOT EXISTS (SELECT 1 FROM dbo.Requests r WHERE r.RequestId = RequestDocuments.RequestId)
UNION ALL SELECT 'orphan appr. attach',  COUNT(*) FROM dbo.ApprovalAttachments WHERE NOT EXISTS (SELECT 1 FROM dbo.Requests r WHERE r.RequestId = ApprovalAttachments.RequestId)
UNION ALL SELECT 'orphan appr. replies', COUNT(*) FROM dbo.ApprovalReplies     WHERE NOT EXISTS (SELECT 1 FROM dbo.Requests r WHERE r.RequestId = ApprovalReplies.RequestId)
UNION ALL SELECT 'orphan view tokens',   COUNT(*) FROM dbo.RequestViewTokens   WHERE NOT EXISTS (SELECT 1 FROM dbo.Requests r WHERE r.RequestId = RequestViewTokens.RequestId)
UNION ALL SELECT 'orphan notifications', COUNT(*) FROM dbo.Notifications       WHERE RequestId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.Requests r WHERE r.RequestId = Notifications.RequestId)
UNION ALL SELECT 'orphan wa history',    COUNT(*) FROM dbo.WhatsAppMessageHistory        WHERE RequestId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM dbo.Requests r WHERE r.RequestId = WhatsAppMessageHistory.RequestId)
UNION ALL SELECT 'orphan wa sessions',   COUNT(*) FROM dbo.WhatsAppPendingClarifications WHERE NOT EXISTS (SELECT 1 FROM dbo.Requests r WHERE r.RequestId = WhatsAppPendingClarifications.RequestId);
-- every N above must be 0

/* Per-request child counts for the survivors - each should show ONLY its own
   rows. If a kept request suddenly has extra History, something adopted it. */
SELECT r.RequestId, r.RequestNumber,
       (SELECT COUNT(*) FROM dbo.RequestActionLog x WHERE x.RequestId = r.RequestId) AS History_,
       (SELECT COUNT(*) FROM dbo.RequestDocuments x WHERE x.RequestId = r.RequestId) AS Docs_,
       (SELECT COUNT(*) FROM dbo.RequestApprovers x WHERE x.RequestId = r.RequestId) AS Approvers_
  FROM dbo.Requests r ORDER BY r.RequestId;

/* Masters untouched. */
SELECT 'Users' AS Master_, COUNT(*) AS N FROM dbo.Users
UNION ALL SELECT 'Roles',         COUNT(*) FROM dbo.Roles
UNION ALL SELECT 'Locations',     COUNT(*) FROM dbo.Locations
UNION ALL SELECT 'Departments',   COUNT(*) FROM dbo.Departments
UNION ALL SELECT 'RequestTypes',  COUNT(*) FROM dbo.RequestTypes
UNION ALL SELECT 'Workflows',     COUNT(*) FROM dbo.WorkflowDefinitions
UNION ALL SELECT 'WorkflowSteps', COUNT(*) FROM dbo.WorkflowSteps;
