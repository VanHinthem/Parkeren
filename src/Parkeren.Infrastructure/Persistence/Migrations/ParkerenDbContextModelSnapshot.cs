using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Parkeren.Infrastructure.Persistence.Migrations;

[DbContext(typeof(ParkerenDbContext))]
partial class ParkerenDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasAnnotation("ProductVersion", "10.0.0");


        modelBuilder.Entity("Parkeren.Domain.Policies.DefaultParkingPolicy", b =>
        {
            b.Property<Guid>("Id").ValueGeneratedNever().HasColumnType("uuid");
            b.Property<bool>("AllowAutoExtension").HasColumnType("boolean");
            b.Property<TimeSpan>("MaxPaidParkingDuration").HasColumnType("interval");
            b.Property<TimeSpan?>("MaxVisitElapsedDuration").HasColumnType("interval");
            b.Property<DateTimeOffset>("UpdatedAt").HasColumnType("timestamp with time zone");
            b.HasKey("Id");
            b.ToTable("default_parking_policy");
        });

        modelBuilder.Entity("Parkeren.Domain.Policies.UserPolicyOverride", b =>
        {
            b.Property<Guid>("UserId").ValueGeneratedNever().HasColumnType("uuid");
            b.Property<bool?>("AllowAutoExtension").HasColumnType("boolean");
            b.Property<TimeSpan?>("MaxPaidParkingDuration").HasColumnType("interval");
            b.Property<TimeSpan?>("MaxVisitElapsedDuration").HasColumnType("interval");
            b.Property<DateTimeOffset>("UpdatedAt").HasColumnType("timestamp with time zone");
            b.HasKey("UserId");
            b.ToTable("user_policy_overrides");
        });


        modelBuilder.Entity("Parkeren.Domain.Rules.ParkingRuleSet", b =>
        {
            b.Property<Guid>("Id").ValueGeneratedNever().HasColumnType("uuid");
            b.Property<TimeSpan>("MaxProviderActionDuration").HasColumnType("interval");
            b.Property<bool>("PublicHolidaysAreFree").HasColumnType("boolean");
            b.Property<DateTimeOffset>("ValidFrom").HasColumnType("timestamp with time zone");
            b.Property<DateTimeOffset?>("ValidUntil").HasColumnType("timestamp with time zone");
            b.HasKey("Id");
            b.ToTable("parking_rule_sets");
        });

        modelBuilder.Entity("Parkeren.Domain.Rules.ParkingBudgetPeriod", b =>
        {
            b.Property<Guid>("Id").ValueGeneratedNever().HasColumnType("uuid");
            b.Property<TimeSpan>("MaximumPaidDuration").HasColumnType("interval");
            b.Property<DateTimeOffset>("ValidFrom").HasColumnType("timestamp with time zone");
            b.Property<DateTimeOffset>("ValidUntil").HasColumnType("timestamp with time zone");
            b.HasKey("Id");
            b.HasIndex("ValidFrom");
            b.ToTable("parking_budget_periods");
        });

        modelBuilder.Entity("Parkeren.Domain.Rules.ParkingTariff", b =>
        {
            b.Property<Guid>("Id").ValueGeneratedNever().HasColumnType("uuid");
            b.Property<decimal>("HourlyRate").HasPrecision(18, 4).HasColumnType("numeric(18,4)");
            b.Property<DateTimeOffset>("ValidFrom").HasColumnType("timestamp with time zone");
            b.Property<DateTimeOffset?>("ValidUntil").HasColumnType("timestamp with time zone");
            b.HasKey("Id");
            b.HasIndex("ValidFrom");
            b.ToTable("parking_tariffs");
        });

        modelBuilder.Entity("Parkeren.Domain.Rules.PaidWindow", b =>
        {
            b.Property<Guid>("Id").ValueGeneratedNever().HasColumnType("uuid");
            b.Property<int>("Day").HasColumnType("integer");
            b.Property<TimeOnly>("End").HasColumnType("time without time zone");
            b.Property<Guid>("ParkingRuleSetId").HasColumnType("uuid");
            b.Property<TimeOnly>("Start").HasColumnType("time without time zone");
            b.HasKey("Id");
            b.HasIndex("ParkingRuleSetId", "Day", "Start", "End").IsUnique();
            b.ToTable("paid_windows");
        });

        modelBuilder.Entity("Parkeren.Domain.Rules.ParkingCalendarException", b =>
        {
            b.Property<Guid>("Id").ValueGeneratedNever().HasColumnType("uuid");
            b.Property<DateOnly>("Date").HasColumnType("date");
            b.Property<bool>("IsPaid").HasColumnType("boolean");
            b.Property<Guid>("ParkingRuleSetId").HasColumnType("uuid");
            b.HasKey("Id");
            b.HasIndex("ParkingRuleSetId", "Date").IsUnique();
            b.ToTable("parking_calendar_exceptions");
        });

        modelBuilder.Entity("Parkeren.Domain.Users.User", b =>
        {
            b.Property<Guid>("Id").ValueGeneratedNever().HasColumnType("uuid");
            b.Property<DateTimeOffset>("CreatedAt").HasColumnType("timestamp with time zone");
            b.Property<bool>("IsActive").HasColumnType("boolean");
            b.Property<string>("NormalizedUsername").IsRequired().HasMaxLength(100).HasColumnType("character varying(100)");
            b.Property<string>("PinHash").IsRequired().HasMaxLength(512).HasColumnType("character varying(512)");
            b.Property<string>("Role").IsRequired().HasMaxLength(20).HasColumnType("character varying(20)");
            b.Property<string>("Username").IsRequired().HasMaxLength(100).HasColumnType("character varying(100)");
            b.HasKey("Id");
            b.HasIndex("NormalizedUsername").IsUnique();
            b.ToTable("users");
        });

        modelBuilder.Entity("Parkeren.Domain.Users.UserSession", b =>
        {
            b.Property<Guid>("Id").ValueGeneratedNever().HasColumnType("uuid");
            b.Property<DateTimeOffset>("CreatedAt").HasColumnType("timestamp with time zone");
            b.Property<DateTimeOffset>("ExpiresAt").HasColumnType("timestamp with time zone");
            b.Property<DateTimeOffset?>("RevokedAt").HasColumnType("timestamp with time zone");
            b.Property<string>("TokenHash").IsRequired().HasMaxLength(64).HasColumnType("character varying(64)");
            b.Property<Guid>("UserId").HasColumnType("uuid");
            b.HasKey("Id");
            b.HasIndex("TokenHash").IsUnique();
            b.HasIndex("UserId", "ExpiresAt");
            b.ToTable("user_sessions");
        });

        modelBuilder.Entity("Parkeren.Domain.Vehicles.Vehicle", b =>
        {
            b.Property<Guid>("Id").ValueGeneratedNever().HasColumnType("uuid");
            b.Property<DateTimeOffset>("CreatedAt").HasColumnType("timestamp with time zone");
            b.Property<string>("DisplayName").HasMaxLength(100).HasColumnType("character varying(100)");
            b.Property<bool>("IsActive").HasColumnType("boolean");
            b.Property<string>("LicensePlate").IsRequired().HasMaxLength(20).HasColumnType("character varying(20)");
            b.Property<string>("NormalizedLicensePlate").IsRequired().HasMaxLength(16).HasColumnType("character varying(16)");
            b.HasKey("Id");
            b.HasIndex("NormalizedLicensePlate").IsUnique();
            b.ToTable("vehicles");
        });

        modelBuilder.Entity("Parkeren.Domain.Vehicles.UserVehicle", b =>
        {
            b.Property<Guid>("UserId").HasColumnType("uuid");
            b.Property<Guid>("VehicleId").HasColumnType("uuid");
            b.Property<DateTimeOffset>("AssignedAt").HasColumnType("timestamp with time zone");
            b.HasKey("UserId", "VehicleId");
            b.HasIndex("VehicleId");
            b.ToTable("user_vehicles");
        });


        modelBuilder.Entity("Parkeren.Domain.Policies.UserPolicyOverride", b =>
        {
            b.HasOne("Parkeren.Domain.Users.User", null).WithOne().HasForeignKey("Parkeren.Domain.Policies.UserPolicyOverride", "UserId").OnDelete(DeleteBehavior.Restrict).IsRequired();
        });


        modelBuilder.Entity("Parkeren.Domain.Rules.PaidWindow", b =>
        {
            b.HasOne("Parkeren.Domain.Rules.ParkingRuleSet", null).WithMany("PaidWindows").HasForeignKey("ParkingRuleSetId").OnDelete(DeleteBehavior.Cascade).IsRequired();
        });

        modelBuilder.Entity("Parkeren.Domain.Rules.ParkingCalendarException", b =>
        {
            b.HasOne("Parkeren.Domain.Rules.ParkingRuleSet", null).WithMany("CalendarExceptions").HasForeignKey("ParkingRuleSetId").OnDelete(DeleteBehavior.Cascade).IsRequired();
        });

        modelBuilder.Entity("Parkeren.Domain.Rules.ParkingRuleSet", b =>
        {
            b.Navigation("CalendarExceptions");
            b.Navigation("PaidWindows");
        });

        modelBuilder.Entity("Parkeren.Domain.Users.UserSession", b =>
        {
            b.HasOne("Parkeren.Domain.Users.User", "User").WithMany().HasForeignKey("UserId").OnDelete(DeleteBehavior.Restrict).IsRequired();
            b.Navigation("User");
        });

        modelBuilder.Entity("Parkeren.Domain.Vehicles.UserVehicle", b =>
        {
            b.HasOne("Parkeren.Domain.Users.User", null).WithMany().HasForeignKey("UserId").OnDelete(DeleteBehavior.Restrict).IsRequired();
            b.HasOne("Parkeren.Domain.Vehicles.Vehicle", null).WithMany().HasForeignKey("VehicleId").OnDelete(DeleteBehavior.Restrict).IsRequired();
        });
    }
}
