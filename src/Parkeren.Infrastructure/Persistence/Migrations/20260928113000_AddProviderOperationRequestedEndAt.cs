using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Parkeren.Infrastructure.Persistence;

#nullable disable

namespace Parkeren.Infrastructure.Persistence.Migrations;

[DbContext(typeof(ParkerenDbContext))]
[Migration("20260928113000_AddProviderOperationRequestedEndAt")]
public partial class AddProviderOperationRequestedEndAt : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "RequestedEndAt",
            table: "provider_operations",
            type: "timestamp with time zone",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "RequestedEndAt", table: "provider_operations");
    }
}
