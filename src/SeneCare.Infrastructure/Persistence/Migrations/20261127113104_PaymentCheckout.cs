using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SeneCare.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PaymentCheckout : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "checkout_alt_link",
                table: "payments",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "checkout_expires_at",
                table: "payments",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "checkout_link",
                table: "payments",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "checkout_qr_code",
                table: "payments",
                type: "nvarchar(max)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "checkout_alt_link",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "checkout_expires_at",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "checkout_link",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "checkout_qr_code",
                table: "payments");
        }
    }
}
