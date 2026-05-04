using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFidelityPointsAndDiscounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "customer_discount_amount",
                table: "orders",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "customer_discount_rule_id",
                table: "orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "fidelity_points_discount",
                table: "orders",
                type: "numeric",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "fidelity_points_earned",
                table: "orders",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "fidelity_points_redeemed",
                table: "orders",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "customer_discount_rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    discount_type = table.Column<string>(type: "text", nullable: false),
                    discount_value = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    min_order_amount = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    max_order_amount = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    max_usage_count = table.Column<int>(type: "integer", nullable: true),
                    usage_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    valid_from = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    valid_until = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_customer_discount_rules", x => x.id);
                    table.ForeignKey(
                        name: "fk_customer_discount_rules_asp_net_users_user_id",
                        column: x => x.user_id,
                        principalTable: "Users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "fidelity_point_balances",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    current_points = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    total_earned_points = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    total_redeemed_points = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    last_updated = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fidelity_point_balances", x => x.id);
                    table.ForeignKey(
                        name: "fk_fidelity_point_balances_asp_net_users_user_id",
                        column: x => x.user_id,
                        principalTable: "Users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "fidelity_points_transactions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    transaction_type = table.Column<string>(type: "text", nullable: false),
                    points = table.Column<int>(type: "integer", nullable: false),
                    order_total = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fidelity_points_transactions", x => x.id);
                    table.ForeignKey(
                        name: "fk_fidelity_points_transactions_asp_net_users_user_id",
                        column: x => x.user_id,
                        principalTable: "Users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_fidelity_points_transactions_orders_order_id",
                        column: x => x.order_id,
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "point_earning_rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    min_order_amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    max_order_amount = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    points_awarded = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    priority = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_point_earning_rules", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_orders_customer_discount_rule_id",
                table: "orders",
                column: "customer_discount_rule_id");

            migrationBuilder.CreateIndex(
                name: "IX_customer_discount_rules_is_active",
                table: "customer_discount_rules",
                column: "is_active");

            migrationBuilder.CreateIndex(
                name: "ix_customer_discount_rules_user_id",
                table: "customer_discount_rules",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_customer_discount_rules_valid_from_valid_until",
                table: "customer_discount_rules",
                columns: new[] { "valid_from", "valid_until" });

            migrationBuilder.CreateIndex(
                name: "ix_fidelity_point_balances_user_id",
                table: "fidelity_point_balances",
                column: "user_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_fidelity_points_transactions_created_at",
                table: "fidelity_points_transactions",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_fidelity_points_transactions_order_id",
                table: "fidelity_points_transactions",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "IX_fidelity_points_transactions_transaction_type",
                table: "fidelity_points_transactions",
                column: "transaction_type");

            migrationBuilder.CreateIndex(
                name: "ix_fidelity_points_transactions_user_id",
                table: "fidelity_points_transactions",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_point_earning_rules_is_active",
                table: "point_earning_rules",
                column: "is_active");

            migrationBuilder.CreateIndex(
                name: "IX_point_earning_rules_min_order_amount_max_order_amount",
                table: "point_earning_rules",
                columns: new[] { "min_order_amount", "max_order_amount" });

            migrationBuilder.CreateIndex(
                name: "IX_point_earning_rules_priority",
                table: "point_earning_rules",
                column: "priority");

            migrationBuilder.AddForeignKey(
                name: "fk_orders_customer_discount_rules_customer_discount_rule_id",
                table: "orders",
                column: "customer_discount_rule_id",
                principalTable: "customer_discount_rules",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_orders_customer_discount_rules_customer_discount_rule_id",
                table: "orders");

            migrationBuilder.DropTable(
                name: "customer_discount_rules");

            migrationBuilder.DropTable(
                name: "fidelity_point_balances");

            migrationBuilder.DropTable(
                name: "fidelity_points_transactions");

            migrationBuilder.DropTable(
                name: "point_earning_rules");

            migrationBuilder.DropIndex(
                name: "ix_orders_customer_discount_rule_id",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "customer_discount_amount",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "customer_discount_rule_id",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "fidelity_points_discount",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "fidelity_points_earned",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "fidelity_points_redeemed",
                table: "orders");
        }
    }
}
