using ApproveFlow.MVC.Models.Domain;
using Microsoft.EntityFrameworkCore;

namespace ApproveFlow.MVC.Data;

public class AppDbContext : DbContext
{
    private readonly IConfiguration _cfg;

    public AppDbContext(DbContextOptions<AppDbContext> opt, IConfiguration cfg)
        : base(opt) { _cfg = cfg; }

    public DbSet<Role> Roles => Set<Role>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<RequestType> RequestTypes => Set<RequestType>();
    public DbSet<User> Users => Set<User>();
    public DbSet<UserLocation> UserLocations => Set<UserLocation>();
    public DbSet<UserDepartment> UserDepartments => Set<UserDepartment>();
    public DbSet<WorkflowDefinition> WorkflowDefinitions => Set<WorkflowDefinition>();
    public DbSet<WorkflowStep> WorkflowSteps => Set<WorkflowStep>();
    public DbSet<Request> Requests => Set<Request>();
    public DbSet<RequestApprover> RequestApprovers => Set<RequestApprover>();
    public DbSet<RequestActionLog> RequestActionLogs => Set<RequestActionLog>();
    public DbSet<RequestDocument> RequestDocuments => Set<RequestDocument>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<WhatsAppPendingClarification> WhatsAppPendingClarifications => Set<WhatsAppPendingClarification>();
    public DbSet<WhatsAppMessageHistory> WhatsAppMessageHistories => Set<WhatsAppMessageHistory>();
    public DbSet<WhatsAppOutbox> WhatsAppOutbox => Set<WhatsAppOutbox>();
    public DbSet<ApprovalAttachment> ApprovalAttachments => Set<ApprovalAttachment>();
    public DbSet<ApprovalReply> ApprovalReplies => Set<ApprovalReply>();
    public DbSet<UserRequestTypeMapping> UserRequestTypeMappings => Set<UserRequestTypeMapping>();
    public DbSet<WorkflowNotificationRole> WorkflowNotificationRoles => Set<WorkflowNotificationRole>();
    public DbSet<RequestViewToken> RequestViewTokens => Set<RequestViewToken>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<Role>(e => { e.ToTable("Roles"); e.HasIndex(x => x.RoleName).IsUnique(); });
        mb.Entity<RolePermission>(e => {
            e.ToTable("RolePermissions");
            e.HasIndex(x => new { x.RoleId, x.ScreenName }).IsUnique();
            e.HasOne(x => x.Role).WithMany(r => r.Permissions).HasForeignKey(x => x.RoleId);
        });
        mb.Entity<Location>(e => { e.ToTable("Locations"); e.HasIndex(x => x.Code).IsUnique(); });
        mb.Entity<Department>(e => { e.ToTable("Departments"); e.HasIndex(x => x.Code).IsUnique(); });
        mb.Entity<RequestType>(e => { e.ToTable("RequestTypes"); e.HasIndex(x => x.Code).IsUnique(); });
        mb.Entity<User>(e => {
            e.ToTable("Users");
            e.Property(x => x.Username).HasMaxLength(100).IsRequired();
            e.HasIndex(x => x.Username).IsUnique();
            // Email is now optional (login uses Username). Filtered so multiple
            // users may have a blank email while real emails stay unique.
            e.HasIndex(x => x.Email).IsUnique().HasFilter("[Email] <> ''");
            e.HasOne(x => x.Role).WithMany(r => r.Users).HasForeignKey(x => x.RoleId);
        });
        mb.Entity<UserLocation>(e => {
            e.ToTable("UserLocations");
            e.HasIndex(x => new { x.UserId, x.LocationId }).IsUnique();
            e.HasOne(x => x.User).WithMany(u => u.UserLocations).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Location).WithMany(l => l.UserLocations).HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
        });
        mb.Entity<UserDepartment>(e => {
            e.ToTable("UserDepartments");
            e.HasIndex(x => new { x.UserId, x.DepartmentId }).IsUnique();
            e.HasOne(x => x.User).WithMany(u => u.UserDepartments).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Department).WithMany(d => d.UserDepartments).HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.Restrict);
        });
        mb.Entity<WorkflowDefinition>(e => {
            e.ToTable("WorkflowDefinitions");
            e.HasOne(x => x.Location).WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.RequestType).WithMany().HasForeignKey(x => x.RequestTypeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Department).WithMany().HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.Restrict);
        });
        mb.Entity<WorkflowStep>(e => {
            e.ToTable("WorkflowSteps");
            e.HasIndex(x => new { x.WorkflowId, x.StepLevel }).IsUnique();
            e.HasOne(x => x.Workflow).WithMany(w => w.Steps).HasForeignKey(x => x.WorkflowId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.ApproverRole).WithMany().HasForeignKey(x => x.ApproverRoleId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.ApproverUser).WithMany().HasForeignKey(x => x.ApproverUserId).OnDelete(DeleteBehavior.Restrict);
        });
        mb.Entity<Request>(e => {
            e.ToTable("Requests", t => t.HasTrigger("trg_RequestNumber")); // prevents EF OUTPUT clause conflict
            e.HasOne(x => x.RequestType).WithMany().HasForeignKey(x => x.RequestTypeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Location).WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Department).WithMany().HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Requestor).WithMany(u => u.SubmittedRequests).HasForeignKey(x => x.RequestorId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Workflow).WithMany().HasForeignKey(x => x.WorkflowId).OnDelete(DeleteBehavior.SetNull);
        });
        mb.Entity<RequestApprover>(e => {
            e.ToTable("RequestApprovers");
            e.HasIndex(x => new { x.RequestId, x.StepLevel }).IsUnique();
            e.HasOne(x => x.Request).WithMany(r => r.Approvers).HasForeignKey(x => x.RequestId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.ApproverUser).WithMany(u => u.ApprovalAssignments).HasForeignKey(x => x.ApproverUserId).OnDelete(DeleteBehavior.Restrict);
        });
        mb.Entity<RequestActionLog>(e => {
            e.ToTable("RequestActionLog");
            e.HasOne(x => x.Request).WithMany(r => r.ActionLogs).HasForeignKey(x => x.RequestId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.ActorUser).WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        });
        mb.Entity<RequestDocument>(e => {
            e.ToTable("RequestDocuments");
            e.HasOne(x => x.Request).WithMany(r => r.Documents).HasForeignKey(x => x.RequestId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.UploadedByUser).WithMany().HasForeignKey(x => x.UploadedByUserId).OnDelete(DeleteBehavior.Restrict);
        });
        mb.Entity<Notification>(e => {
            e.ToTable("Notifications");
            e.HasIndex(x => new { x.UserId, x.IsRead });
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Request).WithMany().HasForeignKey(x => x.RequestId).OnDelete(DeleteBehavior.NoAction);
        });
        mb.Entity<ApprovalAttachment>(e => {
            e.ToTable("ApprovalAttachments");
            e.HasOne(x => x.Request).WithMany().HasForeignKey(x => x.RequestId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.UploadedByUser).WithMany().HasForeignKey(x => x.UploadedByUserId).OnDelete(DeleteBehavior.Restrict);
        });
        mb.Entity<ApprovalReply>(e => {
            e.ToTable("ApprovalReplies");
            e.HasOne(x => x.Request).WithMany().HasForeignKey(x => x.RequestId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.RequestId);
            e.HasIndex(x => x.ApprovalId);
        });
        mb.Entity<UserRequestTypeMapping>(e => {
            e.ToTable("UserRequestTypeMappings");
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.RequestType).WithMany().HasForeignKey(x => x.RequestTypeId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.UserId);
            e.HasIndex(x => x.RequestTypeId);
        });
        mb.Entity<WorkflowNotificationRole>(e => {
            e.ToTable("WorkflowNotificationRoles");
            e.HasIndex(x => new { x.WorkflowId, x.StepLevel, x.RoleId }).IsUnique();
            e.HasOne(x => x.Workflow).WithMany(w => w.NotificationRoles).HasForeignKey(x => x.WorkflowId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Role).WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Restrict);
        });
        mb.Entity<RequestViewToken>(e => {
            e.ToTable("RequestViewTokens");
            e.HasIndex(x => x.RequestId).IsUnique();
            e.HasIndex(x => x.Token).IsUnique();
            e.HasOne(x => x.Request).WithMany().HasForeignKey(x => x.RequestId).OnDelete(DeleteBehavior.Cascade);
        });
        mb.Entity<WhatsAppMessageHistory>(e => {
            e.ToTable("WhatsAppMessageHistory");
            e.HasIndex(x => x.RequestId);
            e.Property(x => x.MobileNumber).HasMaxLength(30);
            e.Property(x => x.Direction).HasMaxLength(10);
            e.Property(x => x.MessageType).HasMaxLength(60);
            e.Property(x => x.MessageId).HasMaxLength(200);
            e.Property(x => x.Status).HasMaxLength(20);
        });
        mb.Entity<WhatsAppOutbox>(e => {
            e.ToTable("WhatsAppOutbox");
            // The poller seeks due rows by (Status, NextAttemptAt) each tick.
            e.HasIndex(x => new { x.Status, x.NextAttemptAt });
            e.Property(x => x.PhoneNumberId).HasMaxLength(40);
            e.Property(x => x.ApiVersion).HasMaxLength(10);
            e.Property(x => x.ToNumber).HasMaxLength(30);
            e.Property(x => x.MessageType).HasMaxLength(60);
            e.Property(x => x.Status).HasMaxLength(20);
            e.Property(x => x.LastError).HasMaxLength(400);
        });
        mb.Entity<WhatsAppPendingClarification>(e => {
            e.ToTable("WhatsAppPendingClarifications");
            // Deferred-approve sessions are polled by the timeout sweep every few seconds
            // and resolved by message id on every inbound reply. Completed sessions are
            // retained as audit records, so both paths need to stay index seeks as the
            // table grows.
            e.HasIndex(x => new { x.Action, x.Status, x.ExpiresOn });
            e.HasIndex(x => x.WhatsAppMessageId);
            e.HasIndex(x => x.ParentMessageId);
        });
    }

    public override async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        StampTimestamps();
        return await base.SaveChangesAsync(ct);
    }
    public override int SaveChanges() { StampTimestamps(); return base.SaveChanges(); }

    private void StampTimestamps()
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById(
            _cfg["AppSettings:TimeZone"] ?? "India Standard Time");
        var now = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, tz);
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State == EntityState.Added)
            {
                TrySet(entry, "CreatedAt", now);
                TrySet(entry, "UpdatedAt", now);
                TrySet(entry, "UploadedAt", now);
            }
            else if (entry.State == EntityState.Modified)
            {
                TrySet(entry, "UpdatedAt", now);
            }
        }
    }

    private static void TrySet(
        Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry e, string prop, object val)
    {
        if (e.Properties.Any(p => p.Metadata.Name == prop))
            e.Property(prop).CurrentValue = val;
    }
}





