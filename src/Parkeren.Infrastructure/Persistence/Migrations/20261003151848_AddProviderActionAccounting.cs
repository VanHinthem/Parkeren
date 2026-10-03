using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Parkeren.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProviderActionAccounting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_parking_tariffs_ValidFrom",
                table: "parking_tariffs");

            migrationBuilder.DropIndex(
                name: "IX_parking_budget_periods_ValidFrom",
                table: "parking_budget_periods");

            migrationBuilder.AlterColumn<TimeSpan>(
                name: "PolicyMaxPaidParkingDuration",
                table: "visits",
                type: "interval",
                nullable: true,
                oldClrType: typeof(TimeSpan),
                oldType: "interval");

            migrationBuilder.AddColumn<int>(
                name: "EndReason",
                table: "visits",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderLocation",
                table: "visits",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderProductExternalId",
                table: "visits",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProviderProductId",
                table: "visits",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EndReason",
                table: "visit_scheduler_work",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ProviderCostAmount",
                table: "provider_parking_actions",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderLocation",
                table: "provider_parking_actions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderProductId",
                table: "provider_parking_actions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ParentOperationId",
                table: "provider_operations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProviderProductId",
                table: "parking_tariffs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int[]>(
                name: "BudgetWarningThresholdPercentages",
                table: "parking_system_settings",
                type: "integer[]",
                nullable: false,
                defaultValue: new int[0]);

            migrationBuilder.AddColumn<Guid>(
                name: "ProviderProductId",
                table: "parking_rule_sets",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProviderProductId",
                table: "parking_budget_periods",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "admin_audit_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    TargetType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    TargetId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ContextJson = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_admin_audit_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_admin_audit_events_users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "notifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RecipientUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    VisitId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceEventId = table.Column<Guid>(type: "uuid", nullable: true),
                    Payload = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReadAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_notifications_users_RecipientUserId",
                        column: x => x.RecipientUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_notifications_visits_VisitId",
                        column: x => x.VisitId,
                        principalTable: "visits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "parking_budget_warning_states",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ParkingBudgetPeriodId = table.Column<Guid>(type: "uuid", nullable: false),
                    ThresholdPercentage = table.Column<int>(type: "integer", nullable: false),
                    NotifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_parking_budget_warning_states", x => x.Id);
                    table.ForeignKey(
                        name: "FK_parking_budget_warning_states_parking_budget_periods_Parkin~",
                        column: x => x.ParkingBudgetPeriodId,
                        principalTable: "parking_budget_periods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "parking_provider_products",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderProductId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CategoryId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CategoryName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Location = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IsAvailable = table.Column<bool>(type: "boolean", nullable: false),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    FirstSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_parking_provider_products", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "push_subscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Endpoint = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    P256dh = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Auth = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_push_subscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_push_subscriptions_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "push_deliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NotificationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeliveredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_push_deliveries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_push_deliveries_notifications_NotificationId",
                        column: x => x.NotificationId,
                        principalTable: "notifications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "provider_discrepancies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ProviderProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    VisitId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProviderParkingActionId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProviderActionId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ProviderStatus = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ProviderStartAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ProviderEndAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DetectedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastObservedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_provider_discrepancies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_provider_discrepancies_parking_provider_products_ProviderPr~",
                        column: x => x.ProviderProductId,
                        principalTable: "parking_provider_products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_provider_discrepancies_provider_parking_actions_ProviderPar~",
                        column: x => x.ProviderParkingActionId,
                        principalTable: "provider_parking_actions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_provider_discrepancies_visits_VisitId",
                        column: x => x.VisitId,
                        principalTable: "visits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_visits_ProviderProductId",
                table: "visits",
                column: "ProviderProductId");

            migrationBuilder.CreateIndex(
                name: "IX_provider_operations_ParentOperationId",
                table: "provider_operations",
                column: "ParentOperationId");

            migrationBuilder.CreateIndex(
                name: "IX_provider_operations_ParentOperationId_Type_ProviderParkingA~",
                table: "provider_operations",
                columns: new[] { "ParentOperationId", "Type", "ProviderParkingActionId" },
                unique: true,
                filter: "\"ParentOperationId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_parking_tariffs_ProviderProductId_ValidFrom",
                table: "parking_tariffs",
                columns: new[] { "ProviderProductId", "ValidFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_parking_rule_sets_ProviderProductId_ValidFrom",
                table: "parking_rule_sets",
                columns: new[] { "ProviderProductId", "ValidFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_parking_budget_periods_ProviderProductId_ValidFrom",
                table: "parking_budget_periods",
                columns: new[] { "ProviderProductId", "ValidFrom" });

            migrationBuilder.CreateIndex(
                name: "IX_admin_audit_events_ActorUserId_CreatedAt",
                table: "admin_audit_events",
                columns: new[] { "ActorUserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_admin_audit_events_CreatedAt",
                table: "admin_audit_events",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_notifications_RecipientUserId_CreatedAt",
                table: "notifications",
                columns: new[] { "RecipientUserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_notifications_RecipientUserId_ReadAt",
                table: "notifications",
                columns: new[] { "RecipientUserId", "ReadAt" });

            migrationBuilder.CreateIndex(
                name: "IX_notifications_RecipientUserId_SourceEventId",
                table: "notifications",
                columns: new[] { "RecipientUserId", "SourceEventId" },
                unique: true,
                filter: "\"SourceEventId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_notifications_VisitId",
                table: "notifications",
                column: "VisitId");

            migrationBuilder.CreateIndex(
                name: "IX_parking_budget_warning_states_ParkingBudgetPeriodId_Thresho~",
                table: "parking_budget_warning_states",
                columns: new[] { "ParkingBudgetPeriodId", "ThresholdPercentage" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_parking_provider_products_IsDefault",
                table: "parking_provider_products",
                column: "IsDefault",
                unique: true,
                filter: "\"IsDefault\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "IX_parking_provider_products_ProviderProductId",
                table: "parking_provider_products",
                column: "ProviderProductId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_provider_discrepancies_Key",
                table: "provider_discrepancies",
                column: "Key",
                unique: true,
                filter: "\"Status\" = 'Open'");

            migrationBuilder.CreateIndex(
                name: "IX_provider_discrepancies_ProviderParkingActionId",
                table: "provider_discrepancies",
                column: "ProviderParkingActionId");

            migrationBuilder.CreateIndex(
                name: "IX_provider_discrepancies_ProviderProductId",
                table: "provider_discrepancies",
                column: "ProviderProductId");

            migrationBuilder.CreateIndex(
                name: "IX_provider_discrepancies_Status_LastObservedAt",
                table: "provider_discrepancies",
                columns: new[] { "Status", "LastObservedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_provider_discrepancies_VisitId",
                table: "provider_discrepancies",
                column: "VisitId");

            migrationBuilder.CreateIndex(
                name: "IX_push_deliveries_NotificationId",
                table: "push_deliveries",
                column: "NotificationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_push_deliveries_Status_LastAttemptAt",
                table: "push_deliveries",
                columns: new[] { "Status", "LastAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_push_subscriptions_Endpoint",
                table: "push_subscriptions",
                column: "Endpoint",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_push_subscriptions_UserId",
                table: "push_subscriptions",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_parking_budget_periods_parking_provider_products_ProviderPr~",
                table: "parking_budget_periods",
                column: "ProviderProductId",
                principalTable: "parking_provider_products",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_parking_rule_sets_parking_provider_products_ProviderProduct~",
                table: "parking_rule_sets",
                column: "ProviderProductId",
                principalTable: "parking_provider_products",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_parking_tariffs_parking_provider_products_ProviderProductId",
                table: "parking_tariffs",
                column: "ProviderProductId",
                principalTable: "parking_provider_products",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_visits_parking_provider_products_ProviderProductId",
                table: "visits",
                column: "ProviderProductId",
                principalTable: "parking_provider_products",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_parking_budget_periods_parking_provider_products_ProviderPr~",
                table: "parking_budget_periods");

            migrationBuilder.DropForeignKey(
                name: "FK_parking_rule_sets_parking_provider_products_ProviderProduct~",
                table: "parking_rule_sets");

            migrationBuilder.DropForeignKey(
                name: "FK_parking_tariffs_parking_provider_products_ProviderProductId",
                table: "parking_tariffs");

            migrationBuilder.DropForeignKey(
                name: "FK_visits_parking_provider_products_ProviderProductId",
                table: "visits");

            migrationBuilder.DropTable(
                name: "admin_audit_events");

            migrationBuilder.DropTable(
                name: "parking_budget_warning_states");

            migrationBuilder.DropTable(
                name: "provider_discrepancies");

            migrationBuilder.DropTable(
                name: "push_deliveries");

            migrationBuilder.DropTable(
                name: "push_subscriptions");

            migrationBuilder.DropTable(
                name: "parking_provider_products");

            migrationBuilder.DropTable(
                name: "notifications");

            migrationBuilder.DropIndex(
                name: "IX_visits_ProviderProductId",
                table: "visits");

            migrationBuilder.DropIndex(
                name: "IX_provider_operations_ParentOperationId",
                table: "provider_operations");

            migrationBuilder.DropIndex(
                name: "IX_provider_operations_ParentOperationId_Type_ProviderParkingA~",
                table: "provider_operations");

            migrationBuilder.DropIndex(
                name: "IX_parking_tariffs_ProviderProductId_ValidFrom",
                table: "parking_tariffs");

            migrationBuilder.DropIndex(
                name: "IX_parking_rule_sets_ProviderProductId_ValidFrom",
                table: "parking_rule_sets");

            migrationBuilder.DropIndex(
                name: "IX_parking_budget_periods_ProviderProductId_ValidFrom",
                table: "parking_budget_periods");

            migrationBuilder.DropColumn(
                name: "EndReason",
                table: "visits");

            migrationBuilder.DropColumn(
                name: "ProviderLocation",
                table: "visits");

            migrationBuilder.DropColumn(
                name: "ProviderProductExternalId",
                table: "visits");

            migrationBuilder.DropColumn(
                name: "ProviderProductId",
                table: "visits");

            migrationBuilder.DropColumn(
                name: "EndReason",
                table: "visit_scheduler_work");

            migrationBuilder.DropColumn(
                name: "ProviderCostAmount",
                table: "provider_parking_actions");

            migrationBuilder.DropColumn(
                name: "ProviderLocation",
                table: "provider_parking_actions");

            migrationBuilder.DropColumn(
                name: "ProviderProductId",
                table: "provider_parking_actions");

            migrationBuilder.DropColumn(
                name: "ParentOperationId",
                table: "provider_operations");

            migrationBuilder.DropColumn(
                name: "ProviderProductId",
                table: "parking_tariffs");

            migrationBuilder.DropColumn(
                name: "BudgetWarningThresholdPercentages",
                table: "parking_system_settings");

            migrationBuilder.DropColumn(
                name: "ProviderProductId",
                table: "parking_rule_sets");

            migrationBuilder.DropColumn(
                name: "ProviderProductId",
                table: "parking_budget_periods");

            migrationBuilder.AlterColumn<TimeSpan>(
                name: "PolicyMaxPaidParkingDuration",
                table: "visits",
                type: "interval",
                nullable: false,
                defaultValue: new TimeSpan(0, 0, 0, 0, 0),
                oldClrType: typeof(TimeSpan),
                oldType: "interval",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_parking_tariffs_ValidFrom",
                table: "parking_tariffs",
                column: "ValidFrom");

            migrationBuilder.CreateIndex(
                name: "IX_parking_budget_periods_ValidFrom",
                table: "parking_budget_periods",
                column: "ValidFrom");
        }
    }
}
