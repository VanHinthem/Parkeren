using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Parkeren.Infrastructure.Persistence.Migrations;

[DbContext(typeof(ParkerenDbContext))]
[Migration("20260928210500_AdminCapacityPolicy")]
public sealed class AdminCapacityPolicy : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Backfill only missing admin overrides. An existing null override is intentional.
        migrationBuilder.Sql("""
            INSERT INTO user_policy_overrides ("UserId", "MaxConcurrentVisits", "UpdatedAt")
            SELECT u."Id", s."MaxConcurrentVisits", now()
            FROM users AS u CROSS JOIN parking_system_settings AS s
            WHERE u."Role" = 'Admin'
              AND NOT EXISTS (
                  SELECT 1 FROM user_policy_overrides AS p WHERE p."UserId" = u."Id"
              );
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // User policy edits made after the backfill must not be deleted on rollback.
    }
}
