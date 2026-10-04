using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using RestaurantSystem.Infrastructure.Persistence;

#nullable disable

namespace RestaurantSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261004072343_AddNativeOrderBillingSnapshots")] // pragma: allowlist secret
    public partial class AddNativeOrderBillingSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_OrderItems_order_id_id",
                table: "OrderItems",
                columns: new[] { "order_id", "id" });

            migrationBuilder.CreateTable(
                name: "order_billing_snapshots",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    pricing_policy_version = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    component_quantization_policy_version = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    earning_basis_policy_version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    gross_food_minor = table.Column<long>(type: "bigint", nullable: false),
                    tax_minor = table.Column<long>(type: "bigint", nullable: false),
                    delivery_fee_minor = table.Column<long>(type: "bigint", nullable: false),
                    charged_delivery_fee_minor = table.Column<long>(type: "bigint", nullable: false),
                    order_discount_minor = table.Column<long>(type: "bigint", nullable: false),
                    customer_discount_minor = table.Column<long>(type: "bigint", nullable: false),
                    courtesy_rounding_minor = table.Column<long>(type: "bigint", nullable: false),
                    redeemed_points = table.Column<int>(type: "integer", nullable: false),
                    redemption_discount_minor = table.Column<long>(type: "bigint", nullable: false),
                    payable_food_minor = table.Column<long>(type: "bigint", nullable: false),
                    tip_minor = table.Column<long>(type: "bigint", nullable: false),
                    total_minor = table.Column<long>(type: "bigint", nullable: false),
                    food_reconciliation_minor = table.Column<long>(type: "bigint", nullable: false),
                    raw_tax_amount = table.Column<decimal>(type: "numeric", nullable: false),
                    raw_order_discount_amount = table.Column<decimal>(type: "numeric", nullable: false),
                    raw_customer_discount_amount = table.Column<decimal>(type: "numeric", nullable: false),
                    raw_redemption_discount_amount = table.Column<decimal>(type: "numeric", nullable: false),
                    raw_courtesy_rounding_amount = table.Column<decimal>(type: "numeric", nullable: false),
                    earning_basis_minor = table.Column<long>(type: "bigint", nullable: false),
                    earned_points_candidate = table.Column<int>(type: "integer", nullable: true),
                    earning_evaluation_version = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    earning_rule_set_fingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    earning_rule_id = table.Column<Guid>(type: "uuid", nullable: true),
                    earning_rule_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    earning_rule_minimum_minor = table.Column<long>(type: "bigint", nullable: true),
                    earning_rule_maximum_minor = table.Column<long>(type: "bigint", nullable: true),
                    earning_rule_points = table.Column<int>(type: "integer", nullable: true),
                    earning_rule_priority = table.Column<int>(type: "integer", nullable: true),
                    redemption_transaction_id = table.Column<Guid>(type: "uuid", nullable: true),
                    redemption_transaction_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    redemption_transaction_points = table.Column<int>(type: "integer", nullable: true),
                    redemption_transaction_order_total = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    redemption_transaction_created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    tax_category = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    tax_rate_basis_points = table.Column<int>(type: "integer", nullable: false),
                    tax_treatment = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_billing_snapshots", x => x.id);
                    table.UniqueConstraint("AK_order_billing_snapshots_order_id", x => x.order_id);
                    table.CheckConstraint("ck_order_billing_snapshot_values", HeaderConstraintSqlAtMigration);
                    table.ForeignKey(
                        name: "FK_order_billing_snapshots_orders_order_id",
                        column: x => x.order_id,
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "order_billing_snapshot_owner_links",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    slot = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    disposition = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    erased_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    erasure_transaction_id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_billing_snapshot_owner_links", x => x.id);
                    table.CheckConstraint("ck_order_billing_snapshot_owner_link_shape", OwnerLinkConstraintSqlAtMigration);
                    table.ForeignKey(
                        name: "fk_snapshot_owner_link_snapshot_order",
                        column: x => x.order_id,
                        principalTable: "order_billing_snapshots",
                        principalColumn: "order_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_snapshot_owner_link_user",
                        column: x => x.user_id,
                        principalTable: "Users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "order_billing_snapshot_units",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    unit_ordinal = table.Column<int>(type: "integer", nullable: false),
                    gross_food_minor = table.Column<long>(type: "bigint", nullable: false),
                    tax_minor = table.Column<long>(type: "bigint", nullable: false),
                    tax_rate_basis_points = table.Column<int>(type: "integer", nullable: false),
                    tax_category = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    tax_treatment = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    order_discount_minor = table.Column<long>(type: "bigint", nullable: false),
                    customer_discount_minor = table.Column<long>(type: "bigint", nullable: false),
                    courtesy_rounding_minor = table.Column<long>(type: "bigint", nullable: false),
                    redeemed_points = table.Column<int>(type: "integer", nullable: false),
                    redemption_discount_minor = table.Column<long>(type: "bigint", nullable: false),
                    payable_food_minor = table.Column<long>(type: "bigint", nullable: false),
                    food_reconciliation_minor = table.Column<long>(type: "bigint", nullable: false),
                    earning_basis_minor = table.Column<long>(type: "bigint", nullable: false),
                    earned_points = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_billing_snapshot_units", x => x.id);
                    table.CheckConstraint("ck_order_billing_snapshot_unit_values", UnitConstraintSqlAtMigration);
                    table.ForeignKey(
                        name: "FK_order_billing_snapshot_units_OrderItems_order_id_order_item~",
                        columns: x => new { x.order_id, x.order_item_id },
                        principalTable: "OrderItems",
                        principalColumns: new[] { "order_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_order_billing_snapshot_units_order_billing_snapshots_order_~",
                        column: x => x.order_id,
                        principalTable: "order_billing_snapshots",
                        principalColumn: "order_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_order_billing_snapshot_owner_links_order_id_slot",
                table: "order_billing_snapshot_owner_links",
                columns: new[] { "order_id", "slot" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_order_billing_snapshot_owner_links_user_id",
                table: "order_billing_snapshot_owner_links",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_order_billing_snapshot_units_order_id_order_item_id_unit_or~",
                table: "order_billing_snapshot_units",
                columns: new[] { "order_id", "order_item_id", "unit_ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_order_billing_snapshots_redemption_transaction_id",
                table: "order_billing_snapshots",
                column: "redemption_transaction_id",
                unique: true,
                filter: "\"redemption_transaction_id\" IS NOT NULL");

            ProtectOrderBillingSnapshotHistory(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            RequireNoOrderBillingSnapshotHistory(migrationBuilder);

            migrationBuilder.DropTable(
                name: "order_billing_snapshot_owner_links");

            migrationBuilder.DropTable(
                name: "order_billing_snapshot_units");

            migrationBuilder.DropTable(
                name: "order_billing_snapshots");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_OrderItems_order_id_id",
                table: "OrderItems");

        }
    }
}
