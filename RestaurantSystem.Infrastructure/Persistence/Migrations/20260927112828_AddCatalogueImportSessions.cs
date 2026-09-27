using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCatalogueImportSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "catalogue_cuisine_preferences",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    cuisines = table.Column<List<string>>(type: "text[]", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_catalogue_cuisine_preferences", x => x.id);
                    table.CheckConstraint("ck_catalogue_cuisine_preferences_singleton", "id = '4a44b885-29e2-4000-8000-000000000006'");
                });

            migrationBuilder.CreateTable(
                name: "catalogue_import_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    root_template_id = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    root_revision = table.Column<int>(type: "integer", nullable: false),
                    locale = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    adoption_id = table.Column<Guid>(type: "uuid", nullable: false),
                    create_new_copy = table.Column<bool>(type: "boolean", nullable: false),
                    create_selection_json = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_import_idempotency_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    last_import_expected_version = table.Column<int>(type: "integer", nullable: true),
                    last_import_result_json = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_catalogue_import_sessions", x => x.id);
                    table.CheckConstraint("ck_catalogue_import_sessions_root_revision", "root_revision > 0");
                });

            migrationBuilder.CreateTable(
                name: "catalogue_match_decisions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    source_template_id = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    source_revision = table.Column<int>(type: "integer", nullable: false),
                    normalized_name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    candidate_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    candidate_id = table.Column<Guid>(type: "uuid", nullable: false),
                    decision = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_catalogue_match_decisions", x => x.id);
                    table.CheckConstraint("ck_catalogue_match_decisions_source_revision", "source_revision > 0");
                });

            migrationBuilder.CreateTable(
                name: "catalogue_template_adoptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    adoption_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_template_id = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    source_revision = table.Column<int>(type: "integer", nullable: false),
                    source_entry_id = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    local_entity_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    local_entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    content_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    baseline_fields_json = table.Column<string>(type: "jsonb", nullable: true),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_catalogue_template_adoptions", x => x.id);
                    table.CheckConstraint("ck_catalogue_template_adoptions_source_revision", "source_revision > 0");
                });

            migrationBuilder.CreateTable(
                name: "catalogue_import_session_templates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    template_id = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    content_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    revision_json = table.Column<string>(type: "jsonb", nullable: false),
                    decision_json = table.Column<string>(type: "jsonb", nullable: true),
                    is_root = table.Column<bool>(type: "boolean", nullable: false),
                    is_selectable = table.Column<bool>(type: "boolean", nullable: false),
                    is_selected = table.Column<bool>(type: "boolean", nullable: false),
                    selection_role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    local_entity_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    local_entity_id = table.Column<Guid>(type: "uuid", nullable: true),
                    failure_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_catalogue_import_session_templates", x => x.id);
                    table.CheckConstraint("ck_catalogue_import_session_templates_revision", "revision > 0");
                    table.ForeignKey(
                        name: "fk_catalogue_import_session_templates_catalogue_import_session~",
                        column: x => x.session_id,
                        principalTable: "catalogue_import_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_catalogue_import_session_templates_session_id_is_selected",
                table: "catalogue_import_session_templates",
                columns: new[] { "session_id", "is_selected" });

            migrationBuilder.CreateIndex(
                name: "IX_catalogue_import_session_templates_session_id_template_id_r~",
                table: "catalogue_import_session_templates",
                columns: new[] { "session_id", "template_id", "revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_catalogue_import_sessions_idempotency_key",
                table: "catalogue_import_sessions",
                column: "idempotency_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_catalogue_import_sessions_root_template_id_root_revision",
                table: "catalogue_import_sessions",
                columns: new[] { "root_template_id", "root_revision" });

            migrationBuilder.CreateIndex(
                name: "IX_catalogue_match_decisions_normalized_name_candidate_type",
                table: "catalogue_match_decisions",
                columns: new[] { "normalized_name", "candidate_type" });

            migrationBuilder.CreateIndex(
                name: "IX_catalogue_match_decisions_source_template_id_source_revisio~",
                table: "catalogue_match_decisions",
                columns: new[] { "source_template_id", "source_revision", "candidate_type", "candidate_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_catalogue_template_adoptions_adoption_id_source_template_i~1",
                table: "catalogue_template_adoptions",
                columns: new[] { "adoption_id", "source_template_id", "source_revision", "local_entity_type", "source_entry_id" },
                unique: true,
                filter: "\"source_entry_id\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_catalogue_template_adoptions_adoption_id_source_template_id~",
                table: "catalogue_template_adoptions",
                columns: new[] { "adoption_id", "source_template_id", "source_revision", "local_entity_type" },
                unique: true,
                filter: "\"source_entry_id\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_catalogue_template_adoptions_session_id_adoption_id",
                table: "catalogue_template_adoptions",
                columns: new[] { "session_id", "adoption_id" });

            migrationBuilder.CreateIndex(
                name: "IX_catalogue_template_adoptions_source_template_id_source_rev~1",
                table: "catalogue_template_adoptions",
                columns: new[] { "source_template_id", "source_revision", "local_entity_type", "source_entry_id" },
                unique: true,
                filter: "\"is_default\" = true AND \"source_entry_id\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_catalogue_template_adoptions_source_template_id_source_revi~",
                table: "catalogue_template_adoptions",
                columns: new[] { "source_template_id", "source_revision", "local_entity_type" },
                unique: true,
                filter: "\"is_default\" = true AND \"source_entry_id\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "catalogue_cuisine_preferences");

            migrationBuilder.DropTable(
                name: "catalogue_import_session_templates");

            migrationBuilder.DropTable(
                name: "catalogue_match_decisions");

            migrationBuilder.DropTable(
                name: "catalogue_template_adoptions");

            migrationBuilder.DropTable(
                name: "catalogue_import_sessions");
        }
    }
}
