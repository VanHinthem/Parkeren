using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Parkeren.Infrastructure.Persistence.Migrations;

[Migration("20260927173500_AddProviderOperationAttemptStartedAt")]
public partial class AddProviderOperationAttemptStartedAt : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "AttemptStartedAt",
            table: "provider_operations",
            type: "timestamp with time zone",
            nullable: true);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(name: "AttemptStartedAt", table: "provider_operations");
}
