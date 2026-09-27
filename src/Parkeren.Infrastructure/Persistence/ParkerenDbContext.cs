using Microsoft.EntityFrameworkCore;
using Parkeren.Domain.Users;
using Parkeren.Domain.Vehicles;
using Parkeren.Domain.Policies;
using Parkeren.Domain.Rules;
using Parkeren.Domain.Visits;
using Parkeren.Domain.Notifications;

namespace Parkeren.Infrastructure.Persistence;

public sealed class ParkerenDbContext(DbContextOptions<ParkerenDbContext> options)
    : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<UserSession> UserSessions => Set<UserSession>();
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<UserVehicle> UserVehicles => Set<UserVehicle>();
    public DbSet<DefaultParkingPolicy> DefaultParkingPolicies => Set<DefaultParkingPolicy>();
    public DbSet<UserPolicyOverride> UserPolicyOverrides => Set<UserPolicyOverride>();
    public DbSet<ParkingRuleSet> ParkingRuleSets => Set<ParkingRuleSet>();
    public DbSet<PaidWindow> PaidWindows => Set<PaidWindow>();
    public DbSet<ParkingCalendarException> ParkingCalendarExceptions => Set<ParkingCalendarException>();
    public DbSet<Visit> Visits => Set<Visit>();
    public DbSet<ProviderParkingAction> ProviderParkingActions => Set<ProviderParkingAction>();
    public DbSet<ProviderOperation> ProviderOperations => Set<ProviderOperation>();
    public DbSet<NotificationEvent> NotificationEvents => Set<NotificationEvent>();

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
        modelBuilder.Entity<DefaultParkingPolicy>(entity =>
        {
            entity.ToTable("default_parking_policy");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.MaxPaidParkingDuration).IsRequired();
        });
        modelBuilder.Entity<ParkingRuleSet>(entity =>
        {
            entity.ToTable("parking_rule_sets"); entity.HasKey(x => x.Id);
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
            entity.HasOne<User>().WithOne().HasForeignKey<UserPolicyOverride>(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<ParkingTariff>(entity =>
        {
            entity.ToTable("parking_tariffs");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Rate).HasPrecision(18, 4);
            entity.Property(x => x.Unit).HasConversion<int>();
            entity.HasIndex(x => x.ValidFrom);
        });

        modelBuilder.Entity<ParkingBudgetPeriod>(entity =>
        {
            entity.ToTable("parking_budget_periods");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.ValidFrom);
        });

        modelBuilder.Entity<ProviderParkingAction>(entity =>
        {
            entity.ToTable("provider_parking_actions");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ProviderActionId).HasMaxLength(200);
            entity.Property(x => x.ProviderStatus).HasMaxLength(100);
            entity.Property(x => x.State).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(x => x.Health).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(x => x.Version).IsRowVersion();
            entity.HasOne<Visit>().WithMany().HasForeignKey(x => x.VisitId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => x.VisitId);
        });

        modelBuilder.Entity<ProviderOperation>(entity =>
        {
            entity.ToTable("provider_operations");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Type).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(x => x.LastErrorCode).HasMaxLength(100);
            entity.Property(x => x.AttemptStartedAt);
            entity.Property(x => x.Version).IsRowVersion();
            entity.HasIndex(x => x.OperationId).IsUnique();
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

        modelBuilder.Entity<Visit>(entity =>
        {
            entity.ToTable("visits");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(x => x.Health).HasConversion<string>().HasMaxLength(30).IsRequired();
            entity.Property(x => x.Version).IsRowVersion();
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Vehicle>().WithMany().HasForeignKey(x => x.VehicleId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.StartedByUserId).OnDelete(DeleteBehavior.Restrict);
            entity.OwnsOne(x => x.PolicySnapshot, owned =>
            {
                owned.Property(x => x.MaxPaidParkingDuration).HasColumnName("PolicyMaxPaidParkingDuration");
                owned.Property(x => x.MaxVisitElapsedDuration).HasColumnName("PolicyMaxVisitElapsedDuration");
                owned.Property(x => x.AllowAutoExtension).HasColumnName("PolicyAllowAutoExtension");
            });
            entity.HasIndex(x => x.StartOperationId).IsUnique();
            entity.HasIndex(x => new { x.Status, x.StartAt });
        });

    }
}
