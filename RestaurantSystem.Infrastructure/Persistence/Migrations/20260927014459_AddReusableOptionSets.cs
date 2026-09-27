using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReusableOptionSets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OptionSetAuthoringRevisions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    option_set_id = table.Column<Guid>(type: "uuid", nullable: true),
                    target_product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_menu_section_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_set_version = table.Column<int>(type: "integer", nullable: true),
                    applied_set_version = table.Column<int>(type: "integer", nullable: true),
                    summary_json = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_option_set_authoring_revisions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "OptionSetMatchDecisions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    normalized_name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    candidate_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    candidate_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_accepted = table.Column<bool>(type: "boolean", nullable: false),
                    alias = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_option_set_match_decisions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "OptionSets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    normalized_name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    source_template_id = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    source_revision = table.Column<int>(type: "integer", nullable: true),
                    source_option_set_id = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_option_sets", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "OptionSetAttachments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    option_set_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<int>(type: "integer", nullable: false),
                    target_product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_menu_section_id = table.Column<Guid>(type: "uuid", nullable: true),
                    applied_set_version = table.Column<int>(type: "integer", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    min_selection = table.Column<int>(type: "integer", nullable: true),
                    max_selection = table.Column<int>(type: "integer", nullable: true),
                    included_free = table.Column<int>(type: "integer", nullable: true),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    intentional_difference_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    last_idempotency_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_option_set_attachments", x => x.id);
                    table.CheckConstraint("ck_option_set_attachments_target", "(role = 2 AND target_menu_section_id IS NOT NULL) OR (role <> 2 AND target_menu_section_id IS NULL)");
                    table.ForeignKey(
                        name: "FK_OptionSetAttachments_Products_target_product_id",
                        column: x => x.target_product_id,
                        principalTable: "Products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OptionSetAttachments_menu_sections_target_menu_section_id",
                        column: x => x.target_menu_section_id,
                        principalTable: "menu_sections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_option_set_attachments_option_sets_option_set_id",
                        column: x => x.option_set_id,
                        principalTable: "OptionSets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OptionSetEntries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    option_set_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    display_order = table.Column<int>(type: "integer", nullable: false),
                    global_ingredient_id = table.Column<Guid>(type: "uuid", nullable: true),
                    product_id = table.Column<Guid>(type: "uuid", nullable: true),
                    product_variation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_optional = table.Column<bool>(type: "boolean", nullable: false),
                    max_quantity = table.Column<int>(type: "integer", nullable: false),
                    price = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    is_included_in_base_price = table.Column<bool>(type: "boolean", nullable: false),
                    is_required = table.Column<bool>(type: "boolean", nullable: false),
                    additional_price = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    source_entry_id = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_option_set_entries", x => x.id);
                    table.CheckConstraint("ck_option_set_entries_one_reference", "(global_ingredient_id IS NULL) <> (product_id IS NULL) AND (product_variation_id IS NULL OR product_id IS NOT NULL)");
                    table.CheckConstraint("ck_option_set_entries_positive_quantity", "max_quantity >= 1");
                    table.ForeignKey(
                        name: "FK_OptionSetEntries_Products_product_id",
                        column: x => x.product_id,
                        principalTable: "Products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OptionSetEntries_global_ingredients_global_ingredient_id",
                        column: x => x.global_ingredient_id,
                        principalTable: "global_ingredients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OptionSetEntries_product_variations_product_variation_id",
                        column: x => x.product_variation_id,
                        principalTable: "product_variations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_option_set_entries_option_sets_option_set_id",
                        column: x => x.option_set_id,
                        principalTable: "OptionSets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OptionSetAppliedRows",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    option_set_attachment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    option_set_entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                    row_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    materialized_row_id = table.Column<Guid>(type: "uuid", nullable: false),
                    last_applied_values_json = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_option_set_applied_rows", x => x.id);
                    table.ForeignKey(
                        name: "fk_option_set_applied_rows_optionsetattachments_option_set_att~",
                        column: x => x.option_set_attachment_id,
                        principalTable: "OptionSetAttachments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_option_set_applied_rows_optionsetentries_option_set_entry_id",
                        column: x => x.option_set_entry_id,
                        principalTable: "OptionSetEntries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_option_set_applied_rows_option_set_entry_id",
                table: "OptionSetAppliedRows",
                column: "option_set_entry_id");

            migrationBuilder.CreateIndex(
                name: "IX_OptionSetAppliedRows_option_set_attachment_id_option_set_en~",
                table: "OptionSetAppliedRows",
                columns: new[] { "option_set_attachment_id", "option_set_entry_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OptionSetAppliedRows_row_type_materialized_row_id",
                table: "OptionSetAppliedRows",
                columns: new[] { "row_type", "materialized_row_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_option_set_attachments_option_set_id",
                table: "OptionSetAttachments",
                column: "option_set_id");

            migrationBuilder.CreateIndex(
                name: "IX_OptionSetAttachments_target_menu_section_id_role",
                table: "OptionSetAttachments",
                columns: new[] { "target_menu_section_id", "role" },
                unique: true,
                filter: "\"target_menu_section_id\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OptionSetAttachments_target_product_id_role",
                table: "OptionSetAttachments",
                columns: new[] { "target_product_id", "role" },
                unique: true,
                filter: "\"target_menu_section_id\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OptionSetAuthoringRevisions_option_set_id_created_at",
                table: "OptionSetAuthoringRevisions",
                columns: new[] { "option_set_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_OptionSetAuthoringRevisions_target_product_id_created_at",
                table: "OptionSetAuthoringRevisions",
                columns: new[] { "target_product_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_OptionSetEntries_global_ingredient_id",
                table: "OptionSetEntries",
                column: "global_ingredient_id");

            migrationBuilder.CreateIndex(
                name: "IX_OptionSetEntries_option_set_id_global_ingredient_id",
                table: "OptionSetEntries",
                columns: new[] { "option_set_id", "global_ingredient_id" },
                unique: true,
                filter: "\"global_ingredient_id\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OptionSetEntries_option_set_id_product_id",
                table: "OptionSetEntries",
                columns: new[] { "option_set_id", "product_id" },
                unique: true,
                filter: "\"product_id\" IS NOT NULL AND \"product_variation_id\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OptionSetEntries_option_set_id_product_variation_id",
                table: "OptionSetEntries",
                columns: new[] { "option_set_id", "product_variation_id" },
                unique: true,
                filter: "\"product_variation_id\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OptionSetEntries_product_id",
                table: "OptionSetEntries",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "IX_OptionSetEntries_product_variation_id",
                table: "OptionSetEntries",
                column: "product_variation_id");

            migrationBuilder.CreateIndex(
                name: "IX_OptionSetMatchDecisions_normalized_name_candidate_type_cand~",
                table: "OptionSetMatchDecisions",
                columns: new[] { "normalized_name", "candidate_type", "candidate_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OptionSets_kind_normalized_name",
                table: "OptionSets",
                columns: new[] { "kind", "normalized_name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OptionSets_source_template_id_source_revision_source_option~",
                table: "OptionSets",
                columns: new[] { "source_template_id", "source_revision", "source_option_set_id" },
                unique: true,
                filter: "\"source_template_id\" IS NOT NULL AND \"source_revision\" IS NOT NULL AND \"source_option_set_id\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OptionSetAppliedRows");

            migrationBuilder.DropTable(
                name: "OptionSetAuthoringRevisions");

            migrationBuilder.DropTable(
                name: "OptionSetMatchDecisions");

            migrationBuilder.DropTable(
                name: "OptionSetAttachments");

            migrationBuilder.DropTable(
                name: "OptionSetEntries");

            migrationBuilder.DropTable(
                name: "OptionSets");
        }
    }
}
