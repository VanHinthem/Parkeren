using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Parkeren.Infrastructure.Persistence.Migrations;

public partial class AddProviderActionAttribution : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "AssignedUserId",
            table: "provider_parking_actions",
            type: "uuid",
            nullable: true);
        migrationBuilder.AddColumn<string>(
            name: "AssignmentSource",
            table: "provider_parking_actions",
            type: "character varying(20)",
            maxLength: 20,
            nullable: false,
            defaultValue: "Unassigned");
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "FirstObservedAt",
            table: "provider_parking_actions",
            type: "timestamp with time zone",
            nullable: true);
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "LastSyncedAt",
            table: "provider_parking_actions",
            type: "timestamp with time zone",
            nullable: true);
        migrationBuilder.AddColumn<string>(
            name: "Origin",
            table: "provider_parking_actions",
            type: "character varying(20)",
            maxLength: 20,
            nullable: false,
            defaultValue: "Managed");
        migrationBuilder.AddColumn<Guid>(
            name: "VehicleId",
            table: "provider_parking_actions",
            type: "uuid",
            nullable: true);
        migrationBuilder.CreateIndex(
            name: "IX_provider_parking_actions_AssignedUserId",
            table: "provider_parking_actions",
            column: "AssignedUserId");
        migrationBuilder.CreateIndex(
            name: "IX_provider_parking_actions_VehicleId",
            table: "provider_parking_actions",
            column: "VehicleId");
        migrationBuilder.AddForeignKey(
            name: "FK_provider_parking_actions_users_AssignedUserId",
            table: "provider_parking_actions",
            column: "AssignedUserId",
            principalTable: "users",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
        migrationBuilder.AddForeignKey(
            name: "FK_provider_parking_actions_vehicles_VehicleId",
            table: "provider_parking_actions",
            column: "VehicleId",
            principalTable: "vehicles",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        // Existing managed actions retain their confirmed Visit owner, if present.
        migrationBuilder.Sql("""
            UPDATE provider_parking_actions AS a
            SET "AssignedUserId" = v."UserId",
                "VehicleId" = v."VehicleId",
                "AssignmentSource" = 'Confirmed'
            FROM visits AS v
            WHERE a."VisitId" = v."Id";
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey("FK_provider_parking_actions_users_AssignedUserId", "provider_parking_actions");
        migrationBuilder.DropForeignKey("FK_provider_parking_actions_vehicles_VehicleId", "provider_parking_actions");
        migrationBuilder.DropIndex("IX_provider_parking_actions_AssignedUserId", "provider_parking_actions");
        migrationBuilder.DropIndex("IX_provider_parking_actions_VehicleId", "provider_parking_actions");
        migrationBuilder.DropColumn("AssignedUserId", "provider_parking_actions");
        migrationBuilder.DropColumn("AssignmentSource", "provider_parking_actions");
        migrationBuilder.DropColumn("FirstObservedAt", "provider_parking_actions");
        migrationBuilder.DropColumn("LastSyncedAt", "provider_parking_actions");
        migrationBuilder.DropColumn("Origin", "provider_parking_actions");
        migrationBuilder.DropColumn("VehicleId", "provider_parking_actions");
    }
}
