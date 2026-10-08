using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Parkeren.Infrastructure.Persistence.Migrations;

public partial class AddProviderHistorySyncRuns : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "provider_history_sync_runs",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ProviderProductId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                Mode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                FinishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                ReadCount = table.Column<int>(type: "integer", nullable: false),
                InsertedCount = table.Column<int>(type: "integer", nullable: false),
                RefreshedCount = table.Column<int>(type: "integer", nullable: false),
                SkippedCount = table.Column<int>(type: "integer", nullable: false),
                Error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_provider_history_sync_runs", x => x.Id));

        migrationBuilder.CreateIndex(
            name: "IX_provider_history_sync_runs_ProviderProductId_StartedAt",
            table: "provider_history_sync_runs",
            columns: new[] { "ProviderProductId", "StartedAt" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "provider_history_sync_runs");
    }
}
