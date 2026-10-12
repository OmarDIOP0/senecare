using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SeneCare.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OwnedCollectionKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "pk_user_roles",
                table: "user_roles");

            migrationBuilder.DropPrimaryKey(
                name: "pk_service_request_attachments",
                table: "service_request_attachments");

            migrationBuilder.DropPrimaryKey(
                name: "pk_property_photos",
                table: "property_photos");

            migrationBuilder.DropPrimaryKey(
                name: "pk_plan_sla_policies",
                table: "plan_sla_policies");

            migrationBuilder.DropPrimaryKey(
                name: "pk_intervention_photos",
                table: "intervention_photos");

            migrationBuilder.DropPrimaryKey(
                name: "pk_greening_operation_photos",
                table: "greening_operation_photos");

            migrationBuilder.DropPrimaryKey(
                name: "PK_diagnostic_control_point_photos",
                table: "diagnostic_control_point_photos");

            migrationBuilder.AddColumn<int>(
                name: "id",
                table: "user_roles",
                type: "integer",
                nullable: false,
                defaultValue: 0)
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.AddColumn<int>(
                name: "id",
                table: "service_request_attachments",
                type: "integer",
                nullable: false,
                defaultValue: 0)
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.AddColumn<int>(
                name: "id",
                table: "property_photos",
                type: "integer",
                nullable: false,
                defaultValue: 0)
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.AddColumn<int>(
                name: "id",
                table: "plan_sla_policies",
                type: "integer",
                nullable: false,
                defaultValue: 0)
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.AddColumn<int>(
                name: "id",
                table: "intervention_photos",
                type: "integer",
                nullable: false,
                defaultValue: 0)
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.AddColumn<int>(
                name: "id",
                table: "greening_operation_photos",
                type: "integer",
                nullable: false,
                defaultValue: 0)
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.AddColumn<int>(
                name: "id",
                table: "diagnostic_control_point_photos",
                type: "integer",
                nullable: false,
                defaultValue: 0)
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.AddPrimaryKey(
                name: "pk_user_roles",
                table: "user_roles",
                columns: new[] { "user_id", "id" });

            migrationBuilder.AddPrimaryKey(
                name: "pk_service_request_attachments",
                table: "service_request_attachments",
                columns: new[] { "service_request_id", "id" });

            migrationBuilder.AddPrimaryKey(
                name: "pk_property_photos",
                table: "property_photos",
                columns: new[] { "property_id", "id" });

            migrationBuilder.AddPrimaryKey(
                name: "pk_plan_sla_policies",
                table: "plan_sla_policies",
                columns: new[] { "plan_id", "id" });

            migrationBuilder.AddPrimaryKey(
                name: "pk_intervention_photos",
                table: "intervention_photos",
                columns: new[] { "intervention_id", "id" });

            migrationBuilder.AddPrimaryKey(
                name: "pk_greening_operation_photos",
                table: "greening_operation_photos",
                columns: new[] { "operation_id", "id" });

            migrationBuilder.AddPrimaryKey(
                name: "pk_diagnostic_control_point_photos",
                table: "diagnostic_control_point_photos",
                columns: new[] { "control_point_id", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_user_roles_user_id_role",
                table: "user_roles",
                columns: new[] { "user_id", "role" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_service_request_attachments_service_request_id_media_file_id",
                table: "service_request_attachments",
                columns: new[] { "service_request_id", "media_file_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_property_photos_property_id_media_file_id",
                table: "property_photos",
                columns: new[] { "property_id", "media_file_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_plan_sla_policies_plan_id_urgency",
                table: "plan_sla_policies",
                columns: new[] { "plan_id", "urgency" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_intervention_photos_intervention_id_media_file_id",
                table: "intervention_photos",
                columns: new[] { "intervention_id", "media_file_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_greening_operation_photos_operation_id_media_file_id",
                table: "greening_operation_photos",
                columns: new[] { "operation_id", "media_file_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_diagnostic_control_point_photos_control_point_id_media_file",
                table: "diagnostic_control_point_photos",
                columns: new[] { "control_point_id", "media_file_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "pk_user_roles",
                table: "user_roles");

            migrationBuilder.DropIndex(
                name: "ix_user_roles_user_id_role",
                table: "user_roles");

            migrationBuilder.DropPrimaryKey(
                name: "pk_service_request_attachments",
                table: "service_request_attachments");

            migrationBuilder.DropIndex(
                name: "ix_service_request_attachments_service_request_id_media_file_id",
                table: "service_request_attachments");

            migrationBuilder.DropPrimaryKey(
                name: "pk_property_photos",
                table: "property_photos");

            migrationBuilder.DropIndex(
                name: "ix_property_photos_property_id_media_file_id",
                table: "property_photos");

            migrationBuilder.DropPrimaryKey(
                name: "pk_plan_sla_policies",
                table: "plan_sla_policies");

            migrationBuilder.DropIndex(
                name: "ix_plan_sla_policies_plan_id_urgency",
                table: "plan_sla_policies");

            migrationBuilder.DropPrimaryKey(
                name: "pk_intervention_photos",
                table: "intervention_photos");

            migrationBuilder.DropIndex(
                name: "ix_intervention_photos_intervention_id_media_file_id",
                table: "intervention_photos");

            migrationBuilder.DropPrimaryKey(
                name: "pk_greening_operation_photos",
                table: "greening_operation_photos");

            migrationBuilder.DropIndex(
                name: "ix_greening_operation_photos_operation_id_media_file_id",
                table: "greening_operation_photos");

            migrationBuilder.DropPrimaryKey(
                name: "pk_diagnostic_control_point_photos",
                table: "diagnostic_control_point_photos");

            migrationBuilder.DropIndex(
                name: "ix_diagnostic_control_point_photos_control_point_id_media_file",
                table: "diagnostic_control_point_photos");

            migrationBuilder.DropColumn(
                name: "id",
                table: "user_roles");

            migrationBuilder.DropColumn(
                name: "id",
                table: "service_request_attachments");

            migrationBuilder.DropColumn(
                name: "id",
                table: "property_photos");

            migrationBuilder.DropColumn(
                name: "id",
                table: "plan_sla_policies");

            migrationBuilder.DropColumn(
                name: "id",
                table: "intervention_photos");

            migrationBuilder.DropColumn(
                name: "id",
                table: "greening_operation_photos");

            migrationBuilder.DropColumn(
                name: "id",
                table: "diagnostic_control_point_photos");

            migrationBuilder.AddPrimaryKey(
                name: "pk_user_roles",
                table: "user_roles",
                columns: new[] { "user_id", "role" });

            migrationBuilder.AddPrimaryKey(
                name: "pk_service_request_attachments",
                table: "service_request_attachments",
                columns: new[] { "service_request_id", "media_file_id" });

            migrationBuilder.AddPrimaryKey(
                name: "pk_property_photos",
                table: "property_photos",
                columns: new[] { "property_id", "media_file_id" });

            migrationBuilder.AddPrimaryKey(
                name: "pk_plan_sla_policies",
                table: "plan_sla_policies",
                columns: new[] { "plan_id", "urgency" });

            migrationBuilder.AddPrimaryKey(
                name: "pk_intervention_photos",
                table: "intervention_photos",
                columns: new[] { "intervention_id", "media_file_id" });

            migrationBuilder.AddPrimaryKey(
                name: "pk_greening_operation_photos",
                table: "greening_operation_photos",
                columns: new[] { "operation_id", "media_file_id" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_diagnostic_control_point_photos",
                table: "diagnostic_control_point_photos",
                columns: new[] { "control_point_id", "media_file_id" });
        }
    }
}
