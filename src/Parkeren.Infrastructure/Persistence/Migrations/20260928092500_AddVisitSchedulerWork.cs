using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Parkeren.Infrastructure.Persistence;

#nullable disable

namespace Parkeren.Infrastructure.Persistence.Migrations;

[DbContext(typeof(ParkerenDbContext))]
[Migration("20260928092500_AddVisitSchedulerWork")]
public partial class AddVisitSchedulerWork : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "visit_scheduler_work",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                VisitId = table.Column<Guid>(type: "uuid", nullable: false),
                Type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                DueAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                ClaimedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                ClaimedBy = table.Column<string>(type: "text", nullable: true),
                CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_visit_scheduler_work", x => x.Id);
                table.ForeignKey(
                    name: "FK_visit_scheduler_work_visits_VisitId",
                    column: x => x.VisitId,
                    principalTable: "visits",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_visit_scheduler_work_Status_DueAt",
            table: "visit_scheduler_work",
            columns: new[] { "Status", "DueAt" });

        migrationBuilder.CreateIndex(
            name: "IX_visit_scheduler_work_VisitId_Type_DueAt",
            table: "visit_scheduler_work",
            columns: new[] { "VisitId", "Type", "DueAt" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "visit_scheduler_work");
    }
}
