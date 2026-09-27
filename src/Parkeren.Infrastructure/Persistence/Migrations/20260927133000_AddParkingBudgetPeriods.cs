using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Parkeren.Infrastructure.Persistence.Migrations;

public partial class AddParkingBudgetPeriods : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "parking_budget_periods",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ValidFrom = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                ValidUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                MaximumPaidDuration = table.Column<TimeSpan>(type: "interval", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_parking_budget_periods", x => x.Id));

        migrationBuilder.CreateIndex(
            name: "IX_parking_budget_periods_ValidFrom",
            table: "parking_budget_periods",
            column: "ValidFrom");
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: "parking_budget_periods");
}
