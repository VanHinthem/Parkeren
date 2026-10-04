using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Parkeren.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PersistProviderBalanceSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "LastSuccessfulBalance",
                table: "parking_provider_products",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastSuccessfulBalanceAt",
                table: "parking_provider_products",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastSuccessfulBalanceUnit",
                table: "parking_provider_products",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastSuccessfulBalance",
                table: "parking_provider_products");

            migrationBuilder.DropColumn(
                name: "LastSuccessfulBalanceAt",
                table: "parking_provider_products");

            migrationBuilder.DropColumn(
                name: "LastSuccessfulBalanceUnit",
                table: "parking_provider_products");
        }
    }
}
