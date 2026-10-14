using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SeneCare.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NeighborhoodSuggestion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "suggested_at",
                table: "neighborhoods",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "suggested_by_user_id",
                table: "neighborhoods",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_neighborhoods_suggested_at",
                table: "neighborhoods",
                column: "suggested_at",
                filter: "suggested_at IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_neighborhoods_suggested_at",
                table: "neighborhoods");

            migrationBuilder.DropColumn(
                name: "suggested_at",
                table: "neighborhoods");

            migrationBuilder.DropColumn(
                name: "suggested_by_user_id",
                table: "neighborhoods");
        }
    }
}
