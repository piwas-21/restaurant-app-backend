using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddExternalOrderReference : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ExternalOrderReferences",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    external_store_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    external_order_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    external_display_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    external_state = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    last_event_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    merchant_total = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    reported_tax = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    payload_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    fulfillment_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    is_sandbox = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_external_order_references", x => x.id);
                    table.CheckConstraint("CK_ExternalOrderReferences_Currency", "\"currency\" ~ '^[A-Z]{3}$'");
                    table.CheckConstraint("CK_ExternalOrderReferences_Money", "\"merchant_total\" >= 0 AND (\"reported_tax\" IS NULL OR \"reported_tax\" >= 0)");
                    table.CheckConstraint("CK_ExternalOrderReferences_PayloadHash", "\"payload_hash\" ~ '^[a-f0-9]{64}$'");
                    table.ForeignKey(
                        name: "fk_external_order_references_orders_order_id",
                        column: x => x.order_id,
                        principalTable: "orders",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "ix_external_order_references_order_id",
                table: "ExternalOrderReferences",
                column: "order_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExternalOrderReferences_provider_external_store_id_external~",
                table: "ExternalOrderReferences",
                columns: new[] { "provider", "external_store_id", "external_order_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExternalOrderReferences");
        }
    }
}
