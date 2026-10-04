namespace RestaurantSystem.Infrastructure.Persistence.Configurations;

internal static class OrderBillingSnapshotConstraintSql
{
    internal const string Header = """
        currency ~ '^[A-Z]{3}$' AND tax_minor = 0 AND tax_rate_basis_points = 0
        AND tax_category = 'none' AND tax_treatment = 'NotApplied'
        AND pricing_policy_version = 'native-zero-tax-v1'
        AND component_quantization_policy_version = 'currency-minor-2dp-away-from-zero-v1'
        AND earning_basis_policy_version = 'raw-root-item-total-v1'
        AND raw_tax_amount = 0 AND raw_order_discount_amount >= 0
        AND raw_customer_discount_amount >= 0 AND raw_redemption_discount_amount >= 0
        AND order_discount_minor = round(raw_order_discount_amount, 2) * 100
        AND customer_discount_minor = round(raw_customer_discount_amount, 2) * 100
        AND tax_minor = round(raw_tax_amount, 2) * 100
        AND redemption_discount_minor = round(raw_redemption_discount_amount, 2) * 100
        AND courtesy_rounding_minor = round(raw_courtesy_rounding_amount, 2) * 100
        AND gross_food_minor >= 0 AND delivery_fee_minor >= 0 AND charged_delivery_fee_minor >= 0
        AND charged_delivery_fee_minor <= delivery_fee_minor AND order_discount_minor >= 0
        AND customer_discount_minor >= 0 AND redeemed_points >= 0 AND redemption_discount_minor >= 0
        AND payable_food_minor >= 0 AND tip_minor >= 0 AND total_minor >= 0
        AND earning_basis_minor = gross_food_minor
        AND payable_food_minor + charged_delivery_fee_minor + tip_minor = total_minor
        AND food_reconciliation_minor = payable_food_minor - (gross_food_minor - tax_minor
        - order_discount_minor - customer_discount_minor + courtesy_rounding_minor - redemption_discount_minor)
        AND (earned_points_candidate IS NULL OR earned_points_candidate >= 0)
        AND ((earned_points_candidate IS NULL AND earning_user_id IS NULL
        AND earning_evaluation_version IS NULL AND earning_rule_set_fingerprint IS NULL
        AND earning_rule_id IS NULL AND earning_rule_name IS NULL AND earning_rule_minimum_minor IS NULL
        AND earning_rule_maximum_minor IS NULL AND earning_rule_points IS NULL AND earning_rule_priority IS NULL)
        OR (earned_points_candidate IS NOT NULL AND earning_user_id IS NOT NULL
        AND earning_evaluation_version IS NOT NULL AND earning_evaluation_version <> ''
        AND earning_rule_set_fingerprint IS NOT NULL
        AND earning_rule_set_fingerprint ~ '^[0-9a-f]{64}$'))
        AND ((earning_rule_id IS NULL AND earning_rule_name IS NULL
        AND earning_rule_minimum_minor IS NULL AND earning_rule_maximum_minor IS NULL
        AND earning_rule_points IS NULL AND earning_rule_priority IS NULL
        AND (earned_points_candidate IS NULL OR earned_points_candidate = 0))
        OR (earning_rule_id IS NOT NULL AND earning_rule_name IS NOT NULL
        AND earning_rule_minimum_minor IS NOT NULL AND earning_rule_minimum_minor >= 0
        AND earning_rule_points IS NOT NULL AND earning_rule_priority IS NOT NULL
        AND earning_rule_points = earned_points_candidate
        AND earning_rule_minimum_minor <= earning_basis_minor
        AND (earning_rule_maximum_minor IS NULL OR earning_rule_maximum_minor >= earning_basis_minor)))
        AND ((redemption_transaction_id IS NULL AND redemption_user_id IS NULL AND redeemed_points = 0
        AND redemption_discount_minor = 0) OR (redemption_transaction_id IS NOT NULL
        AND redemption_user_id IS NOT NULL AND redeemed_points > 0
        -- Current fidelity value is 100 points per major unit; supported currencies all have two decimals.
        AND redemption_discount_minor = redeemed_points
        AND raw_redemption_discount_amount * 100 = redeemed_points))
        """;

    internal const string Unit = """
        unit_ordinal > 0 AND gross_food_minor >= 0 AND tax_minor = 0 AND tax_rate_basis_points = 0
        AND tax_category = 'none' AND tax_treatment = 'NotApplied' AND order_discount_minor >= 0
        AND customer_discount_minor >= 0 AND redeemed_points >= 0 AND redemption_discount_minor >= 0
        AND redemption_discount_minor = redeemed_points
        AND payable_food_minor >= 0 AND earned_points >= 0 AND earning_basis_minor = gross_food_minor
        AND food_reconciliation_minor = payable_food_minor - (gross_food_minor - tax_minor
        - order_discount_minor - customer_discount_minor + courtesy_rounding_minor - redemption_discount_minor)
        """;
}
