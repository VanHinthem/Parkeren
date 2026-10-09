using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Parkeren.Infrastructure.Persistence.Migrations;

public partial class AddProviderHistorySyncStates : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "provider_history_sync_states",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ProviderProductId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                NextPageNumber = table.Column<int>(type: "integer", nullable: false),
                PageSize = table.Column<int>(type: "integer", nullable: false),
                LastSuccessfulSyncAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                LastAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                LastError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_provider_history_sync_states", x => x.Id));

        migrationBuilder.CreateIndex(
            name: "IX_provider_history_sync_states_ProviderProductId",
            table: "provider_history_sync_states",
            column: "ProviderProductId",
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "provider_history_sync_states");
    }
}
