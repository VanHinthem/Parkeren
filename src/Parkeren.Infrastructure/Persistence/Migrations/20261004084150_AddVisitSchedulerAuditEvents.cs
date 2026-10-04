using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Parkeren.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddVisitSchedulerAuditEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "visit_scheduler_audit_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VisitId = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SourceType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    SourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    GroupKey = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    AttemptNumber = table.Column<int>(type: "integer", nullable: true),
                    ReasonCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DetailsJson = table.Column<string>(type: "jsonb", nullable: true),
                    EventKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_visit_scheduler_audit_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_visit_scheduler_audit_events_visits_VisitId",
                        column: x => x.VisitId,
                        principalTable: "visits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_visit_scheduler_audit_events_EventKey",
                table: "visit_scheduler_audit_events",
                column: "EventKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_visit_scheduler_audit_events_VisitId_GroupKey_OccurredAt",
                table: "visit_scheduler_audit_events",
                columns: new[] { "VisitId", "GroupKey", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_visit_scheduler_audit_events_VisitId_OccurredAt",
                table: "visit_scheduler_audit_events",
                columns: new[] { "VisitId", "OccurredAt" });

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "visit_scheduler_audit_events");
        }
    }
}
