using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Parkeren.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EnforceParkingRuleSetPeriodNonOverlap : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE parking_rule_sets " +
                "ADD CONSTRAINT CK_parking_rule_sets_ValidInterval " +
                "CHECK (\"ValidUntil\" IS NULL OR \"ValidUntil\" > \"ValidFrom\");");
            migrationBuilder.Sql(
                "ALTER TABLE parking_rule_sets " +
                "ADD CONSTRAINT EX_parking_rule_sets_ProductPeriod " +
                "EXCLUDE USING gist (" +
                "(COALESCE(\"ProviderProductId\", '00000000-0000-0000-0000-000000000000'::uuid)) WITH =, " +
                "tstzrange(\"ValidFrom\", \"ValidUntil\", '[)') WITH &&);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE parking_rule_sets " +
                "DROP CONSTRAINT EX_parking_rule_sets_ProductPeriod;");
            migrationBuilder.Sql(
                "ALTER TABLE parking_rule_sets " +
                "DROP CONSTRAINT CK_parking_rule_sets_ValidInterval;");
        }
    }
}
