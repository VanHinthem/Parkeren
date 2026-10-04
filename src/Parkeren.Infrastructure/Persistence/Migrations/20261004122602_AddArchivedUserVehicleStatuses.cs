using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Parkeren.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddArchivedUserVehicleStatuses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_visit_scheduler_audit_events_visits_VisitId",
                table: "visit_scheduler_audit_events");

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "vehicles",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "users",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.Sql("UPDATE vehicles SET \"Status\" = CASE WHEN \"IsActive\" THEN 'Active' ELSE 'Inactive' END");
            migrationBuilder.Sql("UPDATE users SET \"Status\" = CASE WHEN \"IsActive\" THEN 'Active' ELSE 'Inactive' END");

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "vehicles",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "users",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(20)",
                oldMaxLength: 20,
                oldNullable: true);

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "vehicles");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "users");

            migrationBuilder.AddForeignKey(
                name: "FK_visit_scheduler_audit_events_visits_VisitId",
                table: "visit_scheduler_audit_events",
                column: "VisitId",
                principalTable: "visits",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_visit_scheduler_audit_events_visits_VisitId",
                table: "visit_scheduler_audit_events");

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "vehicles",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "users",
                type: "boolean",
                nullable: true);

            migrationBuilder.Sql("UPDATE vehicles SET \"IsActive\" = (\"Status\" = 'Active')");
            migrationBuilder.Sql("UPDATE users SET \"IsActive\" = (\"Status\" = 'Active')");

            migrationBuilder.AlterColumn<bool>(
                name: "IsActive",
                table: "vehicles",
                type: "boolean",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldNullable: true);

            migrationBuilder.AlterColumn<bool>(
                name: "IsActive",
                table: "users",
                type: "boolean",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldNullable: true);

            migrationBuilder.DropColumn(
                name: "Status",
                table: "vehicles");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "users");

            migrationBuilder.AddForeignKey(
                name: "FK_visit_scheduler_audit_events_visits_VisitId",
                table: "visit_scheduler_audit_events",
                column: "VisitId",
                principalTable: "visits",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
