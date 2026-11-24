using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SeneCare.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ServiceAreas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_served",
                table: "regions",
                type: "bit",
                nullable: false,
                // Existing zones stay open: no customer is blocked; admins close the ones not covered yet.
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_served",
                table: "communes",
                type: "bit",
                nullable: false,
                // Existing zones stay open: no customer is blocked; admins close the ones not covered yet.
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "coverage_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    region_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    commune_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    locality = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    full_name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    phone_number = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_coverage_requests", x => x.id);
                    table.ForeignKey(
                        name: "fk_coverage_requests_communes_commune_id",
                        column: x => x.commune_id,
                        principalTable: "communes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_coverage_requests_regions_region_id",
                        column: x => x.region_id,
                        principalTable: "regions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_coverage_requests_commune_id",
                table: "coverage_requests",
                column: "commune_id");

            migrationBuilder.CreateIndex(
                name: "ix_coverage_requests_region_id_created_at",
                table: "coverage_requests",
                columns: new[] { "region_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "coverage_requests");

            migrationBuilder.DropColumn(
                name: "is_served",
                table: "regions");

            migrationBuilder.DropColumn(
                name: "is_served",
                table: "communes");
        }
    }
}