//using ApproveFlow.MVC.Models.Domain;
//using Microsoft.EntityFrameworkCore;

//namespace ApproveFlow.MVC.Data;

//public class AppDbContext : DbContext
//{
//    private readonly IConfiguration _cfg;

//    public AppDbContext(DbContextOptions<AppDbContext> opt, IConfiguration cfg)
//        : base(opt) { _cfg = cfg; }

//    public DbSet<Role> Roles => Set<Role>();
//    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
//    public DbSet<Location> Locations => Set<Location>();
//    public DbSet<Department> Departments => Set<Department>();
//    public DbSet<RequestType> RequestTypes => Set<RequestType>();
//    public DbSet<User> Users => Set<User>();
//    public DbSet<UserLocation> UserLocations => Set<UserLocation>();
//    public DbSet<UserDepartment> UserDepartments => Set<UserDepartment>();
//    public DbSet<WorkflowDefinition> WorkflowDefinitions => Set<WorkflowDefinition>();
//    public DbSet<WorkflowStep> WorkflowSteps => Set<WorkflowStep>();
//    public DbSet<Request> Requests => Set<Request>();
//    public DbSet<RequestApprover> RequestApprovers => Set<RequestApprover>();
//    public DbSet<RequestActionLog> RequestActionLogs => Set<RequestActionLog>();
//    public DbSet<RequestDocument> RequestDocuments => Set<RequestDocument>();
//    public DbSet<Notification> Notifications => Set<Notification>();

