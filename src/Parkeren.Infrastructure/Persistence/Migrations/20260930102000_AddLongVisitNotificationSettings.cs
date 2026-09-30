using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Parkeren.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLongVisitNotificationSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<TimeSpan>(
                name: "LongVisitReminderInterval",
                table: "parking_system_settings",
                type: "interval",
                nullable: true);

            migrationBuilder.AddColumn<TimeSpan>(
                name: "LongVisitWarningAfter",
                table: "parking_system_settings",
                type: "interval",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "NotifyAdminOnLongVisit",
                table: "parking_system_settings",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LongVisitReminderInterval",
                table: "parking_system_settings");

            migrationBuilder.DropColumn(
                name: "LongVisitWarningAfter",
                table: "parking_system_settings");

            migrationBuilder.DropColumn(
                name: "NotifyAdminOnLongVisit",
                table: "parking_system_settings");
        }
    }
}
