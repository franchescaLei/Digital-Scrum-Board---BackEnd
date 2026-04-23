using DigitalScrumBoard1.Models;
using Microsoft.EntityFrameworkCore;

namespace DigitalScrumBoard1.Data;

public partial class DigitalScrumBoardContext : DbContext
{
    public DigitalScrumBoardContext(DbContextOptions<DigitalScrumBoardContext> options)
        : base(options)
    {
    }

    // -------------------------
    // DbSets (THIS FIXES your seeder errors: context.Users / context.Roles / context.Teams)
    // -------------------------
    public DbSet<User> Users { get; set; } = null!;
    public DbSet<Role> Roles { get; set; } = null!;
    public DbSet<Team> Teams { get; set; } = null!;
    public DbSet<AuditLog> AuditLogs { get; set; } = null!;

    public DbSet<Notification> Notifications { get; set; } = null!;
    public DbSet<Sprint> Sprints { get; set; } = null!;

    public DbSet<WorkItem> WorkItems { get; set; } = null!;
    public DbSet<WorkItemComment> WorkItemComments { get; set; } = null!;
    public DbSet<WorkItemHistory> WorkItemHistories { get; set; } = null!;

    public DbSet<WorkItemType> WorkItemTypes { get; set; } = null!;
    public DbSet<WorkItemTypeHierarchyRule> WorkItemTypeHierarchyRules { get; set; } = null!;
    public DbSet<EmailVerificationToken> EmailVerificationTokens { get; set; } = null!;

