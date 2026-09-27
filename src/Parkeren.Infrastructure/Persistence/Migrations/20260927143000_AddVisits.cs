using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Parkeren.Infrastructure.Persistence.Migrations;

[DbContext(typeof(ParkerenDbContext))]
[Migration("20260927143000_AddVisits")]
public partial class AddVisits : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "visits",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                VehicleId = table.Column<Guid>(type: "uuid", nullable: false),
                StartedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                StartAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                DesiredEndAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                ActualEndAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                Health = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                PolicyMaxPaidParkingDuration = table.Column<TimeSpan>(type: "interval", nullable: false),
                PolicyMaxVisitElapsedDuration = table.Column<TimeSpan>(type: "interval", nullable: true),
                PolicyAllowAutoExtension = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                Version = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_visits", x => x.Id);
                table.ForeignKey("FK_visits_users_StartedByUserId", x => x.StartedByUserId, "users", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_visits_users_UserId", x => x.UserId, "users", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_visits_vehicles_VehicleId", x => x.VehicleId, "vehicles", "Id", onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateIndex("IX_visits_StartedByUserId", "visits", "StartedByUserId");
        migrationBuilder.CreateIndex("IX_visits_UserId", "visits", "UserId");
        migrationBuilder.CreateIndex("IX_visits_VehicleId", "visits", "VehicleId");
        migrationBuilder.CreateIndex("IX_visits_Status_StartAt", "visits", new[] { "Status", "StartAt" });
    }

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable("visits");
}
