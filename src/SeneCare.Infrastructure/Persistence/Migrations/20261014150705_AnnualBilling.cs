using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SeneCare.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AnnualBilling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "free_months_per_period",
                table: "subscriptions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "annual_free_months",
                table: "plans",
                type: "integer",
                nullable: false,
                defaultValue: 2);

            migrationBuilder.AddColumn<string>(
                name: "chosen_plan_code",
                table: "customer_accounts",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "prefers_annual",
                table: "customer_accounts",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "free_months_per_period",
                table: "subscriptions");

            migrationBuilder.DropColumn(
                name: "annual_free_months",
                table: "plans");

            migrationBuilder.DropColumn(
                name: "chosen_plan_code",
                table: "customer_accounts");

            migrationBuilder.DropColumn(
                name: "prefers_annual",
                table: "customer_accounts");
        }
    }
}
