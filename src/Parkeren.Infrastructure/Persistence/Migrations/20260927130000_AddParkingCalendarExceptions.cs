using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
#nullable disable
namespace Parkeren.Infrastructure.Persistence.Migrations;
[DbContext(typeof(ParkerenDbContext))]
[Migration("20260927130000_AddParkingCalendarExceptions")]
public sealed class AddParkingCalendarExceptions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(name: "parking_calendar_exceptions", columns: table => new
        {
            Id = table.Column<Guid>(type: "uuid", nullable: false),
            ParkingRuleSetId = table.Column<Guid>(type: "uuid", nullable: false),
            Date = table.Column<DateOnly>(type: "date", nullable: false),
            IsPaid = table.Column<bool>(type: "boolean", nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_parking_calendar_exceptions", x => x.Id);
            table.ForeignKey("FK_parking_calendar_exceptions_parking_rule_sets_ParkingRuleSetId", x => x.ParkingRuleSetId, "parking_rule_sets", "Id", onDelete: ReferentialAction.Cascade);
        });
        migrationBuilder.CreateIndex(name: "IX_parking_calendar_exceptions_ParkingRuleSetId_Date", table: "parking_calendar_exceptions", columns: new[] { "ParkingRuleSetId", "Date" }, unique: true);
    }
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable(name: "parking_calendar_exceptions");
}
