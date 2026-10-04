using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Parkeren.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EnforceParkingBudgetPeriodNonOverlap : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS btree_gist;");
            migrationBuilder.Sql(
                "ALTER TABLE parking_budget_periods " +
                "ADD CONSTRAINT CK_parking_budget_periods_ValidInterval " +
                "CHECK (\"ValidUntil\" > \"ValidFrom\");");
            migrationBuilder.Sql(
                "ALTER TABLE parking_budget_periods " +
                "ADD CONSTRAINT EX_parking_budget_periods_ProductPeriod " +
                "EXCLUDE USING gist (" +
                "(COALESCE(\"ProviderProductId\", '00000000-0000-0000-0000-000000000000'::uuid)) WITH =, " +
                "tstzrange(\"ValidFrom\", \"ValidUntil\", '[)') WITH &&);");
            migrationBuilder.Sql(
                "ALTER TABLE parking_tariffs " +
                "ADD CONSTRAINT CK_parking_tariffs_ValidInterval " +
                "CHECK (\"ValidUntil\" IS NULL OR \"ValidUntil\" > \"ValidFrom\");");
            migrationBuilder.Sql(
                "ALTER TABLE parking_tariffs " +
                "ADD CONSTRAINT EX_parking_tariffs_ProductPeriod " +
                "EXCLUDE USING gist (" +
                "(COALESCE(\"ProviderProductId\", '00000000-0000-0000-0000-000000000000'::uuid)) WITH =, " +
                "tstzrange(\"ValidFrom\", \"ValidUntil\", '[)') WITH &&);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE parking_budget_periods " +
                "DROP CONSTRAINT EX_parking_budget_periods_ProductPeriod;");
            migrationBuilder.Sql(
                "ALTER TABLE parking_budget_periods " +
                "DROP CONSTRAINT CK_parking_budget_periods_ValidInterval;");
            migrationBuilder.Sql(
                "ALTER TABLE parking_tariffs " +
                "DROP CONSTRAINT EX_parking_tariffs_ProductPeriod;");
            migrationBuilder.Sql(
                "ALTER TABLE parking_tariffs " +
                "DROP CONSTRAINT CK_parking_tariffs_ValidInterval;");
        }
    }
}