//    protected override void OnModelCreating(ModelBuilder mb)
//    {
//        mb.Entity<Role>(e => { e.ToTable("Roles"); e.HasIndex(x => x.RoleName).IsUnique(); });
//        mb.Entity<RolePermission>(e => {
//            e.ToTable("RolePermissions");
//            e.HasIndex(x => new { x.RoleId, x.ScreenName }).IsUnique();
//            e.HasOne(x => x.Role).WithMany(r => r.Permissions).HasForeignKey(x => x.RoleId);
//        });
//        mb.Entity<Location>(e => { e.ToTable("Locations"); e.HasIndex(x => x.Code).IsUnique(); });
//        mb.Entity<Department>(e => { e.ToTable("Departments"); e.HasIndex(x => x.Code).IsUnique(); });
//        mb.Entity<RequestType>(e => { e.ToTable("RequestTypes"); e.HasIndex(x => x.Code).IsUnique(); });
//        mb.Entity<User>(e => {
//            e.ToTable("Users");
//            e.HasIndex(x => x.Email).IsUnique();
//            e.HasOne(x => x.Role).WithMany(r => r.Users).HasForeignKey(x => x.RoleId);
//        });
//        mb.Entity<UserLocation>(e => {
//            e.ToTable("UserLocations");
//            e.HasIndex(x => new { x.UserId, x.LocationId }).IsUnique();
//            e.HasOne(x => x.User).WithMany(u => u.UserLocations).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
//            e.HasOne(x => x.Location).WithMany(l => l.UserLocations).HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
//        });
//        mb.Entity<UserDepartment>(e => {
//            e.ToTable("UserDepartments");
//            e.HasIndex(x => new { x.UserId, x.DepartmentId }).IsUnique();
//            e.HasOne(x => x.User).WithMany(u => u.UserDepartments).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
//            e.HasOne(x => x.Department).WithMany(d => d.UserDepartments).HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.Restrict);
//        });
//        mb.Entity<WorkflowDefinition>(e => {
//            e.ToTable("WorkflowDefinitions");
//            e.HasOne(x => x.Location).WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
//            e.HasOne(x => x.RequestType).WithMany().HasForeignKey(x => x.RequestTypeId).OnDelete(DeleteBehavior.Restrict);
//            e.HasOne(x => x.Department).WithMany().HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.Restrict);
//        });
//        mb.Entity<WorkflowStep>(e => {
//            e.ToTable("WorkflowSteps");
//            e.HasIndex(x => new { x.WorkflowId, x.StepLevel }).IsUnique();
//            e.HasOne(x => x.Workflow).WithMany(w => w.Steps).HasForeignKey(x => x.WorkflowId).OnDelete(DeleteBehavior.Cascade);
//            e.HasOne(x => x.ApproverUser).WithMany().HasForeignKey(x => x.ApproverUserId).OnDelete(DeleteBehavior.Restrict);
//        });
//        mb.Entity<Request>(e => {
//            e.ToTable("Requests");
//            e.HasOne(x => x.RequestType).WithMany().HasForeignKey(x => x.RequestTypeId).OnDelete(DeleteBehavior.Restrict);
//            e.HasOne(x => x.Location).WithMany().HasForeignKey(x => x.LocationId).OnDelete(DeleteBehavior.Restrict);
//            e.HasOne(x => x.Department).WithMany().HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.Restrict);
//            e.HasOne(x => x.Requestor).WithMany(u => u.SubmittedRequests).HasForeignKey(x => x.RequestorId).OnDelete(DeleteBehavior.Restrict);
//            e.HasOne(x => x.Workflow).WithMany().HasForeignKey(x => x.WorkflowId).OnDelete(DeleteBehavior.SetNull);
//        });
//        mb.Entity<RequestApprover>(e => {
//            e.ToTable("RequestApprovers");
//            e.HasIndex(x => new { x.RequestId, x.StepLevel }).IsUnique();
//            e.HasOne(x => x.Request).WithMany(r => r.Approvers).HasForeignKey(x => x.RequestId).OnDelete(DeleteBehavior.Cascade);
//            e.HasOne(x => x.ApproverUser).WithMany(u => u.ApprovalAssignments).HasForeignKey(x => x.ApproverUserId).OnDelete(DeleteBehavior.Restrict);
//        });
//        mb.Entity<RequestActionLog>(e => {
//            e.ToTable("RequestActionLog");
//            e.HasOne(x => x.Request).WithMany(r => r.ActionLogs).HasForeignKey(x => x.RequestId).OnDelete(DeleteBehavior.Cascade);
//            e.HasOne(x => x.ActorUser).WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
//        });
//        mb.Entity<RequestDocument>(e => {
//            e.ToTable("RequestDocuments");
//            e.HasOne(x => x.Request).WithMany(r => r.Documents).HasForeignKey(x => x.RequestId).OnDelete(DeleteBehavior.Cascade);
//            e.HasOne(x => x.UploadedByUser).WithMany().HasForeignKey(x => x.UploadedByUserId).OnDelete(DeleteBehavior.Restrict);
//        });
//        mb.Entity<Notification>(e => {
//            e.ToTable("Notifications");
//            e.HasIndex(x => new { x.UserId, x.IsRead });
//            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
//            e.HasOne(x => x.Request).WithMany().HasForeignKey(x => x.RequestId).OnDelete(DeleteBehavior.NoAction);
//        });
//    }

//    public override async Task<int> SaveChangesAsync(CancellationToken ct = default)
//    {
//        StampTimestamps();
//        return await base.SaveChangesAsync(ct);
//    }
//    public override int SaveChanges() { StampTimestamps(); return base.SaveChanges(); }

//    private void StampTimestamps()
//    {
//        var tz  = TimeZoneInfo.FindSystemTimeZoneById(
//            _cfg["AppSettings:TimeZone"] ?? "India Standard Time");
//        var now = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, tz);
//        foreach (var entry in ChangeTracker.Entries())
//        {
//            if (entry.State == EntityState.Added)
//            {
//                TrySet(entry, "CreatedAt", now);
//                TrySet(entry, "UpdatedAt", now);
//                TrySet(entry, "UploadedAt", now);
//            }
//            else if (entry.State == EntityState.Modified)
//            {
//                TrySet(entry, "UpdatedAt", now);
//            }
//        }
//    }

//    private static void TrySet(
//        Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry e, string prop, object val)
//    {
//        if (e.Properties.Any(p => p.Metadata.Name == prop))
//            e.Property(prop).CurrentValue = val;
//    }
//}
