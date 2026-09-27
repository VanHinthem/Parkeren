using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace Parkeren.Infrastructure.Persistence.Migrations;

[DbContext(typeof(ParkerenDbContext))]
[Migration("20260927144500_AddProviderOperations")]
public partial class AddProviderOperations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(name: "provider_parking_actions", columns: table => new
        {
            Id = table.Column<Guid>(type: "uuid", nullable: false), VisitId = table.Column<Guid>(type: "uuid", nullable: true), ProviderActionId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
            PlannedStartAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false), PlannedEndAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false), ActualStartAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true), ActualEndAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
            ProviderStatus = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true), State = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false), Health = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false), CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false), Version = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
        }, constraints: table => { table.PrimaryKey("PK_provider_parking_actions", x => x.Id); table.ForeignKey("FK_provider_parking_actions_visits_VisitId", x => x.VisitId, "visits", "Id", onDelete: ReferentialAction.Restrict); });
        migrationBuilder.CreateIndex("IX_provider_parking_actions_VisitId", "provider_parking_actions", "VisitId");

        migrationBuilder.CreateTable(name: "provider_operations", columns: table => new
        {
            Id = table.Column<Guid>(type: "uuid", nullable: false), OperationId = table.Column<Guid>(type: "uuid", nullable: false), VisitId = table.Column<Guid>(type: "uuid", nullable: true), ProviderParkingActionId = table.Column<Guid>(type: "uuid", nullable: true), Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false), Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false), AttemptCount = table.Column<int>(type: "integer", nullable: false), LastErrorCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true), CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false), CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true), Version = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
        }, constraints: table => { table.PrimaryKey("PK_provider_operations", x => x.Id); table.ForeignKey("FK_provider_operations_visits_VisitId", x => x.VisitId, "visits", "Id", onDelete: ReferentialAction.Restrict); table.ForeignKey("FK_provider_operations_provider_parking_actions_ProviderParkingActionId", x => x.ProviderParkingActionId, "provider_parking_actions", "Id", onDelete: ReferentialAction.Restrict); });
        migrationBuilder.CreateIndex("IX_provider_operations_OperationId", "provider_operations", "OperationId", unique: true);
        migrationBuilder.CreateIndex("IX_provider_operations_VisitId", "provider_operations", "VisitId");
        migrationBuilder.CreateIndex("IX_provider_operations_ProviderParkingActionId", "provider_operations", "ProviderParkingActionId");
    }
    protected override void Down(MigrationBuilder migrationBuilder) { migrationBuilder.DropTable("provider_operations"); migrationBuilder.DropTable("provider_parking_actions"); }
}
