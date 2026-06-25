warning: in the working copy of 'Services/DashboardService.cs', LF will be replaced by CRLF the next time Git touches it
warning: in the working copy of 'Services/RequestService.cs', LF will be replaced by CRLF the next time Git touches it
warning: in the working copy of 'Views/Dashboard/_RequestsTab.cshtml', LF will be replaced by CRLF the next time Git touches it
warning: in the working copy of 'Views/Dashboard/_TerritoryTab.cshtml', LF will be replaced by CRLF the next time Git touches it
warning: in the working copy of 'Views/Requests/Aging.cshtml', LF will be replaced by CRLF the next time Git touches it
warning: in the working copy of 'Views/Requests/Detail.cshtml', LF will be replaced by CRLF the next time Git touches it
warning: in the working copy of 'Views/Requests/Index.cshtml', LF will be replaced by CRLF the next time Git touches it
[1mdiff --git a/Services/DashboardService.cs b/Services/DashboardService.cs[m
[1mindex d6bc3c6..a6eb753 100644[m
[1m--- a/Services/DashboardService.cs[m
[1m+++ b/Services/DashboardService.cs[m
[36m@@ -21,16 +21,16 @@[m [mpublic class DashboardService : IDashboardService[m
     {[m
         bool isApprover = !isAdmin && await _db.RequestApprovers.AnyAsync(a => a.ApproverUserId == userId);[m
 [m
[31m-        var myOpen = await _db.Requests.CountAsync(r => r.RequestorId == userId && r.Status == "Open");[m
[32m+[m[32m        var myPendingApproval = await _db.Requests.CountAsync(r => r.RequestorId == userId && r.Status == "Pending Approval");[m
         var myPartial = await _db.Requests.CountAsync(r => r.RequestorId == userId && r.Status == "Partial Approved");[m
         var vm = new DashboardVM[m
         {[m
             MyPendingApprovals = await _db.RequestApprovers.CountAsync(a => a.ApproverUserId == userId && a.Status == "pending"),[m
             MyTotal = await _db.Requests.CountAsync(r => r.RequestorId == userId && r.Status != "Deleted"),[m
             MyDraft = await _db.Requests.CountAsync(r => r.RequestorId == userId && r.Status == "Draft"),[m
[31m-            MyOpen = myOpen,[m
[32m+[m[32m            MyOpen = myPendingApproval,[m
             MyPartialApproved = myPartial,[m
[31m-            MyPending = myOpen + myPartial,[m
[32m+[m[32m            MyPending = myPendingApproval + myPartial,[m
             MyApproved = await _db.Requests.CountAsync(r => r.RequestorId == userId && r.Status == "Approved"),[m
             MyRejected = await _db.Requests.CountAsync(r => r.RequestorId == userId && r.Status == "Rejected"),[m
             MyClarification = await _db.Requests.CountAsync(r => r.RequestorId == userId && r.Status == "Clarification Needed"),[m
[36m@@ -49,18 +49,18 @@[m [mpublic class DashboardService : IDashboardService[m
 [m
         if (isAdmin)[m
         {[m
[31m-            var adminOpen = await _db.Requests.CountAsync(r => r.Status == "Open");[m
[32m+[m[32m            var adminPendingApproval = await _db.Requests.CountAsync(r => r.Status == "Pending Approval");[m
             var adminPartial = await _db.Requests.CountAsync(r => r.Status == "Partial Approved");[m
             vm.Total = await _db.Requests.CountAsync(r => r.Status != "Deleted");[m
             vm.Draft = await _db.Requests.CountAsync(r => r.Status == "Draft");[m
[31m-            vm.Open = adminOpen;[m
[32m+[m[32m            vm.Open = adminPendingApproval;[m
             vm.PartialApproved = adminPartial;[m
[31m-            vm.PendingApproval = adminOpen + adminPartial;[m
[32m+[m[32m            vm.PendingApproval = adminPendingApproval + adminPartial;[m
             vm.Approved = await _db.Requests.CountAsync(r => r.Status == "Approved");[m
             vm.Rejected = await _db.Requests.CountAsync(r => r.Status == "Rejected");[m
             vm.ClarificationNeeded = await _db.Requests.CountAsync(r => r.Status == "Clarification Needed");[m
             vm.TotalAmountApproved = await _db.Requests.Where(r => r.Status == "Approved" && r.Amount != null).SumAsync(r => r.Amount ?? 0);[m
[31m-            vm.TotalAmountPending = await _db.Requests.Where(r => (r.Status == "Open" || r.Status == "Partial Approved") && r.Amount != null).SumAsync(r => r.Amount ?? 0);[m
[32m+[m[32m            vm.TotalAmountPending = await _db.Requests.Where(r => (r.Status == "Pending Approval" || r.Status == "Partial Approved") && r.Amount != null).SumAsync(r => r.Amount ?? 0);[m
         }[m
 [m
         var activityQuery = _db.RequestActionLogs[m
[36m@@ -102,7 +102,7 @@[m [mpublic class DashboardService : IDashboardService[m
         var urgentQuery = _db.Requests[m
             .Include(r => r.Requestor).Include(r => r.Location)[m
             .Include(r => r.Department).Include(r => r.RequestType).Include(r => r.Documents)[m
[31m-            .Where(r => (r.Status == "Open" || r.Status == "Partial Approved") && (r.Priority == "Critical" || r.Priority == "High"));[m
[32m+[m[32m            .Where(r => (r.Status == "Pending Approval" || r.Status == "Partial Approved") && (r.Priority == "Critical" || r.Priority == "High"));[m
         if (!isAdmin)[m
             urgentQuery = urgentQuery.Where(r => r.RequestorId == userId);[m
         vm.UrgentPending = await urgentQuery[m
[36m@@ -150,7 +150,7 @@[m [mpublic class DashboardService : IDashboardService[m
                 {[m
                     LocationName = g.Key,[m
                     Total = g.Count(),[m
[31m-                    Pending = g.Count(r => r.Status == "Open" || r.Status == "Partial Approved"),[m
[32m+[m[32m                    Pending = g.Count(r => r.Status == "Pending Approval" || r.Status == "Partial Approved"),[m
                     Approved = g.Count(r => r.Status == "Approved"),[m
                     Rejected = g.Count(r => r.Status == "Rejected"),[m
                 }).ToListAsync();[m
[36m@@ -162,7 +162,7 @@[m [mpublic class DashboardService : IDashboardService[m
                 {[m
                     DeptName = g.Key,[m
                     Total = g.Count(),[m
[31m-                    Pending = g.Count(r => r.Status == "Open" || r.Status == "Partial Approved"),[m
[32m+[m[32m                    Pending = g.Count(r => r.Status == "Pending Approval" || r.Status == "Partial Approved"),[m
                     Approved = g.Count(r => r.Status == "Approved"),[m
                     Rejected = g.Count(r => r.Status == "Rejected"),[m
                 }).OrderByDescending(d => d.Total).ToListAsync();[m
[36m@@ -174,7 +174,7 @@[m [mpublic class DashboardService : IDashboardService[m
                 .ToDictionaryAsync(x => x.Key, x => x.Count);[m
 [m
             vm.ByPriority = await _db.Requests[m
[31m-                .Where(r => r.Status == "Open" || r.Status == "Partial Approved")[m
[32m+[m[32m                .Where(r => r.Status == "Pending Approval" || r.Status == "Partial Approved")[m
                 .GroupBy(r => r.Priority)[m
                 .Select(g => new { g.Key, Count = g.Count() })[m
                 .ToDictionaryAsync(x => x.Key, x => x.Count);[m
[36m@@ -215,7 +215,7 @@[m [mpublic class DashboardService : IDashboardService[m
         var vm = new MyRequestsTabVM[m
         {[m
             MyTotal         = await _db.Requests.CountAsync(r => r.RequestorId == userId && r.Status != "Deleted"),[m
[31m-            MyPending       = await _db.Requests.CountAsync(r => r.RequestorId == userId && (r.Status == "Open" || r.Status == "Partial Approved")),[m
[32m+[m[32m            MyPending       = await _db.Requests.CountAsync(r => r.RequestorId == userId && (r.Status == "Pending Approval" || r.Status == "Partial Approved")),[m
             MyApproved      = await _db.Requests.CountAsync(r => r.RequestorId == userId && r.Status == "Approved"),[m
             MyRejected      = await _db.Requests.CountAsync(r => r.RequestorId == userId && r.Status == "Rejected"),[m
             MyDraft         = await _db.Requests.CountAsync(r => r.RequestorId == userId && r.Status == "Draft"),[m
[36m@@ -229,7 +229,7 @@[m [mpublic class DashboardService : IDashboardService[m
             .Select(g => new DashboardSummaryDto[m
             {[m
                 CategoryName       = g.Key,[m
[31m-                PendingCount       = g.Count(r => r.Status == "Open" || r.Status == "Partial Approved"),[m
[32m+[m[32m                PendingCount       = g.Count(r => r.Status == "Pending Approval" || r.Status == "Partial Approved"),[m
                 ApprovedCount      = g.Count(r => r.Status == "Approved"),[m
                 RejectedCount      = g.Count(r => r.Status == "Rejected"),[m
                 ClarificationCount = g.Count(r => r.Status == "Clarification Needed"),[m
[36m@@ -242,7 +242,7 @@[m [mpublic class DashboardService : IDashboardService[m
             .Select(g => new DashboardSummaryDto[m
             {[m
                 CategoryName       = g.Key,[m
[31m-                PendingCount       = g.Count(r => r.Status == "Open" || r.Status == "Partial Approved"),[m
[32m+[m[32m                PendingCount       = g.Count(r => r.Status == "Pending Approval" || r.Status == "Partial Approved"),[m
                 ApprovedCount      = g.Count(r => r.Status == "Approved"),[m
                 RejectedCount      = g.Count(r => r.Status == "Rejected"),[m
                 ClarificationCount = g.Count(r => r.Status == "Clarification Needed"),[m
[36m@@ -257,7 +257,7 @@[m [mpublic class DashboardService : IDashboardService[m
             .Select(g => new DashboardSummaryDto[m
             {[m
                 CategoryName       = g.Key,[m
[31m-                PendingCount       = g.Count(r => r.Status == "Open" || r.Status == "Partial Approved"),[m
[32m+[m[32m                PendingCount       = g.Count(r => r.Status == "Pending Approval" || r.Status == "Partial Approved"),[m
                 ApprovedCount      = g.Count(r => r.Status == "Approved"),[m
                 RejectedCount      = g.Count(r => r.Status == "Rejected"),[m
                 ClarificationCount = g.Count(r => r.Status == "Clarification Needed"),[m
[36m@@ -269,7 +269,7 @@[m [mpublic class DashboardService : IDashboardService[m
             .Select(g => new DashboardSummaryDto[m
             {[m
                 CategoryName       = g.Key,[m
[31m-                PendingCount       = g.Count(r => r.Status == "Open" || r.Status == "Partial Approved"),[m
[32m+[m[32m                PendingCount       = g.Count(r => r.Status == "Pending Approval" || r.Status == "Partial Approved"),[m
                 ApprovedCount      = g.Count(r => r.Status == "Approved"),[m
                 RejectedCount      = g.Count(r => r.Status == "Rejected"),[m
                 ClarificationCount = g.Count(r => r.Status == "Clarification Needed"),[m
[36m@@ -416,7 +416,7 @@[m [mpublic class DashboardService : IDashboardService[m
         var vm = new MyTerritoryTabVM[m
         {[m
             TotalInTerritory = await q.CountAsync(),[m
[31m-            Pending          = await q.CountAsync(r => r.Status == "Open" || r.Status == "Partial Approved"),[m
[32m+[m[32m            Pending          = await q.CountAsync(r => r.Status == "Pending Approval" || r.Status == "Partial Approved"),[m
             Approved         = await q.CountAsync(r => r.Status == "Approved"),[m
             Rejected         = await q.CountAsync(r => r.Status == "Rejected"),[m
             Clarification    = await q.CountAsync(r => r.Status == "Clarification Needed"),[m
[36m@@ -432,7 +432,7 @@[m [mpublic class DashboardService : IDashboardService[m
             .Select(g => new DashboardSummaryDto[m
             {[m
                 CategoryName       = g.Key,[m
[31m-                PendingCount       = g.Count(r => r.Status == "Open" || r.Status == "Partial Approved"),[m
[32m+[m[32m                PendingCount       = g.Count(r => r.Status == "Pending Approval" || r.Status == "Partial Approved"),[m
                 ApprovedCount      = g.Count(r => r.Status == "Approved"),[m
                 RejectedCount      = g.Count(r => r.Status == "Rejected"),[m
                 ClarificationCount = g.Count(r => r.Status == "Clarification Needed"),[m
[36m@@ -445,7 +445,7 @@[m [mpublic class DashboardService : IDashboardService[m
             .Select(g => new DashboardSummaryDto[m
             {[m
                 CategoryName       = g.Key,[m
[31m-                PendingCount       = g.Count(r => r.Status == "Open" || r.Status == "Partial Approved"),[m
[32m+[m[32m                PendingCount       = g.Count(r => r.Status == "Pending Approval" || r.Status == "Partial Approved"),[m
                 ApprovedCount      = g.Count(r => r.Status == "Approved"),[m
                 RejectedCount      = g.Count(r => r.Status == "Rejected"),[m
                 ClarificationCount = g.Count(r => r.Status == "Clarification Needed"),[m
[36m@@ -460,7 +460,7 @@[m [mpublic class DashboardService : IDashboardService[m
             .Select(g => new DashboardSummaryDto[m
             {[m
                 CategoryName       = g.Key,[m
[31m-                PendingCount       = g.Count(r => r.Status == "Open" || r.Status == "Partial Approved"),[m
[32m+[m[32m                PendingCount       = g.Count(r => r.Status == "Pending Approval" || r.Status == "Partial Approved"),[m
                 ApprovedCount      = g.Count(r => r.Status == "Approved"),[m
                 RejectedCount      = g.Count(r => r.Status == "Rejected"),[m
                 ClarificationCount = g.Count(r => r.Status == "Clarification Needed"),[m
[36m@@ -472,7 +472,7 @@[m [mpublic class DashboardService : IDashboardService[m
             .Select(g => new DashboardSummaryDto[m
             {[m
                 CategoryName       = g.Key,[m
[31m-                PendingCount       = g.Count(r => r.Status == "Open" || r.Status == "Partial Approved"),[m
[32m+[m[32m                PendingCount       = g.Count(r => r.Status == "Pending Approval" || r.Status == "Partial Approved"),[m
                 ApprovedCount      = g.Count(r => r.Status == "Approved"),[m
                 RejectedCount      = g.Count(r => r.Status == "Rejected"),[m
                 ClarificationCount = g.Count(r => r.Status == "Clarification Needed"),[m
[36m@@ -490,7 +490,7 @@[m [mpublic class DashboardService : IDashboardService[m
             .Select(g => new DashboardSummaryDto[m
             {[m
                 CategoryName      = g.Key,[m
[31m-                PendingCount      = g.Count(r => r.Status == "Open" || r.Status == "Partial Approved"),[m
[32m+[m[32m                PendingCount      = g.Count(r => r.Status == "Pending Approval" || r.Status == "Partial Approved"),[m
                 ApprovedCount     = g.Count(r => r.Status == "Approved"),[m
                 RejectedCount     = g.Count(r => r.Status == "Rejected"),[m
                 ClarificationCount = g.Count(r => r.Status == "Clarification Needed"),[m
[36m@@ -508,7 +508,7 @@[m [mpublic class DashboardService : IDashboardService[m
             .Select(g => new DashboardSummaryDto[m
             {[m
                 CategoryName      = g.Key,[m
[31m-                PendingCount      = g.Count(r => r.Status == "Open" || r.Status == "Partial Approved"),[m
[32m+[m[32m                PendingCount      = g.Count(r => r.Status == "Pending Approval" || r.Status == "Partial Approved"),[m
                 ApprovedCount     = g.Count(r => r.Status == "Approved"),[m
                 RejectedCount     = g.Count(r => r.Status == "Rejected"),[m
                 ClarificationCount = g.Count(r => r.Status == "Clarification Needed"),[m
[36m@@ -526,7 +526,7 @@[m [mpublic class DashboardService : IDashboardService[m
             .Select(g => new DashboardSummaryDto[m
             {[m
                 CategoryName      = g.Key,[m
[31m-                PendingCount      = g.Count(r => r.Status == "Open" || r.Status == "Partial Approved"),[m
[32m+[m[32m                PendingCount      = g.Count(r => r.Status == "Pending Approval" || r.Status == "Partial Approved"),[m
                 ApprovedCount     = g.Count(r => r.Status == "Approved"),[m
                 RejectedCount     = g.Count(r => r.Status == "Rejected"),[m
                 ClarificationCount = g.Count(r => r.Status == "Clarification Needed"),[m
[36m@@ -542,7 +542,7 @@[m [mpublic class DashboardService : IDashboardService[m
             .Select(g => new DashboardSummaryDto[m
             {[m
                 CategoryName      = g.Key,[m
[31m-                PendingCount      = g.Count(r => r.Status == "Open" || r.Status == "Partial Approved"),[m
[32m+[m[32m                PendingCount      = g.Count(r => r.Status == "Pending Approval" || r.Status == "Partial Approved"),[m
                 ApprovedCount     = g.Count(r => r.Status == "Approved"),[m
                 RejectedCount     = g.Count(r => r.Status == "Rejected"),[m
                 ClarificationCount = g.Count(r => r.Status == "Clarification Needed"),[m
[1mdiff --git a/Services/RequestService.cs b/Services/RequestService.cs[m
[1mind