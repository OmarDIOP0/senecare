using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SeneCare.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Referrals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "referral_code",
                table: "users",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "referred_by_user_id",
                table: "users",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_referral_code",
                table: "users",
                column: "referral_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_referred_by_user_id",
                table: "users",
                column: "referred_by_user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_users_referral_code",
                table: "users");

            migrationBuilder.DropIndex(
                name: "ix_users_referred_by_user_id",
                table: "users");

            migrationBuilder.DropColumn(
                name: "referral_code",
                table: "users");

            migrationBuilder.DropColumn(
                name: "referred_by_user_id",
                table: "users");
        }
    }
}
