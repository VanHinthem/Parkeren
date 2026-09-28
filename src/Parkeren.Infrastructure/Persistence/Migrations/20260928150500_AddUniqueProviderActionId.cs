using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace Parkeren.Infrastructure.Persistence.Migrations;

[DbContext(typeof(ParkerenDbContext))]
[Migration("20260928150500_AddUniqueProviderActionId")]
public partial class AddUniqueProviderActionId : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "IX_provider_parking_actions_ProviderActionId",
            table: "provider_parking_actions",
            column: "ProviderActionId",
            unique: true,
            filter: "\"ProviderActionId\" IS NOT NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_provider_parking_actions_ProviderActionId",
            table: "provider_parking_actions");
    }
}
