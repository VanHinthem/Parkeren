using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace Parkeren.Infrastructure.Persistence.Migrations;

[DbContext(typeof(ParkerenDbContext))]
[Migration("20260927195500_AddVisitEndTimeChanges")]
public partial class AddVisitEndTimeChanges : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "visit_end_time_changes",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                VisitId = table.Column<Guid>(type: "uuid", nullable: false),
                ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                PreviousDesiredEndAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                RequestedDesiredEndAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                Result = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_visit_end_time_changes", x => x.Id);
                table.ForeignKey(
                    name: "FK_visit_end_time_changes_users_ActorUserId",
                    column: x => x.ActorUserId,
                    principalTable: "users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_visit_end_time_changes_visits_VisitId",
                    column: x => x.VisitId,
                    principalTable: "visits",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_visit_end_time_changes_ActorUserId",
            table: "visit_end_time_changes",
            column: "ActorUserId");

        migrationBuilder.CreateIndex(
            name: "IX_visit_end_time_changes_OperationId",
            table: "visit_end_time_changes",
            column: "OperationId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_visit_end_time_changes_VisitId",
            table: "visit_end_time_changes",
            column: "VisitId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "visit_end_time_changes");
    }
}
