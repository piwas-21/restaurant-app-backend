using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderBillingCredits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "billing_allocation_version",
                table: "table_service_sessions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "billing_credit_amount",
                table: "orders",
                type: "numeric(10,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_order_amendments_id_source_order_id",
                table: "order_amendments",
                columns: new[] { "id", "source_order_id" });

            migrationBuilder.CreateTable(
                name: "order_billing_credits",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    source_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amendment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount_minor = table.Column<long>(type: "bigint", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_role = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_billing_credits", x => x.id);
                    table.CheckConstraint("ck_order_billing_credit_money", "amount_minor > 0 AND currency ~ '^[A-Z]{3}$'");
                    table.ForeignKey(
                        name: "FK_order_billing_credits_order_amendments_amendment_id_source_~",
                        columns: x => new { x.amendment_id, x.source_order_id },
                        principalTable: "order_amendments",
                        principalColumns: new[] { "id", "source_order_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_order_billing_credits_orders_source_order_id",
                        column: x => x.source_order_id,
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_order_billing_credit_bounds",
                table: "orders",
                sql: "billing_credit_amount >= 0 AND billing_credit_amount <= total");

            migrationBuilder.CreateIndex(
                name: "IX_order_billing_credits_amendment_id",
                table: "order_billing_credits",
                column: "amendment_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_order_billing_credits_amendment_id_source_order_id",
                table: "order_billing_credits",
                columns: new[] { "amendment_id", "source_order_id" });

            migrationBuilder.CreateIndex(
                name: "IX_order_billing_credits_source_order_id_created_at",
                table: "order_billing_credits",
                columns: new[] { "source_order_id", "created_at" });

            ApplyCompatibilityBackfill(migrationBuilder);
            ProtectBillingHistory(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            RequireEmptyBillingHistory(migrationBuilder);
            migrationBuilder.DropTable(
                name: "order_billing_credits");

            migrationBuilder.DropCheckConstraint(
                name: "ck_order_billing_credit_bounds",
                table: "orders");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_order_amendments_id_source_order_id",
                table: "order_amendments");

            migrationBuilder.DropColumn(
                name: "billing_allocation_version",
                table: "table_service_sessions");

            migrationBuilder.DropColumn(
                name: "billing_credit_amount",
                table: "orders");
        }
    }
}
