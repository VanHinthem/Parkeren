using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Parkeren.Infrastructure.Persistence.Migrations;

[DbContext(typeof(ParkerenDbContext))]
[Migration("20260927105500_AddVehicles")]
public sealed class AddVehicles : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "vehicles",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                LicensePlate = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                NormalizedLicensePlate = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                DisplayName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_vehicles", x => x.Id));

        migrationBuilder.CreateTable(
            name: "user_vehicles",
            columns: table => new
            {
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                VehicleId = table.Column<Guid>(type: "uuid", nullable: false),
                AssignedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_user_vehicles", x => new { x.UserId, x.VehicleId });
                table.ForeignKey("FK_user_vehicles_users_UserId", x => x.UserId, "users", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_user_vehicles_vehicles_VehicleId", x => x.VehicleId, "vehicles", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex("IX_vehicles_NormalizedLicensePlate", "vehicles", "NormalizedLicensePlate", unique: true);
        migrationBuilder.CreateIndex("IX_user_vehicles_VehicleId", "user_vehicles", "VehicleId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "user_vehicles");
        migrationBuilder.DropTable(name: "vehicles");
    }
}
