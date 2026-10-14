using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SeneCare.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReviewPublication : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "rating_is_hidden",
                table: "service_requests",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "rating_publication_consent",
                table: "service_requests",
                type: "boolean",
                nullable: true);

            // Ratings given before: no consent was asked, so nothing is published.
            migrationBuilder.Sql("UPDATE service_requests SET rating_publication_consent = false, rating_is_hidden = false WHERE rating_score IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "rating_is_hidden",
                table: "service_requests");

            migrationBuilder.DropColumn(
                name: "rating_publication_consent",
                table: "service_requests");
        }
    }
}
