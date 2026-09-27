using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTranslationWorkbench : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "translation_field_provenances",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    entity_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    field_key = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    locale = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    source_locale = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    source_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    context_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    text_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    kind = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    review_status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    template_id = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    template_revision = table.Column<int>(type: "integer", nullable: true),
                    reviewer_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    reviewed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_translation_field_provenances", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "translation_generation_batches",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    requested_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    model = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    input_tokens = table.Column<int>(type: "integer", nullable: false),
                    output_tokens = table.Column<int>(type: "integer", nullable: false),
                    estimated_cost_usd = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_translation_generation_batches", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "translation_suggestions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entity_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: true),
                    client_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    field_key = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    locale = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    source_locale = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    source_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    context_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    suggested_text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    reviewed_text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    model = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    requested_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    reviewer_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    reviewed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_translation_suggestions", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_translation_field_provenances_entity_type_entity_id_field_k~",
                table: "translation_field_provenances",
                columns: new[] { "entity_type", "entity_id", "field_key", "locale" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_translation_generation_batches_created_at",
                table: "translation_generation_batches",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "IX_translation_generation_batches_fingerprint",
                table: "translation_generation_batches",
                column: "fingerprint");

            migrationBuilder.CreateIndex(
                name: "IX_translation_suggestions_batch_id",
                table: "translation_suggestions",
                column: "batch_id");

            migrationBuilder.CreateIndex(
                name: "IX_translation_suggestions_created_at_requested_by",
                table: "translation_suggestions",
                columns: new[] { "created_at", "requested_by" });

            migrationBuilder.CreateIndex(
                name: "IX_translation_suggestions_fingerprint_entity_type_entity_id_c~",
                table: "translation_suggestions",
                columns: new[] { "fingerprint", "entity_type", "entity_id", "client_key", "field_key", "locale", "requested_by" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "translation_field_provenances");

            migrationBuilder.DropTable(
                name: "translation_generation_batches");

            migrationBuilder.DropTable(
                name: "translation_suggestions");
        }
    }
}
