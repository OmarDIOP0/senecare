using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SeneCare.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CustomerSuspension : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "suspended_at",
                table: "customer_accounts",
                type: "datetimeoffset",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "suspended_at",
                table: "customer_accounts");
        }
    }
}
