using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Parkeren.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProviderActionReconciliationWork : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "HistoryStatus",
                table: "provider_parking_actions",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "NotRequired");

            migrationBuilder.DropIndex(
                name: "IX_visit_scheduler_work_VisitId_Type_DueAt",
                table: "visit_scheduler_work");

            migrationBuilder.AddColumn<int>(
                name: "AttemptCount",
                table: "visit_scheduler_work",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "ProviderParkingActionId",
                table: "visit_scheduler_work",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_visit_scheduler_work_ProviderParkingActionId",
                table: "visit_scheduler_work",
                column: "ProviderParkingActionId",
                unique: true,
                filter: "\"ProviderParkingActionId\" IS NOT NULL AND \"Type\" = 'ReconcileProviderAction' AND \"Status\" IN ('Pending', 'Claimed')");

            migrationBuilder.CreateIndex(
                name: "IX_visit_scheduler_work_VisitId_Type_DueAt",
                table: "visit_scheduler_work",
                columns: new[] { "VisitId", "Type", "DueAt" },
                unique: true,
                filter: "\"Status\" IN ('Pending', 'Claimed') AND \"Type\" <> 'ReconcileProviderAction'");

            migrationBuilder.AddForeignKey(
                name: "FK_visit_scheduler_work_provider_parking_actions_ProviderParki~",
                table: "visit_scheduler_work",
                column: "ProviderParkingActionId",
                principalTable: "provider_parking_actions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_visit_scheduler_work_provider_parking_actions_ProviderParki~",
                table: "visit_scheduler_work");

            migrationBuilder.DropIndex(
                name: "IX_visit_scheduler_work_ProviderParkingActionId",
                table: "visit_scheduler_work");

            migrationBuilder.DropIndex(
                name: "IX_visit_scheduler_work_VisitId_Type_DueAt",
                table: "visit_scheduler_work");

            migrationBuilder.DropColumn(
                name: "AttemptCount",
                table: "visit_scheduler_work");

            migrationBuilder.DropColumn(
                name: "ProviderParkingActionId",
                table: "visit_scheduler_work");

            migrationBuilder.DropColumn(
                name: "HistoryStatus",
                table: "provider_parking_actions");

            migrationBuilder.CreateIndex(
                name: "IX_visit_scheduler_work_VisitId_Type_DueAt",
                table: "visit_scheduler_work",
                columns: new[] { "VisitId", "Type", "DueAt" },
                unique: true,
                filter: "\"Status\" IN ('Pending', 'Claimed')");
        }
    }
}
