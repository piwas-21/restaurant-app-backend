using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderAmendmentResolutionRefusals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "order_amendment_resolution_refusals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amendment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_operation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    failure_code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    original_request_json = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_amendment_resolution_refusals", x => x.id);
                    table.CheckConstraint("ck_amendment_resolution_refusal_code", "failure_code IN ('quoteExpired','sourceVersionConflict','accountRevisionConflict','quoteChanged')");
                    table.CheckConstraint("ck_amendment_resolution_refusal_hash", "request_hash ~ '^[a-f0-9]{64}$'");
                    table.ForeignKey(
                        name: "FK_order_amendment_resolution_refusals_order_amendments_amendm~",
                        column: x => x.amendment_id,
                        principalTable: "order_amendments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_order_amendment_resolution_refusals_orders_source_order_id",
                        column: x => x.source_order_id,
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_order_amendment_resolution_refusals_actor_user_id_client_op~",
                table: "order_amendment_resolution_refusals",
                columns: new[] { "actor_user_id", "client_operation_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_order_amendment_resolution_refusals_actor_user_id_source_or~",
                table: "order_amendment_resolution_refusals",
                columns: new[] { "actor_user_id", "source_order_id", "amendment_id" });

            migrationBuilder.CreateIndex(
                name: "IX_order_amendment_resolution_refusals_amendment_id",
                table: "order_amendment_resolution_refusals",
                column: "amendment_id");

            migrationBuilder.CreateIndex(
                name: "IX_order_amendment_resolution_refusals_source_order_id",
                table: "order_amendment_resolution_refusals",
                column: "source_order_id");

            ProtectRefusalHistory(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            RequireNoRefusalHistory(migrationBuilder);
            migrationBuilder.DropTable(
                name: "order_amendment_resolution_refusals");
        }
    }
}