    public DbSet<PasswordResetToken> PasswordResetTokens { get; set; } = null!;
    // -------------------------
    // Hierarchy Rules Enforcement (application-level)
    // -------------------------
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnforceWorkItemHierarchyRules();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        return SaveChangesAsyncWithHierarchyRules(acceptAllChangesOnSuccess, cancellationToken);
    }

    private async Task<int> SaveChangesAsyncWithHierarchyRules(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken)
    {
        await EnforceWorkItemHierarchyRulesAsync(cancellationToken);
        return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void EnforceWorkItemHierarchyRules()
    {
        EnforceWorkItemHierarchyRulesAsync(CancellationToken.None)
            .GetAwaiter()
            .GetResult();
    }

    private async Task EnforceWorkItemHierarchyRulesAsync(CancellationToken ct)
    {
        var candidates = ChangeTracker.Entries<WorkItem>()
            .Where(e => e.State == EntityState.Added || e.State == EntityState.Modified)
            .Where(e =>
                e.Property(x => x.ParentWorkItemID).IsModified ||
                e.Property(x => x.WorkItemTypeID).IsModified ||
                e.State == EntityState.Added)
            .Select(e => e.Entity)
            .ToList();

        if (candidates.Count == 0) return;

        // ✅ TypeName lookup for special rules (Epic no parent, Task no children)
        var typeNameById = await WorkItemTypes
            .AsNoTracking()
            .ToDictionaryAsync(t => t.WorkItemTypeID, t => t.TypeName, ct);

        var allowedPairs = await WorkItemTypeHierarchyRules
            .AsNoTracking()
            .Where(r => r.IsAllowed)
            .Select(r => new { r.ParentTypeID, r.ChildTypeID })
            .ToListAsync(ct);

        var allowed = new HashSet<(int ParentTypeID, int ChildTypeID)>(
            allowedPairs.Select(x => (x.ParentTypeID, x.ChildTypeID))
        );

        var trackedWorkItems = ChangeTracker.Entries<WorkItem>()
            .Where(e => e.State != EntityState.Deleted)
            .Select(e => e.Entity)
            .ToDictionary(w => w.WorkItemID, w => w);

        var parentIdsToFetch = new HashSet<int>();

        foreach (var wi in candidates)
        {
            if (wi.ParentWorkItemID.HasValue && wi.ParentWorkItem == null)
            {
                var pid = wi.ParentWorkItemID.Value;
                if (!trackedWorkItems.ContainsKey(pid))
                    parentIdsToFetch.Add(pid);
            }
        }

        var dbParents = parentIdsToFetch.Count == 0
            ? new Dictionary<int, WorkItem>()
            : await WorkItems
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(p => parentIdsToFetch.Contains(p.WorkItemID))
                .Select(p => new WorkItem
                {
                    WorkItemID = p.WorkItemID,
                    WorkItemTypeID = p.WorkItemTypeID,
                    IsDeleted = p.IsDeleted
                })
                .ToDictionaryAsync(p => p.WorkItemID, p => p, ct);

        foreach (var wi in candidates)
        {
            typeNameById.TryGetValue(wi.WorkItemTypeID, out var wiTypeName);

            // ✅ RULE: Epic shall not have a parent
            if (string.Equals(wiTypeName, "Epic", StringComparison.OrdinalIgnoreCase) &&
                wi.ParentWorkItemID.HasValue)
            {
                throw new DbUpdateException("Hierarchy rule violated: Epic shall not have a parent.");
            }

            // ✅ RULE: Task shall not contain child items
            // (blocks changing an item into Task if it already has children)
            if (string.Equals(wiTypeName, "Task", StringComparison.OrdinalIgnoreCase) &&
                wi.WorkItemID != 0)
            {
                var hasChildren = await WorkItems
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .AnyAsync(x => x.ParentWorkItemID == wi.WorkItemID && !x.IsDeleted, ct);

                if (hasChildren)
                    throw new DbUpdateException("Hierarchy rule violated: Task shall not contain child items.");
            }

            if (!wi.ParentWorkItemID.HasValue && wi.ParentWorkItem == null)
                continue;

            WorkItem? parent = wi.ParentWorkItem;

            if (parent == null && wi.ParentWorkItemID.HasValue)
            {
                var pid = wi.ParentWorkItemID.Value;

                if (trackedWorkItems.TryGetValue(pid, out var trackedParent))
                    parent = trackedParent;
                else if (dbParents.TryGetValue(pid, out var dbParent))
                    parent = dbParent;
            }

            if (parent == null)
                throw new DbUpdateException(
                    $"Hierarchy rule check failed: Parent WorkItem not found for WorkItemID={wi.WorkItemID}."
                );

            if (wi.WorkItemID != 0 && parent.WorkItemID == wi.WorkItemID)
                throw new DbUpdateException("Hierarchy rule check failed: A work item cannot be its own parent.");

            if (parent.IsDeleted)
                throw new DbUpdateException("Hierarchy rule check failed: Cannot assign a parent that is deleted.");

            var parentTypeId = parent.WorkItemTypeID;
            var childTypeId = wi.WorkItemTypeID;

            if (!allowed.Contains((parentTypeId, childTypeId)))
            {
                throw new DbUpdateException(
                    $"Hierarchy rule violated: ParentTypeID={parentTypeId} cannot contain ChildTypeID={childTypeId}."
                );
            }
        }
    }

    // -------------------------
    // Fluent constraints
    // -------------------------
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // User
        modelBuilder.Entity<User>(e =>
        {
            e.HasKey(x => x.UserID);

            e.Property(x => x.FirstName).HasMaxLength(50).IsRequired();
            e.Property(x => x.MiddleName).HasMaxLength(50);
            e.Property(x => x.NameExtension).HasMaxLength(10);
            e.Property(x => x.LastName).HasMaxLength(50).IsRequired();

            e.Property(x => x.EmailAddress).HasMaxLength(100).IsRequired();
            e.HasIndex(x => x.EmailAddress).IsUnique();

            e.Property(x => x.PasswordHash).HasMaxLength(255).IsRequired();

            e.Property(x => x.CreatedAt).HasDefaultValueSql("DATEADD(hour, 8, GETUTCDATE())").IsRequired();
            e.Property(x => x.UpdatedAt).IsRequired();

            e.Property(x => x.Disabled).HasDefaultValue(false).IsRequired();

            e.HasOne(x => x.Role)
                .WithMany()
                .HasForeignKey(x => x.RoleID)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.Team)
                .WithMany()
                .HasForeignKey(x => x.TeamID)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired(false);
        });

        // Role
        modelBuilder.Entity<Role>(e =>
        {
            e.HasKey(x => x.RoleID);
            e.Property(x => x.RoleName).HasMaxLength(50).IsRequired();
            e.HasIndex(x => x.RoleName).IsUnique();
            e.Property(x => x.Description).HasMaxLength(255);
        });

        // Team
        modelBuilder.Entity<Team>(e =>
        {
            e.HasKey(x => x.TeamID);
            e.Property(x => x.TeamName).HasMaxLength(50).IsRequired();
            e.HasIndex(x => x.TeamName).IsUnique();
            e.Property(x => x.Description).HasMaxLength(255);
            e.Property(x => x.IsActive).HasDefaultValue(true);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("DATEADD(hour, 8, GETUTCDATE())");
        });

        // AuditLog
        modelBuilder.Entity<AuditLog>(e =>
        {
            e.HasKey(x => x.LogID);
            e.Property(x => x.Action).HasMaxLength(100).IsRequired();
            e.Property(x => x.IPAddress).HasMaxLength(45).IsRequired();
            e.Property(x => x.Timestamp).HasDefaultValueSql("DATEADD(hour, 8, GETUTCDATE())").IsRequired();
            e.Property(x => x.Details).HasMaxLength(500);

            e.Property(x => x.TargetType).HasMaxLength(50);

            // Indexes for common query patterns
            e.HasIndex(x => x.Timestamp);
            e.HasIndex(x => x.UserID);
            e.HasIndex(x => new { x.UserID, x.Timestamp });
            e.HasIndex(x => x.TargetType);

            e.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserID)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // WorkItemType
        modelBuilder.Entity<WorkItemType>(e =>
        {
            e.HasKey(x => x.WorkItemTypeID);
            e.Property(x => x.TypeName).HasMaxLength(20).IsRequired();
            e.HasIndex(x => x.TypeName).IsUnique();
            e.Property(x => x.Description).HasMaxLength(255);
        });

        // WorkItemTypeHierarchyRule (matches YOUR model exactly)
        modelBuilder.Entity<WorkItemTypeHierarchyRule>(e =>
        {
            e.HasKey(x => x.RuleID);

            e.Property(x => x.IsAllowed)
                .HasDefaultValue(true)
                .IsRequired();

            e.HasIndex(x => new { x.ParentTypeID, x.ChildTypeID })
                .IsUnique();

            e.HasOne(x => x.ParentType)
                .WithMany()
                .HasForeignKey(x => x.ParentTypeID)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.ChildType)
                .WithMany()
                .HasForeignKey(x => x.ChildTypeID)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // WorkItem (soft delete filter)
        modelBuilder.Entity<WorkItem>(e =>
        {
            e.HasKey(x => x.WorkItemID);

            e.Property(x => x.Title).HasMaxLength(100).IsRequired();
            e.Property(x => x.Status).HasMaxLength(20).IsRequired().HasDefaultValue("To-do");
            e.Property(x => x.Priority).HasMaxLength(20);

            // ✅ Enforce allowed Status/Priority values in DB
            e.ToTable(tb => tb.HasCheckConstraint(
                "CK_WorkItems_Status",
                "[Status] IN ('To-do','Ongoing','For Checking','Completed')"
            ));

            e.ToTable(tb => tb.HasCheckConstraint(
                "CK_WorkItems_Priority",
                "[Priority] IS NULL OR [Priority] IN ('Low','Medium','High','Critical')"
            ));

            e.Property(x => x.CreatedAt).HasDefaultValueSql("DATEADD(hour, 8, GETUTCDATE())").IsRequired();
            e.Property(x => x.UpdatedAt).IsRequired();

            e.Property(x => x.IsDeleted).HasDefaultValue(false).IsRequired();

            // Indexes for common query patterns
            e.HasIndex(x => x.SprintID);
            e.HasIndex(x => x.AssignedUserID);
            e.HasIndex(x => x.Status);
            e.HasIndex(x => new { x.SprintID, x.Status });
            e.HasIndex(x => new { x.SprintID, x.BoardOrder });
            e.HasIndex(x => x.ParentWorkItemID);
            e.HasIndex(x => x.WorkItemTypeID);
            e.HasIndex(x => x.TeamID);
            e.HasIndex(x => x.CreatedAt);

            e.HasOne(x => x.ParentWorkItem)
                .WithMany()
                .HasForeignKey(x => x.ParentWorkItemID)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.WorkItemType)
                .WithMany()
                .HasForeignKey(x => x.WorkItemTypeID)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.Team)
                .WithMany()
                .HasForeignKey(x => x.TeamID)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired(false);

            e.HasOne(x => x.Sprint)
                .WithMany()
                .HasForeignKey(x => x.SprintID)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.AssignedUser)
                .WithMany()
                .HasForeignKey(x => x.AssignedUserID)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.CreatedByUser)
                .WithMany()
                .HasForeignKey(x => x.CreatedByUserID)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasQueryFilter(x => !x.IsDeleted);
        });

        // WorkItemComment (soft delete filter)
        modelBuilder.Entity<WorkItemComment>(e =>
        {
            e.HasKey(x => x.CommentID);

            e.Property(x => x.CreatedAt).HasDefaultValueSql("DATEADD(hour, 8, GETUTCDATE())").IsRequired();
            e.Property(x => x.IsDeleted).HasDefaultValue(false).IsRequired();

            // Indexes for common query patterns
            e.HasIndex(x => x.WorkItemID);
            e.HasIndex(x => new { x.WorkItemID, x.CreatedAt });

            e.HasOne(x => x.WorkItem)
                .WithMany()
                .HasForeignKey(x => x.WorkItemID)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.CommentedByUser)
                .WithMany()
                .HasForeignKey(x => x.CommentedBy)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasQueryFilter(x => !x.IsDeleted);
        });

        // WorkItemHistory
        modelBuilder.Entity<WorkItemHistory>(e =>
        {
            e.HasKey(x => x.HistoryID);

            e.Property(x => x.FieldChanged).HasMaxLength(50).IsRequired();
            e.Property(x => x.ChangedAt).HasDefaultValueSql("DATEADD(hour, 8, GETUTCDATE())").IsRequired();

            // Indexes for common query patterns
            e.HasIndex(x => x.WorkItemID);
            e.HasIndex(x => x.ChangedAt);
            e.HasIndex(x => new { x.WorkItemID, x.ChangedAt });

            e.HasOne(x => x.WorkItem)
                .WithMany()
                .HasForeignKey(x => x.WorkItemID)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired(false); // ✅ fixes the global filter warning

            e.HasOne(x => x.ChangedByUser)
                .WithMany()
                .HasForeignKey(x => x.ChangedBy)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Sprint
        modelBuilder.Entity<Sprint>(e =>
        {
            e.HasKey(x => x.SprintID);

            e.Property(x => x.SprintName).HasMaxLength(100).IsRequired();
            e.Property(x => x.Goal).HasMaxLength(255);

            e.Property(x => x.Status).HasMaxLength(20).IsRequired();

            e.ToTable(tb => tb.HasCheckConstraint(
                "CK_Sprints_Status",
                "[Status] IN ('Planned','Active','Completed')"
            ));

            e.Property(x => x.CreatedAt).HasDefaultValueSql("DATEADD(hour, 8, GETUTCDATE())");
            e.Property(x => x.UpdatedAt).IsRequired();

            // Indexes for common query patterns
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.ManagedBy);
            e.HasIndex(x => x.TeamID);

            e.HasOne(x => x.Manager)
                .WithMany()
                .HasForeignKey(x => x.ManagedBy)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.Team)
                .WithMany()
                .HasForeignKey(x => x.TeamID)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Notification
        modelBuilder.Entity<Notification>(e =>
        {
            e.HasKey(x => x.NotificationID);

            e.Property(x => x.NotificationType).HasMaxLength(50).IsRequired();
            e.Property(x => x.IsRead).HasDefaultValue(false).IsRequired();
            e.Property(x => x.CreatedAt).HasDefaultValueSql("DATEADD(hour, 8, GETUTCDATE())").IsRequired();

            // Indexes for common query patterns
            e.HasIndex(x => new { x.UserID, x.IsRead });
            e.HasIndex(x => x.CreatedAt);
            e.HasIndex(x => x.RelatedWorkItemID);
            e.HasIndex(x => x.RelatedSprintID);

            e.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserID)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.RelatedWorkItem)
                .WithMany()
                .HasForeignKey(x => x.RelatedWorkItemID)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.RelatedSprint)
                .WithMany()
                .HasForeignKey(x => x.RelatedSprintID)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}