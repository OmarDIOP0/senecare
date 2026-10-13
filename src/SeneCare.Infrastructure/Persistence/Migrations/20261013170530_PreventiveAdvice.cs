using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SeneCare.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PreventiveAdvice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "preventive_advices",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    body = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: false),
                    domain = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    months = table.Column<int[]>(type: "integer[]", nullable: false),
                    is_published = table.Column<bool>(type: "boolean", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_preventive_advices", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_preventive_advices_is_published",
                table: "preventive_advices",
                column: "is_published");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "preventive_advices");
        }
    }
}
