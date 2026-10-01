using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Parkeren.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "default_parking_policy",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MaxPaidParkingDuration = table.Column<TimeSpan>(type: "interval", nullable: true),
                    MaxVisitElapsedDuration = table.Column<TimeSpan>(type: "interval", nullable: true),
                    AllowVisitExtension = table.Column<bool>(type: "boolean", nullable: false),
                    AllowOpenEndedVisits = table.Column<bool>(type: "boolean", nullable: false),
                    MaxConcurrentVisits = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_default_parking_policy", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "notification_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    AggregateId = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_events", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "parking_budget_periods",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ValidFrom = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ValidUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    MaximumPaidDuration = table.Column<TimeSpan>(type: "interval", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_parking_budget_periods", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "parking_rule_sets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ValidFrom = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ValidUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    MaxProviderActionDuration = table.Column<TimeSpan>(type: "interval", nullable: false),
                    Continuation = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    PublicHolidaysAreFree = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_parking_rule_sets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "parking_system_settings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MaxConcurrentVisits = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_parking_system_settings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "parking_tariffs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ValidFrom = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ValidUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Rate = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Unit = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_parking_tariffs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "parking_zones",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ProviderLocation = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ValidFrom = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ValidUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_parking_zones", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Username = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    NormalizedUsername = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PinHash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "vehicles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LicensePlate = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    NormalizedLicensePlate = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_vehicles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "paid_windows",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ParkingRuleSetId = table.Column<Guid>(type: "uuid", nullable: false),
                    Day = table.Column<int>(type: "integer", nullable: false),
                    Start = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    End = table.Column<TimeOnly>(type: "time without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_paid_windows", x => x.Id);
                    table.ForeignKey(
                        name: "FK_paid_windows_parking_rule_sets_ParkingRuleSetId",
                        column: x => x.ParkingRuleSetId,
                        principalTable: "parking_rule_sets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "parking_calendar_exceptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ParkingRuleSetId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    IsPaid = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_parking_calendar_exceptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_parking_calendar_exceptions_parking_rule_sets_ParkingRuleSe~",
                        column: x => x.ParkingRuleSetId,
                        principalTable: "parking_rule_sets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_policy_overrides",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    MaxPaidParkingDurationMode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    MaxPaidParkingDuration = table.Column<TimeSpan>(type: "interval", nullable: true),
                    MaxVisitElapsedDurationMode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    MaxVisitElapsedDuration = table.Column<TimeSpan>(type: "interval", nullable: true),
                    AllowVisitExtension = table.Column<bool>(type: "boolean", nullable: true),
                    AllowOpenEndedVisits = table.Column<bool>(type: "boolean", nullable: true),
                    MaxConcurrentVisits = table.Column<int>(type: "integer", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_policy_overrides", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_user_policy_overrides_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "user_sessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_sessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_user_sessions_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "user_vehicles",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    VehicleId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_vehicles", x => new { x.UserId, x.VehicleId });
                    table.ForeignKey(
                        name: "FK_user_vehicles_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_user_vehicles_vehicles_VehicleId",
                        column: x => x.VehicleId,
                        principalTable: "vehicles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "visits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StartOperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    VehicleId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParkingZoneId = table.Column<Guid>(type: "uuid", nullable: true),
                    StartedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DesiredEndAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ActualEndAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Health = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    PolicyMaxPaidParkingDuration = table.Column<TimeSpan>(type: "interval", nullable: false),
                    PolicyMaxVisitElapsedDuration = table.Column<TimeSpan>(type: "interval", nullable: true),
                    PolicyAllowVisitExtension = table.Column<bool>(type: "boolean", nullable: false),
                    PolicyAllowOpenEndedVisits = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_visits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_visits_parking_zones_ParkingZoneId",
                        column: x => x.ParkingZoneId,
                        principalTable: "parking_zones",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_visits_users_StartedByUserId",
                        column: x => x.StartedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_visits_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_visits_vehicles_VehicleId",
                        column: x => x.VehicleId,
                        principalTable: "vehicles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "provider_parking_actions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    VisitId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProviderActionId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    PlannedStartAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PlannedEndAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ActualStartAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ActualEndAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ProviderStatus = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    State = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Health = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_provider_parking_actions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_provider_parking_actions_visits_VisitId",
                        column: x => x.VisitId,
                        principalTable: "visits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

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
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
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

            migrationBuilder.CreateTable(
                name: "provider_operations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    VisitId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProviderParkingActionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    LastErrorCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AttemptStartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RequestedEndAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_provider_operations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_provider_operations_provider_parking_actions_ProviderParkin~",
                        column: x => x.ProviderParkingActionId,
                        principalTable: "provider_parking_actions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_provider_operations_visits_VisitId",
                        column: x => x.VisitId,
                        principalTable: "visits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_notification_events_Type_AggregateId",
                table: "notification_events",
                columns: new[] { "Type", "AggregateId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_paid_windows_ParkingRuleSetId_Day_Start_End",
                table: "paid_windows",
                columns: new[] { "ParkingRuleSetId", "Day", "Start", "End" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_parking_budget_periods_ValidFrom",
                table: "parking_budget_periods",
                column: "ValidFrom");

            migrationBuilder.CreateIndex(
                name: "IX_parking_calendar_exceptions_ParkingRuleSetId_Date",
                table: "parking_calendar_exceptions",
                columns: new[] { "ParkingRuleSetId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_parking_tariffs_ValidFrom",
                table: "parking_tariffs",
                column: "ValidFrom");

            migrationBuilder.CreateIndex(
                name: "IX_provider_operations_OperationId",
                table: "provider_operations",
                column: "OperationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_provider_operations_ProviderParkingActionId",
                table: "provider_operations",
                column: "ProviderParkingActionId");

            migrationBuilder.CreateIndex(
                name: "IX_provider_operations_VisitId",
                table: "provider_operations",
                column: "VisitId");

            migrationBuilder.CreateIndex(
                name: "IX_provider_parking_actions_ProviderActionId",
                table: "provider_parking_actions",
                column: "ProviderActionId",
                unique: true,
                filter: "\"ProviderActionId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_provider_parking_actions_VisitId",
                table: "provider_parking_actions",
                column: "VisitId");

            migrationBuilder.CreateIndex(
                name: "IX_user_sessions_TokenHash",
                table: "user_sessions",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_sessions_UserId_ExpiresAt",
                table: "user_sessions",
                columns: new[] { "UserId", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_user_vehicles_VehicleId",
                table: "user_vehicles",
                column: "VehicleId");

            migrationBuilder.CreateIndex(
                name: "IX_users_NormalizedUsername",
                table: "users",
                column: "NormalizedUsername",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_vehicles_NormalizedLicensePlate",
                table: "vehicles",
                column: "NormalizedLicensePlate",
                unique: true);

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

            migrationBuilder.CreateIndex(
                name: "IX_visit_scheduler_work_Status_DueAt",
                table: "visit_scheduler_work",
                columns: new[] { "Status", "DueAt" });

            migrationBuilder.CreateIndex(
                name: "IX_visit_scheduler_work_VisitId_Type_DueAt",
                table: "visit_scheduler_work",
                columns: new[] { "VisitId", "Type", "DueAt" },
                unique: true,
                filter: "\"Status\" IN ('Pending', 'Claimed')");

            migrationBuilder.CreateIndex(
                name: "IX_parking_zones_ProviderLocation",
                table: "parking_zones",
                column: "ProviderLocation");

            migrationBuilder.CreateIndex(
                name: "IX_parking_zones_ValidFrom",
                table: "parking_zones",
                column: "ValidFrom");

            migrationBuilder.CreateIndex(
                name: "IX_visits_ParkingZoneId",
                table: "visits",
                column: "ParkingZoneId");

            migrationBuilder.CreateIndex(
                name: "IX_visits_StartedByUserId",
                table: "visits",
                column: "StartedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_visits_StartOperationId",
                table: "visits",
                column: "StartOperationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_visits_Status_StartAt",
                table: "visits",
                columns: new[] { "Status", "StartAt" });

            migrationBuilder.CreateIndex(
                name: "IX_visits_UserId",
                table: "visits",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_visits_VehicleId",
                table: "visits",
                column: "VehicleId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "default_parking_policy");

            migrationBuilder.DropTable(
                name: "notification_events");

            migrationBuilder.DropTable(
                name: "paid_windows");

            migrationBuilder.DropTable(
                name: "parking_budget_periods");

            migrationBuilder.DropTable(
                name: "parking_calendar_exceptions");

            migrationBuilder.DropTable(
                name: "parking_system_settings");

            migrationBuilder.DropTable(
                name: "parking_tariffs");

            migrationBuilder.DropTable(
                name: "provider_operations");

            migrationBuilder.DropTable(
                name: "user_policy_overrides");

            migrationBuilder.DropTable(
                name: "user_sessions");

            migrationBuilder.DropTable(
                name: "user_vehicles");

            migrationBuilder.DropTable(
                name: "visit_end_time_changes");

            migrationBuilder.DropTable(
                name: "visit_scheduler_work");

            migrationBuilder.DropTable(
                name: "parking_rule_sets");

            migrationBuilder.DropTable(
                name: "provider_parking_actions");

            migrationBuilder.DropTable(
                name: "visits");

            migrationBuilder.DropTable(
                name: "parking_zones");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "vehicles");
        }
    }
}
