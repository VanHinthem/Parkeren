using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Parkeren.Infrastructure.Persistence.Migrations;

[DbContext(typeof(ParkerenDbContext))]
[Migration("20260927140000_ConfigureParkingTariffUnit")]
public partial class ConfigureParkingTariffUnit : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.RenameColumn(name: "HourlyRate", table: "parking_tariffs", newName: "Rate");
        migrationBuilder.AddColumn<int>(name: "Unit", table: "parking_tariffs", type: "integer", nullable: false, defaultValue: 0);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "Unit", table: "parking_tariffs");
        migrationBuilder.RenameColumn(name: "Rate", table: "parking_tariffs", newName: "HourlyRate");
    }
}
