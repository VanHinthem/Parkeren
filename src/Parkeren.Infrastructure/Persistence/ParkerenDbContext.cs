using Microsoft.EntityFrameworkCore;
using Parkeren.Domain.Users;
using Parkeren.Domain.Vehicles;
using Parkeren.Domain.Policies;
using Parkeren.Domain.Rules;
using Parkeren.Domain.Visits;
using Parkeren.Domain.Notifications;
using Parkeren.Domain.ParkingProvider;
using Parkeren.Domain.Administration;

namespace Parkeren.Infrastructure.Persistence;

public sealed class ParkerenDbContext(DbContextOptions<ParkerenDbContext> options)
    : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<UserSession> UserSessions => Set<UserSession>();
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<UserVehicle> UserVehicles => Set<UserVehicle>();
    public DbSet<DefaultParkingPolicy> DefaultParkingPolicies => Set<DefaultParkingPolicy>();
    public DbSet<ParkingSystemSettings> ParkingSystemSettings => Set<ParkingSystemSettings>();
    public DbSet<UserPolicyOverride> UserPolicyOverrides => Set<UserPolicyOverride>();
    public DbSet<ParkingRuleSet> ParkingRuleSets => Set<ParkingRuleSet>();
    public DbSet<ParkingBudgetPeriod> ParkingBudgetPeriods => Set<ParkingBudgetPeriod>();
    public DbSet<ParkingTariff> ParkingTariffs => Set<ParkingTariff>();
    public DbSet<PaidWindow> PaidWindows => Set<PaidWindow>();
    public DbSet<ParkingCalendarException> ParkingCalendarExceptions => Set<ParkingCalendarException>();
    public DbSet<Visit> Visits => Set<Visit>();
    public DbSet<ProviderParkingAction> ProviderParkingActions => Set<ProviderParkingAction>();
    public DbSet<ProviderOperation> ProviderOperations => Set<ProviderOperation>();
    public DbSet<NotificationEvent> NotificationEvents => Set<NotificationEvent>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<PushSubscription> PushSubscriptions => Set<PushSubscription>();
    public DbSet<PushDelivery> PushDeliveries => Set<PushDelivery>();
    public DbSet<VisitEndTimeChange> VisitEndTimeChanges => Set<VisitEndTimeChange>();
    public DbSet<VisitSchedulerWork> VisitSchedulerWork => Set<VisitSchedulerWork>();
    public DbSet<ParkingBudgetWarningState> ParkingBudgetWarningStates => Set<ParkingBudgetWarningState>();
    public DbSet<ParkingProviderProduct> ParkingProviderProducts => Set<ParkingProviderProduct>();
    public DbSet<ProviderDiscrepancy> ProviderDiscrepancies => Set<ProviderDiscrepancy>();
    public DbSet<AdminAuditEvent> AdminAuditEvents => Set<AdminAuditEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("users");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Username).HasMaxLength(100).IsRequired();
            entity.Property(x => x.NormalizedUsername).HasMaxLength(100).IsRequired();
            entity.Property(x => x.PinHash).HasMaxLength(512).IsRequired();
            entity.Property(x => x.Role).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.HasIndex(x => x.NormalizedUsername).IsUnique();
        });

        modelBuilder.Entity<UserSession>(entity =>
        {
            entity.ToTable("user_sessions");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();
            entity.HasIndex(x => x.TokenHash).IsUnique();
            entity.HasIndex(x => new { x.UserId, x.ExpiresAt });
            entity.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Vehicle>(entity =>
        {
            entity.ToTable("vehicles");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.LicensePlate).HasMaxLength(20).IsRequired();
            entity.Property(x => x.NormalizedLicensePlate).HasMaxLength(16).IsRequired();
            entity.Property(x => x.DisplayName).HasMaxLength(100);
            entity.HasIndex(x => x.NormalizedLicensePlate).IsUnique();
        });

        modelBuilder.Entity<UserVehicle>(entity =>
        {
            entity.ToTable("user_vehicles");
            entity.HasKey(x => new { x.UserId, x.VehicleId });
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Vehicle>().WithMany().HasForeignKey(x => x.VehicleId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<ParkingSystemSettings>(entity =>
        {
            entity.ToTable("parking_system_settings");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.MaxConcurrentVisits).IsRequired();
            entity.Property(x => x.LongVisitWarningAfter);
            entity.Property(x => x.NotifyAdminOnLongVisit).IsRequired();
            entity.Property(x => x.LongVisitReminderInterval);
            entity.Property(x => x.BudgetWarningThresholdPercentages).HasColumnType("integer[]").IsRequired();
        });
        modelBuilder.Entity<DefaultParkingPolicy>(entity =>
        {
            entity.ToTable("default_parking_policy");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.MaxPaidParkingDuration);
        });
        modelBuilder.Entity<ParkingRuleSet>(entity =>
        {
            entity.ToTable("parking_rule_sets"); entity.HasKey(x => x.Id);
            entity.Property(x => x.Continuation).HasConversion<string>().HasMaxLength(30).IsRequired();
            entity.HasOne<ParkingProviderProduct>().WithMany().HasForeignKey(x => x.ProviderProductId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.ProviderProductId, x.ValidFrom });
            entity.HasMany(x => x.PaidWindows).WithOne().HasForeignKey(x => x.ParkingRuleSetId).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(x => x.CalendarExceptions).WithOne().HasForeignKey(x => x.ParkingRuleSetId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<ParkingCalendarException>(entity =>
        {
            entity.ToTable("parking_calendar_exceptions");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Date).HasColumnType("date");
            entity.HasIndex(x => new { x.ParkingRuleSetId, x.Date }).IsUnique();
        });
        modelBuilder.Entity<PaidWindow>(entity =>
        {
            entity.ToTable("paid_windows"); entity.HasKey(x => x.Id);
            entity.Property(x => x.Day).HasConversion<int>();
            entity.Property(x => x.Start).HasColumnType("time without time zone");
            entity.Property(x => x.End).HasColumnType("time without time zone");
            entity.HasIndex(x => new { x.ParkingRuleSetId, x.Day, x.Start, x.End }).IsUnique();
        });
        modelBuilder.Entity<UserPolicyOverride>(entity =>
        {
            entity.ToTable("user_policy_overrides");
            entity.HasKey(x => x.UserId);
            entity.Property(x => x.MaxPaidParkingDurationMode)
                .HasConversion<string>()
                .HasMaxLength(20)
                .IsRequired();
            entity.Property(x => x.MaxVisitElapsedDurationMode)
                .HasConversion<string>()
                .HasMaxLength(20)
                .IsRequired();
            entity.HasOne<User>().WithOne().HasForeignKey<UserPolicyOverride>(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<ParkingTariff>(entity =>
        {
            entity.ToTable("parking_tariffs");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Rate).HasPrecision(18, 4);
            entity.Property(x => x.Unit).HasConversion<int>();
            entity.HasOne<ParkingProviderProduct>().WithMany().HasForeignKey(x => x.ProviderProductId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.ProviderProductId, x.ValidFrom });
        });

        modelBuilder.Entity<ParkingBudgetPeriod>(entity =>
        {
            entity.ToTable("parking_budget_periods");
            entity.HasKey(x => x.Id);
            entity.HasOne<ParkingProviderProduct>().WithMany().HasForeignKey(x => x.ProviderProductId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.ProviderProductId, x.ValidFrom });
        });

        modelBuilder.Entity<ParkingBudgetWarningState>(entity =>
        {
            entity.ToTable("parking_budget_warning_states");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.ParkingBudgetPeriodId, x.ThresholdPercentage }).IsUnique();
            entity.HasOne<ParkingBudgetPeriod>().WithMany().HasForeignKey(x => x.ParkingBudgetPeriodId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ParkingProviderProduct>(entity =>
        {
            entity.ToTable("parking_provider_products");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ProviderProductId).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.CategoryId).HasMaxLength(100);
            entity.Property(x => x.CategoryName).HasMaxLength(200);
            entity.Property(x => x.Location).HasMaxLength(100).IsRequired();
            entity.HasIndex(x => x.ProviderProductId).IsUnique();
            entity.HasIndex(x => x.IsDefault).IsUnique().HasFilter("\"IsDefault\" = TRUE");
        });

        modelBuilder.Entity<AdminAuditEvent>(entity =>
        {
            entity.ToTable("admin_audit_events");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Action).HasMaxLength(100).IsRequired();
            entity.Property(x => x.TargetType).HasMaxLength(100).IsRequired();
            entity.Property(x => x.TargetId).HasMaxLength(200);
            entity.Property(x => x.ContextJson).HasColumnType("jsonb");
            entity.HasIndex(x => x.CreatedAt);
            entity.HasIndex(x => new { x.ActorUserId, x.CreatedAt });
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ProviderDiscrepancy>(entity =>
        {
            entity.ToTable("provider_discrepancies");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Key).HasMaxLength(500).IsRequired();
            entity.Property(x => x.Type).HasConversion<string>().HasMaxLength(50).IsRequired();
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(x => x.ProviderActionId).HasMaxLength(200);
            entity.Property(x => x.ProviderStatus).HasMaxLength(100);
            entity.Property(x => x.Version).IsRowVersion();
            entity.HasIndex(x => x.Key)
                .IsUnique()
                .HasFilter("\"Status\" = 'Open'");
            entity.HasIndex(x => new { x.Status, x.LastObservedAt });
            entity.HasIndex(x => x.ProviderProductId);
            entity.HasIndex(x => x.VisitId);
            entity.HasIndex(x => x.ProviderParkingActionId);
            entity.HasOne<ParkingProviderProduct>().WithMany().HasForeignKey(x => x.ProviderProductId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Visit>().WithMany().HasForeignKey(x => x.VisitId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ProviderParkingAction>().WithMany().HasForeignKey(x => x.ProviderParkingActionId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ProviderParkingAction>(entity =>
        {
            entity.ToTable("provider_parking_actions");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ProviderActionId).HasMaxLength(200);
            entity.Property(x => x.ProviderProductId).HasMaxLength(200);
            entity.Property(x => x.ProviderLocation).HasMaxLength(100);
            entity.Property(x => x.ProviderStatus).HasMaxLength(100);
            entity.Property(x => x.ProviderCostAmount);
            entity.Property(x => x.State).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(x => x.Health).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(x => x.Version).IsRowVersion();
            entity.HasOne<Visit>().WithMany().HasForeignKey(x => x.VisitId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => x.VisitId);
            entity.HasIndex(x => x.ProviderActionId).IsUnique().HasFilter("\"ProviderActionId\" IS NOT NULL");
        });

        modelBuilder.Entity<ProviderOperation>(entity =>
        {
            entity.ToTable("provider_operations");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Type).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(x => x.LastErrorCode).HasMaxLength(100);
            entity.Property(x => x.AttemptStartedAt);
            entity.Property(x => x.RequestedEndAt);
            entity.Property(x => x.ParentOperationId);
            entity.Property(x => x.Version).IsRowVersion();
            entity.HasIndex(x => x.OperationId).IsUnique();
            entity.HasIndex(x => x.ParentOperationId);
            entity.HasIndex(x => new { x.ParentOperationId, x.Type, x.ProviderParkingActionId })
                .IsUnique()
                .HasFilter("\"ParentOperationId\" IS NOT NULL");
            entity.HasOne<Visit>().WithMany().HasForeignKey(x => x.VisitId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ProviderParkingAction>().WithMany().HasForeignKey(x => x.ProviderParkingActionId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<NotificationEvent>(entity =>
        {
            entity.ToTable("notification_events");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Type).HasConversion<string>().HasMaxLength(50).IsRequired();
            entity.HasIndex(x => new { x.Type, x.AggregateId }).IsUnique();
        });

        modelBuilder.Entity<Notification>(entity =>
        {
            entity.ToTable("notifications");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Type).HasConversion<string>().HasMaxLength(60).IsRequired();
            entity.Property(x => x.Payload).HasColumnType("jsonb");
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.RecipientUserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Visit>().WithMany().HasForeignKey(x => x.VisitId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.RecipientUserId, x.CreatedAt });
            entity.HasIndex(x => new { x.RecipientUserId, x.ReadAt });
            entity.HasIndex(x => x.VisitId);
            entity.HasIndex(x => new { x.RecipientUserId, x.SourceEventId })
                .IsUnique()
                .HasFilter("\"SourceEventId\" IS NOT NULL");
        });


        modelBuilder.Entity<PushDelivery>(entity =>
        {
            entity.ToTable("push_deliveries");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.HasIndex(x => x.NotificationId).IsUnique();
            entity.HasIndex(x => new { x.Status, x.LastAttemptAt });
            entity.HasOne<Notification>().WithOne().HasForeignKey<PushDelivery>(x => x.NotificationId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PushSubscription>(entity =>
        {
            entity.ToTable("push_subscriptions");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Endpoint).HasMaxLength(2048).IsRequired();
            entity.Property(x => x.P256dh).HasMaxLength(512).IsRequired();
            entity.Property(x => x.Auth).HasMaxLength(512).IsRequired();
            entity.HasIndex(x => x.Endpoint).IsUnique();
            entity.HasIndex(x => x.UserId);
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });


        modelBuilder.Entity<VisitEndTimeChange>(entity =>
        {
            entity.ToTable("visit_end_time_changes");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Result).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.HasIndex(x => x.OperationId).IsUnique();
            entity.HasIndex(x => x.VisitId);
            entity.HasOne<Visit>().WithMany().HasForeignKey(x => x.VisitId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<VisitSchedulerWork>(entity =>
        {
            entity.ToTable("visit_scheduler_work");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Type).HasConversion<string>().HasMaxLength(30).IsRequired();
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(x => x.Version).IsRowVersion();
            entity.HasIndex(x => new { x.Status, x.DueAt });
            entity.HasIndex(x => new { x.VisitId, x.Type, x.DueAt })
                .IsUnique()
                .HasFilter("\"Status\" IN ('Pending', 'Claimed')");
            entity.HasOne<Visit>().WithMany().HasForeignKey(x => x.VisitId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Visit>(entity =>
        {
            entity.ToTable("visits");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(x => x.Health).HasConversion<string>().HasMaxLength(30).IsRequired();
            entity.Property(x => x.ProviderProductExternalId).HasMaxLength(200);
            entity.Property(x => x.ProviderLocation).HasMaxLength(100);
            entity.Property(x => x.Version).IsRowVersion();
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Vehicle>().WithMany().HasForeignKey(x => x.VehicleId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.StartedByUserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ParkingProviderProduct>().WithMany().HasForeignKey(x => x.ProviderProductId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => x.ProviderProductId);
            entity.OwnsOne(x => x.PolicySnapshot, owned =>
            {
                owned.Property(x => x.MaxPaidParkingDuration).HasColumnName("PolicyMaxPaidParkingDuration");
                owned.Property(x => x.MaxVisitElapsedDuration).HasColumnName("PolicyMaxVisitElapsedDuration");
                owned.Property(x => x.AllowVisitExtension).HasColumnName("PolicyAllowVisitExtension");
                owned.Property(x => x.AllowOpenEndedVisits).HasColumnName("PolicyAllowOpenEndedVisits");
            });
            entity.HasIndex(x => x.StartOperationId).IsUnique();
            entity.HasIndex(x => new { x.Status, x.StartAt });
        });

    }
}
