using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RestaurantSystem.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderBillingEarningRetirements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_order_billing_snapshot_values",
                table: "order_billing_snapshots");

            migrationBuilder.AddColumn<string>(
                name: "earning_disposition",
                table: "order_billing_snapshots",
                type: "character varying(48)",
                maxLength: 48,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "order_billing_earning_retirements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    snapshot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amendment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    retired_unit_count = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_billing_earning_retirements", x => x.id);
                    table.CheckConstraint("ck_order_billing_earning_retirement_values", "retired_unit_count > 0 AND created_by = 'OrderBillingEarningRetirementService' AND updated_at IS NULL AND updated_by IS NULL");
                    table.ForeignKey(
                        name: "FK_order_billing_earning_retirements_order_amendments_order_id~",
                        columns: x => new { x.order_id, x.amendment_id },
                        principalTable: "order_amendments",
                        principalColumns: new[] { "source_order_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_order_billing_earning_retirements_order_billing_snapshots_o~",
                        columns: x => new { x.order_id, x.snapshot_id },
                        principalTable: "order_billing_snapshots",
                        principalColumns: new[] { "order_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_order_billing_earning_retirements_orders_order_id",
                        column: x => x.order_id,
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_order_billing_snapshot_values",
                table: "order_billing_snapshots",
                sql: "created_by = 'OrderBillingSnapshotFactory' AND updated_by IS NULL\nAND currency ~ '^[A-Z]{3}$' AND tax_minor = 0 AND tax_rate_basis_points = 0\nAND tax_category = 'none' AND tax_treatment = 'NotApplied'\nAND pricing_policy_version = 'native-zero-tax-v1'\nAND component_quantization_policy_version = 'currency-minor-2dp-away-from-zero-v1'\nAND earning_basis_policy_version = 'raw-root-item-total-v1'\nAND raw_tax_amount = 0 AND raw_order_discount_amount >= 0\nAND raw_customer_discount_amount >= 0 AND raw_redemption_discount_amount >= 0\nAND order_discount_minor = round(raw_order_discount_amount, 2) * 100\nAND customer_discount_minor = round(raw_customer_discount_amount, 2) * 100\nAND tax_minor = round(raw_tax_amount, 2) * 100\nAND redemption_discount_minor = round(raw_redemption_discount_amount, 2) * 100\nAND courtesy_rounding_minor = round(raw_courtesy_rounding_amount, 2) * 100\nAND gross_food_minor >= 0 AND delivery_fee_minor >= 0 AND charged_delivery_fee_minor >= 0\nAND charged_delivery_fee_minor <= delivery_fee_minor AND order_discount_minor >= 0\nAND customer_discount_minor >= 0 AND redeemed_points >= 0 AND redemption_discount_minor >= 0\nAND payable_food_minor >= 0 AND tip_minor >= 0 AND total_minor >= 0\nAND earning_basis_minor = gross_food_minor\nAND payable_food_minor + charged_delivery_fee_minor + tip_minor = total_minor\nAND food_reconciliation_minor = payable_food_minor - (gross_food_minor - tax_minor\n- order_discount_minor - customer_discount_minor + courtesy_rounding_minor - redemption_discount_minor)\nAND (earned_points_candidate IS NULL OR earned_points_candidate >= 0)\nAND ((earning_disposition IS NULL AND\n((earned_points_candidate IS NULL AND earning_evaluation_version IS NULL\nAND earning_rule_set_fingerprint IS NULL AND earning_rule_id IS NULL\nAND earning_rule_name IS NULL AND earning_rule_minimum_minor IS NULL\nAND earning_rule_maximum_minor IS NULL AND earning_rule_points IS NULL\nAND earning_rule_priority IS NULL)\nOR (earned_points_candidate IS NOT NULL AND earning_evaluation_version IS NOT NULL\nAND earning_evaluation_version <> '' AND earning_rule_set_fingerprint IS NOT NULL\nAND earning_rule_set_fingerprint ~ '^[0-9a-f]{64}$')))\nOR (earning_disposition IN ('Unevaluated', 'NoCustomerOwnerAtAcceptance', 'LoyaltyModuleDisabledAtAcceptance')\nAND earned_points_candidate IS NULL AND earning_evaluation_version IS NULL\nAND earning_rule_set_fingerprint IS NULL AND earning_rule_id IS NULL\nAND earning_rule_name IS NULL AND earning_rule_minimum_minor IS NULL\nAND earning_rule_maximum_minor IS NULL AND earning_rule_points IS NULL\nAND earning_rule_priority IS NULL)\nOR (earning_disposition = 'Evaluated' AND earned_points_candidate IS NOT NULL\nAND earning_evaluation_version IS NOT NULL AND earning_evaluation_version <> ''\nAND earning_rule_set_fingerprint IS NOT NULL\nAND earning_rule_set_fingerprint ~ '^[0-9a-f]{64}$'))\nAND ((earning_rule_id IS NULL AND earning_rule_name IS NULL\nAND earning_rule_minimum_minor IS NULL AND earning_rule_maximum_minor IS NULL\nAND earning_rule_points IS NULL AND earning_rule_priority IS NULL\nAND (earned_points_candidate IS NULL OR earned_points_candidate = 0))\nOR (earning_rule_id IS NOT NULL AND earning_rule_name IS NOT NULL\nAND earning_rule_minimum_minor IS NOT NULL AND earning_rule_minimum_minor >= 0\nAND earning_rule_points IS NOT NULL AND earning_rule_priority IS NOT NULL\nAND earning_rule_points = earned_points_candidate\nAND earning_rule_minimum_minor <= earning_basis_minor\nAND (earning_rule_maximum_minor IS NULL OR earning_rule_maximum_minor >= earning_basis_minor)))\nAND ((redemption_transaction_id IS NULL AND redemption_transaction_type IS NULL\nAND redemption_transaction_points IS NULL AND redemption_transaction_order_total IS NULL\nAND redemption_transaction_created_at IS NULL AND redeemed_points = 0\nAND redemption_discount_minor = 0) OR (redemption_transaction_id IS NOT NULL\nAND redemption_transaction_type IS NOT NULL AND redemption_transaction_type = 'Redeemed'\nAND redemption_transaction_points IS NOT NULL\nAND redemption_transaction_points = -redeemed_points\nAND redemption_transaction_order_total IS NULL AND redemption_transaction_created_at IS NOT NULL\nAND redeemed_points > 0\n-- Current fidelity value is 100 points per major unit; supported currencies all have two decimals.\nAND redemption_discount_minor = redeemed_points\nAND raw_redemption_discount_amount * 100 = redeemed_points))");

            migrationBuilder.CreateIndex(
                name: "IX_order_billing_earning_retirements_order_id",
                table: "order_billing_earning_retirements",
                column: "order_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_order_billing_earning_retirements_order_id_amendment_id",
                table: "order_billing_earning_retirements",
                columns: new[] { "order_id", "amendment_id" });

            migrationBuilder.CreateIndex(
                name: "IX_order_billing_earning_retirements_order_id_snapshot_id",
                table: "order_billing_earning_retirements",
                columns: new[] { "order_id", "snapshot_id" });

            ProtectOrderBillingEarningRetirements(migrationBuilder);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                  IF EXISTS (SELECT 1 FROM order_billing_earning_retirements)
                     OR EXISTS (SELECT 1 FROM order_billing_snapshots WHERE earning_disposition IS NOT NULL) THEN
                    RAISE EXCEPTION 'Native order earning dispositions and retirements must be retained' USING ERRCODE = '23514';
                  END IF;
                END $$;
                """);

            RemoveOrderBillingEarningRetirements(migrationBuilder);

            migrationBuilder.DropTable(
                name: "order_billing_earning_retirements");

            migrationBuilder.DropCheckConstraint(
                name: "ck_order_billing_snapshot_values",
                table: "order_billing_snapshots");

            migrationBuilder.DropColumn(
                name: "earning_disposition",
                table: "order_billing_snapshots");

            migrationBuilder.AddCheckConstraint(
                name: "ck_order_billing_snapshot_values",
                table: "order_billing_snapshots",
                sql: "created_by = 'OrderBillingSnapshotFactory' AND updated_by IS NULL\nAND currency ~ '^[A-Z]{3}$' AND tax_minor = 0 AND tax_rate_basis_points = 0\nAND tax_category = 'none' AND tax_treatment = 'NotApplied'\nAND pricing_policy_version = 'native-zero-tax-v1'\nAND component_quantization_policy_version = 'currency-minor-2dp-away-from-zero-v1'\nAND earning_basis_policy_version = 'raw-root-item-total-v1'\nAND raw_tax_amount = 0 AND raw_order_discount_amount >= 0\nAND raw_customer_discount_amount >= 0 AND raw_redemption_discount_amount >= 0\nAND order_discount_minor = round(raw_order_discount_amount, 2) * 100\nAND customer_discount_minor = round(raw_customer_discount_amount, 2) * 100\nAND tax_minor = round(raw_tax_amount, 2) * 100\nAND redemption_discount_minor = round(raw_redemption_discount_amount, 2) * 100\nAND courtesy_rounding_minor = round(raw_courtesy_rounding_amount, 2) * 100\nAND gross_food_minor >= 0 AND delivery_fee_minor >= 0 AND charged_delivery_fee_minor >= 0\nAND charged_delivery_fee_minor <= delivery_fee_minor AND order_discount_minor >= 0\nAND customer_discount_minor >= 0 AND redeemed_points >= 0 AND redemption_discount_minor >= 0\nAND payable_food_minor >= 0 AND tip_minor >= 0 AND total_minor >= 0\nAND earning_basis_minor = gross_food_minor\nAND payable_food_minor + charged_delivery_fee_minor + tip_minor = total_minor\nAND food_reconciliation_minor = payable_food_minor - (gross_food_minor - tax_minor\n- order_discount_minor - customer_discount_minor + courtesy_rounding_minor - redemption_discount_minor)\nAND (earned_points_candidate IS NULL OR earned_points_candidate >= 0)\nAND ((earned_points_candidate IS NULL\nAND earning_evaluation_version IS NULL AND earning_rule_set_fingerprint IS NULL\nAND earning_rule_id IS NULL AND earning_rule_name IS NULL AND earning_rule_minimum_minor IS NULL\nAND earning_rule_maximum_minor IS NULL AND earning_rule_points IS NULL AND earning_rule_priority IS NULL)\nOR (earned_points_candidate IS NOT NULL\nAND earning_evaluation_version IS NOT NULL AND earning_evaluation_version <> ''\nAND earning_rule_set_fingerprint IS NOT NULL\nAND earning_rule_set_fingerprint ~ '^[0-9a-f]{64}$'))\nAND ((earning_rule_id IS NULL AND earning_rule_name IS NULL\nAND earning_rule_minimum_minor IS NULL AND earning_rule_maximum_minor IS NULL\nAND earning_rule_points IS NULL AND earning_rule_priority IS NULL\nAND (earned_points_candidate IS NULL OR earned_points_candidate = 0))\nOR (earning_rule_id IS NOT NULL AND earning_rule_name IS NOT NULL\nAND earning_rule_minimum_minor IS NOT NULL AND earning_rule_minimum_minor >= 0\nAND earning_rule_points IS NOT NULL AND earning_rule_priority IS NOT NULL\nAND earning_rule_points = earned_points_candidate\nAND earning_rule_minimum_minor <= earning_basis_minor\nAND (earning_rule_maximum_minor IS NULL OR earning_rule_maximum_minor >= earning_basis_minor)))\nAND ((redemption_transaction_id IS NULL AND redemption_transaction_type IS NULL\nAND redemption_transaction_points IS NULL AND redemption_transaction_order_total IS NULL\nAND redemption_transaction_created_at IS NULL AND redeemed_points = 0\nAND redemption_discount_minor = 0) OR (redemption_transaction_id IS NOT NULL\nAND redemption_transaction_type IS NOT NULL AND redemption_transaction_type = 'Redeemed'\nAND redemption_transaction_points IS NOT NULL\nAND redemption_transaction_points = -redeemed_points\nAND redemption_transaction_order_total IS NULL AND redemption_transaction_created_at IS NOT NULL\nAND redeemed_points > 0\n-- Current fidelity value is 100 points per major unit; supported currencies all have two decimals.\nAND redemption_discount_minor = redeemed_points\nAND raw_redemption_discount_amount * 100 = redeemed_points))");
        }
    }
}
