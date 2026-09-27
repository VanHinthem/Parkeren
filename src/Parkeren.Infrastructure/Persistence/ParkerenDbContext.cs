using Microsoft.EntityFrameworkCore;
using Parkeren.Domain.Users;
using Parkeren.Domain.Vehicles;
using Parkeren.Domain.Policies;
using Parkeren.Domain.Rules;

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
            entity.Property(x => x.HourlyRate).HasPrecision(18, 4);
            entity.HasIndex(x => x.ValidFrom);
        });

    }
}
