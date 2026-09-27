using Microsoft.EntityFrameworkCore;
using Parkeren.Domain.Users;
using Parkeren.Domain.Vehicles;

namespace Parkeren.Infrastructure.Persistence;

public sealed class ParkerenDbContext(DbContextOptions<ParkerenDbContext> options)
    : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<UserSession> UserSessions => Set<UserSession>();
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<UserVehicle> UserVehicles => Set<UserVehicle>();

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
    }
}
