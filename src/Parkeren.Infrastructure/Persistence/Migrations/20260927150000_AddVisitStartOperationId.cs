using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace Parkeren.Infrastructure.Persistence.Migrations;

public partial class AddVisitStartOperationId : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(name: "StartOperationId", table: "visits", type: "uuid", nullable: false, defaultValue: Guid.Empty);
        migrationBuilder.CreateIndex(name: "IX_visits_StartOperationId", table: "visits", column: "StartOperationId", unique: true);
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_visits_StartOperationId", table: "visits");
        migrationBuilder.DropColumn(name: "StartOperationId", table: "visits");
    }
}
