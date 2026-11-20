using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SeneCare.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSqlServer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "invoice_number_seq");

            migrationBuilder.CreateSequence(
                name: "service_request_number_seq");

            migrationBuilder.CreateTable(
                name: "app_settings",
                columns: table => new
                {
                    key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    value = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_app_settings", x => x.key);
                });

            migrationBuilder.CreateTable(
                name: "audit_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    action = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    entity_type = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    entity_id = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    is_sensitive_data = table.Column<bool>(type: "bit", nullable: false),
                    ip_address = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    details = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "customer_accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    kind = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    display_name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    ninea = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    requires_request_approval = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    chosen_plan_code = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    prefers_annual = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_customer_accounts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "data_protection_keys",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    friendly_name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    xml = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_data_protection_keys", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "media_files",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    storage_key = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    content_type = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    kind = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    uploaded_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    account_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    uploaded_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_media_files", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    channel = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    template_key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    payload_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    is_critical = table.Column<bool>(type: "bit", nullable: false),
                    status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    attempts = table.Column<int>(type: "int", nullable: false),
                    last_error = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    read_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notifications", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "otp_challenges",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    phone_number = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    code_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    channel = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ip_address = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    attempts = table.Column<int>(type: "int", nullable: false),
                    consumed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_otp_challenges", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "plans",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    code = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    name = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    segment = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    registration_fee = table.Column<long>(type: "bigint", nullable: false),
                    monthly_price = table.Column<long>(type: "bigint", nullable: false),
                    billing_period_months = table.Column<int>(type: "int", nullable: false),
                    diagnostic_required = table.Column<bool>(type: "bit", nullable: false),
                    included_visits_per_period = table.Column<int>(type: "int", nullable: true),
                    priority = table.Column<int>(type: "int", nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    included_domains = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    annual_free_months = table.Column<int>(type: "int", nullable: false, defaultValue: 2)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plans", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "preventive_advices",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    title = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    body = table.Column<string>(type: "nvarchar(600)", maxLength: 600, nullable: false),
                    domain = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    months = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    is_published = table.Column<bool>(type: "bit", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_preventive_advices", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "properties",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    account_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    kind = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    neighborhood_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    latitude = table.Column<double>(type: "float", nullable: true),
                    longitude = table.Column<double>(type: "float", nullable: true),
                    landmark = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    address_details = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_properties", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "regions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    slug = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_regions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    phone_number = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    full_name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    language = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    completed_tours = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    anonymized_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    referral_code = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: true),
                    referred_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "account_invitations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    account_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    phone_number = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    role = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    token_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    invited_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    accepted_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    accepted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    property_scope = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_account_invitations", x => x.id);
                    table.ForeignKey(
                        name: "fk_account_invitations_customer_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "customer_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "account_members",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    role = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    joined_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    property_scope = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    account_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_account_members", x => x.id);
                    table.ForeignKey(
                        name: "fk_account_members_customer_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "customer_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "plan_sla_policies",
                columns: table => new
                {
                    plan_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    urgency = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    max_delay = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plan_sla_policies", x => new { x.plan_id, x.id });
                    table.ForeignKey(
                        name: "fk_plan_sla_policies_plans_plan_id",
                        column: x => x.plan_id,
                        principalTable: "plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "subscriptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    account_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    plan_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    segment = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    registration_fee = table.Column<long>(type: "bigint", nullable: false),
                    monthly_price = table.Column<long>(type: "bigint", nullable: false),
                    billing_period_months = table.Column<int>(type: "int", nullable: false),
                    free_months_per_period = table.Column<int>(type: "int", nullable: false),
                    diagnostic_required = table.Column<bool>(type: "bit", nullable: false),
                    current_period_start = table.Column<DateOnly>(type: "date", nullable: true),
                    current_period_end = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    activated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    covered_property_ids = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_subscriptions", x => x.id);
                    table.ForeignKey(
                        name: "fk_subscriptions_customer_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "customer_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_subscriptions_plans_plan_id",
                        column: x => x.plan_id,
                        principalTable: "plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "property_photos",
                columns: table => new
                {
                    property_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    media_file_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    kind = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_property_photos", x => new { x.property_id, x.id });
                    table.ForeignKey(
                        name: "fk_property_photos_properties_property_id",
                        column: x => x.property_id,
                        principalTable: "properties",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "departments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    region_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    slug = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_departments", x => x.id);
                    table.ForeignKey(
                        name: "fk_departments_regions_region_id",
                        column: x => x.region_id,
                        principalTable: "regions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "identity_users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    must_change_password = table.Column<bool>(type: "bit", nullable: false),
                    user_name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    normalized_user_name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    normalized_email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    email_confirmed = table.Column<bool>(type: "bit", nullable: false),
                    password_hash = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    security_stamp = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    concurrency_stamp = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    phone_number = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    phone_number_confirmed = table.Column<bool>(type: "bit", nullable: false),
                    two_factor_enabled = table.Column<bool>(type: "bit", nullable: false),
                    lockout_end = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    lockout_enabled = table.Column<bool>(type: "bit", nullable: false),
                    access_failed_count = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_identity_users", x => x.id);
                    table.ForeignKey(
                        name: "fk_identity_users_users_id",
                        column: x => x.id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "refresh_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    family_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    token_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    replaced_by_token_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    created_by_ip = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_refresh_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_refresh_tokens_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "service_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    number = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    account_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    property_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    reported_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    client_reference = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    domain = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    urgency = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    submitted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    sla_deadline = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    responded_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    warranty_intervention_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    rating_score = table.Column<int>(type: "int", nullable: true),
                    rating_comment = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    rating_given_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    rating_author_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    rating_publication_consent = table.Column<bool>(type: "bit", nullable: true),
                    rating_is_hidden = table.Column<bool>(type: "bit", nullable: true),
                    dispute_reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_service_requests", x => x.id);
                    table.ForeignKey(
                        name: "fk_service_requests_customer_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "customer_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_service_requests_properties_property_id",
                        column: x => x.property_id,
                        principalTable: "properties",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_service_requests_users_reported_by_user_id",
                        column: x => x.reported_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "technicians",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    is_certified = table.Column<bool>(type: "bit", nullable: false),
                    rating_count = table.Column<int>(type: "int", nullable: false),
                    rating_sum = table.Column<int>(type: "int", nullable: false),
                    skills = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    commune_ids = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_technicians", x => x.id);
                    table.ForeignKey(
                        name: "fk_technicians_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "user_consents",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    type = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    policy_version = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    given_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    withdrawn_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_consents", x => new { x.user_id, x.id });
                    table.ForeignKey(
                        name: "fk_user_consents_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_roles",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    role = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    granted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_roles", x => new { x.user_id, x.id });
                    table.ForeignKey(
                        name: "fk_user_roles_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "diagnostics",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    account_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    subscription_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    period_start = table.Column<DateOnly>(type: "date", nullable: false),
                    period_end = table.Column<DateOnly>(type: "date", nullable: false),
                    scheduled_for = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    technician_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    submitted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    recommendations = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    report_media_file_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    validated_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    validated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_diagnostics", x => x.id);
                    table.ForeignKey(
                        name: "fk_diagnostics_subscriptions_subscription_id",
                        column: x => x.subscription_id,
                        principalTable: "subscriptions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "invoices",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    number = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    account_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    subscription_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    period_start = table.Column<DateOnly>(type: "date", nullable: true),
                    period_end = table.Column<DateOnly>(type: "date", nullable: true),
                    due_date = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    issued_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    paid_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    amount_paid = table.Column<long>(type: "bigint", nullable: false),
                    applied_payment_ids = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invoices", x => x.id);
                    table.ForeignKey(
                        name: "fk_invoices_customer_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "customer_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_invoices_subscriptions_subscription_id",
                        column: x => x.subscription_id,
                        principalTable: "subscriptions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "communes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    department_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    slug = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_communes", x => x.id);
                    table.ForeignKey(
                        name: "fk_communes_departments_department_id",
                        column: x => x.department_id,
                        principalTable: "departments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "identity_user_claims",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    claim_type = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    claim_value = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_identity_user_claims", x => x.id);
                    table.ForeignKey(
                        name: "fk_identity_user_claims_identity_users_user_id",
                        column: x => x.user_id,
                        principalTable: "identity_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "identity_user_logins",
                columns: table => new
                {
                    login_provider = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    provider_key = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    provider_display_name = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_identity_user_logins", x => new { x.login_provider, x.provider_key });
                    table.ForeignKey(
                        name: "fk_identity_user_logins_identity_users_user_id",
                        column: x => x.user_id,
                        principalTable: "identity_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "identity_user_tokens",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    login_provider = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    name = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    value = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_identity_user_tokens", x => new { x.user_id, x.login_provider, x.name });
                    table.ForeignKey(
                        name: "fk_identity_user_tokens_identity_users_user_id",
                        column: x => x.user_id,
                        principalTable: "identity_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "appointments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    chosen_start = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    chosen_end = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    proposed_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    proposed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    confirmed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    service_request_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_appointments", x => x.id);
                    table.ForeignKey(
                        name: "fk_appointments_service_requests_service_request_id",
                        column: x => x.service_request_id,
                        principalTable: "service_requests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "interventions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    request_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    property_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    domain = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    lead_technician_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    customer_signature_media_file_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    signed_by_name = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    no_signature_reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    report = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    report_media_file_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    warranty_ends_on = table.Column<DateOnly>(type: "date", nullable: true),
                    team_technician_ids = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_interventions", x => x.id);
                    table.ForeignKey(
                        name: "fk_interventions_service_requests_request_id",
                        column: x => x.request_id,
                        principalTable: "service_requests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "service_request_attachments",
                columns: table => new
                {
                    service_request_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    media_file_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    kind = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_service_request_attachments", x => new { x.service_request_id, x.id });
                    table.ForeignKey(
                        name: "fk_service_request_attachments_service_requests_service_request_id",
                        column: x => x.service_request_id,
                        principalTable: "service_requests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "service_request_history",
                columns: table => new
                {
                    service_request_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    from = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    to = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    comment = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_service_request_history", x => new { x.service_request_id, x.id });
                    table.ForeignKey(
                        name: "fk_service_request_history_service_requests_service_request_id",
                        column: x => x.service_request_id,
                        principalTable: "service_requests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "service_request_preferred_slots",
                columns: table => new
                {
                    service_request_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    start = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    end = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_service_request_preferred_slots", x => new { x.service_request_id, x.id });
                    table.ForeignKey(
                        name: "fk_service_request_preferred_slots_service_requests_service_request_id",
                        column: x => x.service_request_id,
                        principalTable: "service_requests",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "technician_unavailabilities",
                columns: table => new
                {
                    technician_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    slot_start = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    slot_end = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    reason = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_technician_unavailabilities", x => new { x.technician_id, x.id });
                    table.ForeignKey(
                        name: "fk_technician_unavailabilities_technicians_technician_id",
                        column: x => x.technician_id,
                        principalTable: "technicians",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "technician_weekly_availability",
                columns: table => new
                {
                    technician_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    day = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    start = table.Column<TimeOnly>(type: "time", nullable: false),
                    end = table.Column<TimeOnly>(type: "time", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_technician_weekly_availability", x => new { x.technician_id, x.id });
                    table.ForeignKey(
                        name: "fk_technician_weekly_availability_technicians_technician_id",
                        column: x => x.technician_id,
                        principalTable: "technicians",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "diagnostic_control_points",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    property_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    domain = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    label = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    result = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    comment = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    converted_request_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    diagnostic_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_diagnostic_control_points", x => x.id);
                    table.ForeignKey(
                        name: "fk_diagnostic_control_points_diagnostics_diagnostic_id",
                        column: x => x.diagnostic_id,
                        principalTable: "diagnostics",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "invoice_lines",
                columns: table => new
                {
                    invoice_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    kind = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    quantity = table.Column<int>(type: "int", nullable: false),
                    unit_price = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invoice_lines", x => new { x.invoice_id, x.id });
                    table.ForeignKey(
                        name: "fk_invoice_lines_invoices_invoice_id",
                        column: x => x.invoice_id,
                        principalTable: "invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    invoice_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    amount = table.Column<long>(type: "bigint", nullable: false),
                    channel = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    provider_reference = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    recorded_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    declared_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    customer_reference = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payments", x => x.id);
                    table.ForeignKey(
                        name: "fk_payments_invoices_invoice_id",
                        column: x => x.invoice_id,
                        principalTable: "invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "neighborhoods",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    commune_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    suggested_by_user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    suggested_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    slug = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_neighborhoods", x => x.id);
                    table.ForeignKey(
                        name: "fk_neighborhoods_communes_commune_id",
                        column: x => x.commune_id,
                        principalTable: "communes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "appointment_proposed_slots",
                columns: table => new
                {
                    appointment_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    start = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    end = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_appointment_proposed_slots", x => new { x.appointment_id, x.id });
                    table.ForeignKey(
                        name: "fk_appointment_proposed_slots_appointments_appointment_id",
                        column: x => x.appointment_id,
                        principalTable: "appointments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "intervention_checklist_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    label = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    is_done = table.Column<bool>(type: "bit", nullable: false),
                    comment = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    intervention_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_intervention_checklist_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_intervention_checklist_items_interventions_intervention_id",
                        column: x => x.intervention_id,
                        principalTable: "interventions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "intervention_parts",
                columns: table => new
                {
                    intervention_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    name = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    quantity = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    unit = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    unit_cost = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_intervention_parts", x => new { x.intervention_id, x.id });
                    table.ForeignKey(
                        name: "fk_intervention_parts_interventions_intervention_id",
                        column: x => x.intervention_id,
                        principalTable: "interventions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "intervention_photos",
                columns: table => new
                {
                    intervention_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    media_file_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    kind = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_intervention_photos", x => new { x.intervention_id, x.id });
                    table.ForeignKey(
                        name: "fk_intervention_photos_interventions_intervention_id",
                        column: x => x.intervention_id,
                        principalTable: "interventions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "diagnostic_control_point_photos",
                columns: table => new
                {
                    control_point_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    media_file_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    kind = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_diagnostic_control_point_photos", x => new { x.control_point_id, x.id });
                    table.ForeignKey(
                        name: "fk_diagnostic_control_point_photos_diagnostic_control_points_control_point_id",
                        column: x => x.control_point_id,
                        principalTable: "diagnostic_control_points",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "neighborhood_programs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    neighborhood_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    member_count = table.Column<int>(type: "int", nullable: false),
                    threshold = table.Column<int>(type: "int", nullable: false),
                    status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    activated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_neighborhood_programs", x => x.id);
                    table.ForeignKey(
                        name: "fk_neighborhood_programs_neighborhoods_neighborhood_id",
                        column: x => x.neighborhood_id,
                        principalTable: "neighborhoods",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "greening_operations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    trees_planted = table.Column<int>(type: "int", nullable: false),
                    description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    is_published = table.Column<bool>(type: "bit", nullable: false),
                    program_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_greening_operations", x => x.id);
                    table.ForeignKey(
                        name: "fk_greening_operations_neighborhood_programs_program_id",
                        column: x => x.program_id,
                        principalTable: "neighborhood_programs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "greening_operation_photos",
                columns: table => new
                {
                    operation_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    media_file_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    kind = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_greening_operation_photos", x => new { x.operation_id, x.id });
                    table.ForeignKey(
                        name: "fk_greening_operation_photos_greening_operations_operation_id",
                        column: x => x.operation_id,
                        principalTable: "greening_operations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_account_invitations_account_id_phone_number_status",
                table: "account_invitations",
                columns: new[] { "account_id", "phone_number", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_account_invitations_token_hash",
                table: "account_invitations",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_account_members_account_id_user_id",
                table: "account_members",
                columns: new[] { "account_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_account_members_user_id",
                table: "account_members",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_appointments_service_request_id",
                table: "appointments",
                column: "service_request_id");

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_actor_user_id",
                table: "audit_logs",
                column: "actor_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_at",
                table: "audit_logs",
                column: "at");

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_entity_type_entity_id",
                table: "audit_logs",
                columns: new[] { "entity_type", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_communes_department_id_slug",
                table: "communes",
                columns: new[] { "department_id", "slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_departments_region_id_slug",
                table: "departments",
                columns: new[] { "region_id", "slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_diagnostic_control_point_photos_control_point_id_media_file_id",
                table: "diagnostic_control_point_photos",
                columns: new[] { "control_point_id", "media_file_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_diagnostic_control_points_diagnostic_id",
                table: "diagnostic_control_points",
                column: "diagnostic_id");

            migrationBuilder.CreateIndex(
                name: "ix_diagnostics_scheduled_for",
                table: "diagnostics",
                column: "scheduled_for");

            migrationBuilder.CreateIndex(
                name: "ix_diagnostics_subscription_id_status",
                table: "diagnostics",
                columns: new[] { "subscription_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_greening_operation_photos_operation_id_media_file_id",
                table: "greening_operation_photos",
                columns: new[] { "operation_id", "media_file_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_greening_operations_program_id",
                table: "greening_operations",
                column: "program_id");

            migrationBuilder.CreateIndex(
                name: "ix_identity_user_claims_user_id",
                table: "identity_user_claims",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_identity_user_logins_user_id",
                table: "identity_user_logins",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "identity_users",
                column: "normalized_email");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "identity_users",
                column: "normalized_user_name",
                unique: true,
                filter: "[normalized_user_name] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_intervention_checklist_items_intervention_id",
                table: "intervention_checklist_items",
                column: "intervention_id");

            migrationBuilder.CreateIndex(
                name: "ix_intervention_photos_intervention_id_media_file_id",
                table: "intervention_photos",
                columns: new[] { "intervention_id", "media_file_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_interventions_lead_technician_id",
                table: "interventions",
                column: "lead_technician_id");

            migrationBuilder.CreateIndex(
                name: "ix_interventions_property_id_domain_warranty_ends_on",
                table: "interventions",
                columns: new[] { "property_id", "domain", "warranty_ends_on" });

            migrationBuilder.CreateIndex(
                name: "ix_interventions_request_id",
                table: "interventions",
                column: "request_id");

            migrationBuilder.CreateIndex(
                name: "ix_invoices_account_id",
                table: "invoices",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_invoices_number",
                table: "invoices",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_invoices_status_due_date",
                table: "invoices",
                columns: new[] { "status", "due_date" });

            migrationBuilder.CreateIndex(
                name: "ix_invoices_subscription_id",
                table: "invoices",
                column: "subscription_id");

            migrationBuilder.CreateIndex(
                name: "ix_media_files_account_id",
                table: "media_files",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_media_files_storage_key",
                table: "media_files",
                column: "storage_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_neighborhood_programs_neighborhood_id",
                table: "neighborhood_programs",
                column: "neighborhood_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_neighborhoods_commune_id_slug",
                table: "neighborhoods",
                columns: new[] { "commune_id", "slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_neighborhoods_name",
                table: "neighborhoods",
                column: "name");

            migrationBuilder.CreateIndex(
                name: "ix_neighborhoods_suggested_at",
                table: "neighborhoods",
                column: "suggested_at",
                filter: "suggested_at IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_notifications_status",
                table: "notifications",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_notifications_user_id_created_at",
                table: "notifications",
                columns: new[] { "user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_otp_challenges_ip_address_created_at",
                table: "otp_challenges",
                columns: new[] { "ip_address", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_otp_challenges_phone_number_created_at",
                table: "otp_challenges",
                columns: new[] { "phone_number", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_payments_channel_provider_reference",
                table: "payments",
                columns: new[] { "channel", "provider_reference" },
                unique: true,
                filter: "provider_reference IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_payments_invoice_id",
                table: "payments",
                column: "invoice_id");

            migrationBuilder.CreateIndex(
                name: "ix_payments_status_declared_by_user_id",
                table: "payments",
                columns: new[] { "status", "declared_by_user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_plan_sla_policies_plan_id_urgency",
                table: "plan_sla_policies",
                columns: new[] { "plan_id", "urgency" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_plans_code",
                table: "plans",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_preventive_advices_is_published",
                table: "preventive_advices",
                column: "is_published");

            migrationBuilder.CreateIndex(
                name: "ix_properties_account_id",
                table: "properties",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_properties_neighborhood_id",
                table: "properties",
                column: "neighborhood_id");

            migrationBuilder.CreateIndex(
                name: "ix_property_photos_property_id_media_file_id",
                table: "property_photos",
                columns: new[] { "property_id", "media_file_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_family_id",
                table: "refresh_tokens",
                column: "family_id");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_token_hash",
                table: "refresh_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_user_id",
                table: "refresh_tokens",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_regions_slug",
                table: "regions",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_service_request_attachments_service_request_id_media_file_id",
                table: "service_request_attachments",
                columns: new[] { "service_request_id", "media_file_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_service_requests_account_id_client_reference",
                table: "service_requests",
                columns: new[] { "account_id", "client_reference" },
                unique: true,
                filter: "client_reference IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_service_requests_account_id_created_at",
                table: "service_requests",
                columns: new[] { "account_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_service_requests_number",
                table: "service_requests",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_service_requests_property_id_created_at",
                table: "service_requests",
                columns: new[] { "property_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_service_requests_reported_by_user_id",
                table: "service_requests",
                column: "reported_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_service_requests_status_urgency_sla_deadline",
                table: "service_requests",
                columns: new[] { "status", "urgency", "sla_deadline" });

            migrationBuilder.CreateIndex(
                name: "ix_subscriptions_account_id",
                table: "subscriptions",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_subscriptions_plan_id",
                table: "subscriptions",
                column: "plan_id");

            migrationBuilder.CreateIndex(
                name: "ix_subscriptions_status",
                table: "subscriptions",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_technicians_user_id",
                table: "technicians",
                column: "user_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_roles_user_id_role",
                table: "user_roles",
                columns: new[] { "user_id", "role" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_phone_number",
                table: "users",
                column: "phone_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_referral_code",
                table: "users",
                column: "referral_code",
                unique: true,
                filter: "[referral_code] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_users_referred_by_user_id",
                table: "users",
                column: "referred_by_user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "account_invitations");

            migrationBuilder.DropTable(
                name: "account_members");

            migrationBuilder.DropTable(
                name: "app_settings");

            migrationBuilder.DropTable(
                name: "appointment_proposed_slots");

            migrationBuilder.DropTable(
                name: "audit_logs");

            migrationBuilder.DropTable(
                name: "data_protection_keys");

            migrationBuilder.DropTable(
                name: "diagnostic_control_point_photos");

            migrationBuilder.DropTable(
                name: "greening_operation_photos");

            migrationBuilder.DropTable(
                name: "identity_user_claims");

            migrationBuilder.DropTable(
                name: "identity_user_logins");

            migrationBuilder.DropTable(
                name: "identity_user_tokens");

            migrationBuilder.DropTable(
                name: "intervention_checklist_items");

            migrationBuilder.DropTable(
                name: "intervention_parts");

            migrationBuilder.DropTable(
                name: "intervention_photos");

            migrationBuilder.DropTable(
                name: "invoice_lines");

            migrationBuilder.DropTable(
                name: "media_files");

            migrationBuilder.DropTable(
                name: "notifications");

            migrationBuilder.DropTable(
                name: "otp_challenges");

            migrationBuilder.DropTable(
                name: "payments");

            migrationBuilder.DropTable(
                name: "plan_sla_policies");

            migrationBuilder.DropTable(
                name: "preventive_advices");

            migrationBuilder.DropTable(
                name: "property_photos");

            migrationBuilder.DropTable(
                name: "refresh_tokens");

            migrationBuilder.DropTable(
                name: "service_request_attachments");

            migrationBuilder.DropTable(
                name: "service_request_history");

            migrationBuilder.DropTable(
                name: "service_request_preferred_slots");

            migrationBuilder.DropTable(
                name: "technician_unavailabilities");

            migrationBuilder.DropTable(
                name: "technician_weekly_availability");

            migrationBuilder.DropTable(
                name: "user_consents");

            migrationBuilder.DropTable(
                name: "user_roles");

            migrationBuilder.DropTable(
                name: "appointments");

            migrationBuilder.DropTable(
                name: "diagnostic_control_points");

            migrationBuilder.DropTable(
                name: "greening_operations");

            migrationBuilder.DropTable(
                name: "identity_users");

            migrationBuilder.DropTable(
                name: "interventions");

            migrationBuilder.DropTable(
                name: "invoices");

            migrationBuilder.DropTable(
                name: "technicians");

            migrationBuilder.DropTable(
                name: "diagnostics");

            migrationBuilder.DropTable(
                name: "neighborhood_programs");

            migrationBuilder.DropTable(
                name: "service_requests");

            migrationBuilder.DropTable(
                name: "subscriptions");

            migrationBuilder.DropTable(
                name: "neighborhoods");

            migrationBuilder.DropTable(
                name: "properties");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "customer_accounts");

            migrationBuilder.DropTable(
                name: "plans");

            migrationBuilder.DropTable(
                name: "communes");

            migrationBuilder.DropTable(
                name: "departments");

            migrationBuilder.DropTable(
                name: "regions");

            migrationBuilder.DropSequence(
                name: "invoice_number_seq");

            migrationBuilder.DropSequence(
                name: "service_request_number_seq");
        }
    }
}
