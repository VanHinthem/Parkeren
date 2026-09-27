using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Parkeren.Infrastructure.Persistence.Migrations;

public partial class AddParkingTariffs : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "parking_tariffs",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ValidFrom = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                ValidUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                HourlyRate = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_parking_tariffs", x => x.Id));

        migrationBuilder.CreateIndex(
            name: "IX_parking_tariffs_ValidFrom",
            table: "parking_tariffs",
            column: "ValidFrom");
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: "parking_tariffs");
}
