using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProductChoiceOptionSetTargets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OptionSetAttachments_target_product_id_role",
                table: "OptionSetAttachments");

            migrationBuilder.DropCheckConstraint(
                name: "ck_option_set_attachments_target",
                table: "OptionSetAttachments");

            migrationBuilder.AddColumn<int>(
                name: "authoring_version",
                table: "product_customization_groups",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<Guid>(
                name: "target_customization_group_id",
                table: "OptionSetAuthoringRevisions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "target_customization_group_id",
                table: "OptionSetAttachments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OptionSetAuthoringRevisions_target_customization_group_id",
                table: "OptionSetAuthoringRevisions",
                column: "target_customization_group_id");

            migrationBuilder.CreateIndex(
                name: "IX_OptionSetAttachments_target_customization_group_id_role",
                table: "OptionSetAttachments",
                columns: new[] { "target_customization_group_id", "role" },
                unique: true,
                filter: "\"target_customization_group_id\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OptionSetAttachments_target_product_id_role",
                table: "OptionSetAttachments",
                columns: new[] { "target_product_id", "role" },
                unique: true,
                filter: "\"target_menu_section_id\" IS NULL AND \"target_customization_group_id\" IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_option_set_attachments_target",
                table: "OptionSetAttachments",
                sql: "(role = 2 AND target_menu_section_id IS NOT NULL AND target_customization_group_id IS NULL) OR (role = 4 AND target_menu_section_id IS NULL AND target_customization_group_id IS NOT NULL) OR (role NOT IN (2, 4) AND target_menu_section_id IS NULL AND target_customization_group_id IS NULL)");

            migrationBuilder.AddForeignKey(
                name: "FK_OptionSetAttachments_product_customization_groups_target_cu~",
                table: "OptionSetAttachments",
                column: "target_customization_group_id",
                principalTable: "product_customization_groups",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_OptionSetAuthoringRevisions_product_customization_groups_ta~",
                table: "OptionSetAuthoringRevisions",
                column: "target_customization_group_id",
                principalTable: "product_customization_groups",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OptionSetAttachments_product_customization_groups_target_cu~",
                table: "OptionSetAttachments");

            migrationBuilder.DropForeignKey(
                name: "FK_OptionSetAuthoringRevisions_product_customization_groups_ta~",
                table: "OptionSetAuthoringRevisions");

            migrationBuilder.DropIndex(
                name: "IX_OptionSetAuthoringRevisions_target_customization_group_id",
                table: "OptionSetAuthoringRevisions");

            migrationBuilder.DropIndex(
                name: "IX_OptionSetAttachments_target_customization_group_id_role",
                table: "OptionSetAttachments");

            migrationBuilder.DropIndex(
                name: "IX_OptionSetAttachments_target_product_id_role",
                table: "OptionSetAttachments");

            migrationBuilder.DropCheckConstraint(
                name: "ck_option_set_attachments_target",
                table: "OptionSetAttachments");

            migrationBuilder.DropColumn(
                name: "authoring_version",
                table: "product_customization_groups");

            migrationBuilder.DropColumn(
                name: "target_customization_group_id",
                table: "OptionSetAuthoringRevisions");

            migrationBuilder.DropColumn(
                name: "target_customization_group_id",
                table: "OptionSetAttachments");

            migrationBuilder.CreateIndex(
                name: "IX_OptionSetAttachments_target_product_id_role",
                table: "OptionSetAttachments",
                columns: new[] { "target_product_id", "role" },
                unique: true,
                filter: "\"target_menu_section_id\" IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_option_set_attachments_target",
                table: "OptionSetAttachments",
                sql: "(role = 2 AND target_menu_section_id IS NOT NULL) OR (role <> 2 AND target_menu_section_id IS NULL)");
        }
    }
}
