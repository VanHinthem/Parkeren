using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Parkeren.Infrastructure.Persistence;

#nullable disable

namespace Parkeren.Infrastructure.Persistence.Migrations;

[DbContext(typeof(ParkerenDbContext))]
[Migration("20260927204000_AddAllowManualStop")]
public partial class AddAllowManualStop : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "AllowManualStop",
            table: "default_parking_policy",
            type: "boolean",
            nullable: false,
            defaultValue: true);

        migrationBuilder.AddColumn<bool>(
            name: "AllowManualStop",
            table: "user_policy_overrides",
            type: "boolean",
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "PolicyAllowManualStop",
            table: "visits",
            type: "boolean",
            nullable: false,
            defaultValue: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "AllowManualStop", table: "default_parking_policy");
        migrationBuilder.DropColumn(name: "AllowManualStop", table: "user_policy_overrides");
        migrationBuilder.DropColumn(name: "PolicyAllowManualStop", table: "visits");
    }
}
