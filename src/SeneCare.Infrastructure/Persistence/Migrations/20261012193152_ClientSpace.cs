using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SeneCare.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ClientSpace : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "client_reference",
                table: "service_requests",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "account_id",
                table: "media_files",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_service_requests_account_id_client_reference",
                table: "service_requests",
                columns: new[] { "account_id", "client_reference" },
                unique: true,
                filter: "client_reference IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_media_files_account_id",
                table: "media_files",
                column: "account_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_service_requests_account_id_client_reference",
                table: "service_requests");

            migrationBuilder.DropIndex(
                name: "ix_media_files_account_id",
                table: "media_files");

            migrationBuilder.DropColumn(
                name: "client_reference",
                table: "service_requests");

            migrationBuilder.DropColumn(
                name: "account_id",
                table: "media_files");
        }
    }
}
