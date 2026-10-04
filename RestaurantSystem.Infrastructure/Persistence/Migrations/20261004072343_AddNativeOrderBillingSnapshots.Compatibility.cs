using Microsoft.EntityFrameworkCore.Migrations;

namespace RestaurantSystem.Infrastructure.Persistence.Migrations;

public partial class AddNativeOrderBillingSnapshots
{
    // These CHECK expressions are deliberately migration-local. Runtime configuration may evolve;
    // an already-authored migration must always create the same accepted schema.
    private const string HeaderConstraintSqlAtMigration = """
        created_by = 'OrderBillingSnapshotFactory' AND updated_by IS NULL
        AND currency ~ '^[A-Z]{3}$' AND tax_minor = 0 AND tax_rate_basis_points = 0
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
        AND ((earned_points_candidate IS NULL
        AND earning_evaluation_version IS NULL AND earning_rule_set_fingerprint IS NULL
        AND earning_rule_id IS NULL AND earning_rule_name IS NULL AND earning_rule_minimum_minor IS NULL
        AND earning_rule_maximum_minor IS NULL AND earning_rule_points IS NULL AND earning_rule_priority IS NULL)
        OR (earned_points_candidate IS NOT NULL
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
        AND ((redemption_transaction_id IS NULL AND redemption_transaction_type IS NULL
        AND redemption_transaction_points IS NULL AND redemption_transaction_order_total IS NULL
        AND redemption_transaction_created_at IS NULL AND redeemed_points = 0
        AND redemption_discount_minor = 0) OR (redemption_transaction_id IS NOT NULL
        AND redemption_transaction_type IS NOT NULL AND redemption_transaction_type = 'Redeemed'
        AND redemption_transaction_points IS NOT NULL
        AND redemption_transaction_points = -redeemed_points
        AND redemption_transaction_order_total IS NULL AND redemption_transaction_created_at IS NOT NULL
        AND redeemed_points > 0
        -- Current fidelity value is 100 points per major unit; supported currencies all have two decimals.
        AND redemption_discount_minor = redeemed_points
        AND raw_redemption_discount_amount * 100 = redeemed_points))
        """;

    private const string OwnerLinkConstraintSqlAtMigration = """
        created_by = 'OrderBillingSnapshotFactory' AND updated_by IS NULL
        AND slot IN ('Earning', 'Redemption') AND
        ((disposition = 'Linked' AND user_id IS NOT NULL AND erased_at IS NULL
        AND erasure_transaction_id IS NULL)
        OR (disposition = 'Erased' AND user_id IS NULL AND erased_at IS NOT NULL
        AND erasure_transaction_id IS NOT NULL
        AND erasure_transaction_id ~ '^[0-9]{1,20}$'))
        """;

    private const string UnitConstraintSqlAtMigration = """
        created_by = 'OrderBillingSnapshotFactory' AND updated_by IS NULL
        AND unit_ordinal > 0 AND gross_food_minor >= 0 AND tax_minor = 0 AND tax_rate_basis_points = 0
        AND tax_category = 'none' AND tax_treatment = 'NotApplied' AND order_discount_minor >= 0
        AND customer_discount_minor >= 0 AND redeemed_points >= 0 AND redemption_discount_minor >= 0
        AND redemption_discount_minor = redeemed_points
        AND payable_food_minor >= 0 AND earned_points >= 0 AND earning_basis_minor = gross_food_minor
        AND food_reconciliation_minor = payable_food_minor - (gross_food_minor - tax_minor
        - order_discount_minor - customer_discount_minor + courtesy_rounding_minor - redemption_discount_minor)
        """;

    private static void ProtectOrderBillingSnapshotHistory(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE FUNCTION lock_order_billing_snapshot_source_before_insert() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          PERFORM 1 FROM orders WHERE id = NEW.order_id FOR UPDATE;
          IF NOT FOUND THEN
            RAISE EXCEPTION 'A billing snapshot requires its source order to exist in this transaction' USING ERRCODE = '23514';
          END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER lock_order_billing_snapshot_source
          BEFORE INSERT ON order_billing_snapshots
          FOR EACH ROW EXECUTE FUNCTION lock_order_billing_snapshot_source_before_insert();

        CREATE FUNCTION reject_order_billing_snapshot_mutation() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          RAISE EXCEPTION 'Native order billing snapshots are immutable' USING ERRCODE = '23514';
        END $$;
        CREATE TRIGGER immutable_order_billing_snapshots
          BEFORE UPDATE OR DELETE ON order_billing_snapshots
          FOR EACH ROW EXECUTE FUNCTION reject_order_billing_snapshot_mutation();
        CREATE TRIGGER immutable_order_billing_snapshot_units
          BEFORE UPDATE OR DELETE ON order_billing_snapshot_units
          FOR EACH ROW EXECUTE FUNCTION reject_order_billing_snapshot_mutation();

        CREATE FUNCTION validate_order_billing_snapshot_owner_link_insert() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE
          frozen order_billing_snapshots%ROWTYPE;
          order_owner_id uuid;
          source fidelity_points_transactions%ROWTYPE;
        BEGIN
          IF NEW.disposition <> 'Linked' OR NEW.user_id IS NULL OR NEW.erased_at IS NOT NULL
             OR NEW.erasure_transaction_id IS NOT NULL THEN
            RAISE EXCEPTION 'A snapshot owner link must start linked' USING ERRCODE = '23514';
          END IF;

          SELECT * INTO frozen FROM order_billing_snapshots
          WHERE order_id = NEW.order_id FOR KEY SHARE;
          IF NOT FOUND THEN
            RAISE EXCEPTION 'A snapshot owner link requires its immutable snapshot' USING ERRCODE = '23514';
          END IF;

          SELECT orders.user_id INTO order_owner_id FROM orders
          WHERE orders.id = NEW.order_id FOR SHARE;
          IF NOT FOUND OR order_owner_id IS DISTINCT FROM NEW.user_id THEN
            RAISE EXCEPTION 'A snapshot owner link must match the accepted order owner' USING ERRCODE = '23514';
          END IF;
          PERFORM 1 FROM "Users" WHERE id = NEW.user_id FOR KEY SHARE;
          IF NOT FOUND THEN
            RAISE EXCEPTION 'A snapshot owner link requires a live owner' USING ERRCODE = '23514';
          END IF;

          IF NEW.slot = 'Earning' THEN
            IF frozen.earned_points_candidate IS NULL THEN
              RAISE EXCEPTION 'An earning owner link requires an evaluated candidate' USING ERRCODE = '23514';
            END IF;
          ELSIF NEW.slot = 'Redemption' THEN
            IF frozen.redemption_transaction_id IS NULL THEN
              RAISE EXCEPTION 'A redemption owner link requires redemption evidence' USING ERRCODE = '23514';
            END IF;

            SELECT * INTO source FROM fidelity_points_transactions
            WHERE id = frozen.redemption_transaction_id FOR SHARE;
            IF NOT FOUND OR source.id IS DISTINCT FROM frozen.redemption_transaction_id
               OR source.user_id IS DISTINCT FROM NEW.user_id
               OR source.order_id IS DISTINCT FROM NEW.order_id
               OR source.transaction_type::text IS DISTINCT FROM frozen.redemption_transaction_type
               OR source.points IS DISTINCT FROM frozen.redemption_transaction_points
               OR source.order_total IS DISTINCT FROM frozen.redemption_transaction_order_total
               OR source.created_at IS DISTINCT FROM frozen.redemption_transaction_created_at
               OR source.transaction_type <> 'Redeemed' OR source.points >= 0 OR source.order_total IS NOT NULL THEN
              RAISE EXCEPTION 'The redemption owner link does not match the exact source debit' USING ERRCODE = '23514';
            END IF;
          ELSE
            RAISE EXCEPTION 'Unknown snapshot owner slot' USING ERRCODE = '23514';
          END IF;

          RETURN NEW;
        END $$;
        CREATE TRIGGER validate_order_billing_snapshot_owner_link_insert
          BEFORE INSERT ON order_billing_snapshot_owner_links
          FOR EACH ROW EXECUTE FUNCTION validate_order_billing_snapshot_owner_link_insert();

        CREATE FUNCTION protect_order_billing_snapshot_owner_link_mutation() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF TG_OP = 'DELETE' THEN
            RAISE EXCEPTION 'Snapshot owner links cannot be deleted' USING ERRCODE = '23514';
          END IF;
          IF pg_trigger_depth() < 2
             OR OLD.disposition <> 'Linked' OR OLD.user_id IS NULL OR OLD.erased_at IS NOT NULL
             OR OLD.erasure_transaction_id IS NOT NULL
             OR NEW.id IS DISTINCT FROM OLD.id OR NEW.order_id IS DISTINCT FROM OLD.order_id
             OR NEW.slot IS DISTINCT FROM OLD.slot OR NEW.created_at IS DISTINCT FROM OLD.created_at
             OR NEW.created_by IS DISTINCT FROM OLD.created_by
             OR NEW.updated_at IS DISTINCT FROM OLD.updated_at
             OR NEW.updated_by IS DISTINCT FROM OLD.updated_by
             OR NEW.disposition <> 'Erased' OR NEW.user_id IS NOT NULL
             OR NEW.erased_at IS DISTINCT FROM transaction_timestamp()
             OR NEW.erasure_transaction_id IS DISTINCT FROM pg_current_xact_id()::text THEN
            RAISE EXCEPTION 'Snapshot owner links only change during the matching user erasure' USING ERRCODE = '23514';
          END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER immutable_order_billing_snapshot_owner_links
          BEFORE UPDATE OR DELETE ON order_billing_snapshot_owner_links
          FOR EACH ROW EXECUTE FUNCTION protect_order_billing_snapshot_owner_link_mutation();

        CREATE FUNCTION erase_order_billing_snapshot_owner_links_before_user_delete() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          UPDATE order_billing_snapshot_owner_links
          SET user_id = NULL,
              disposition = 'Erased',
              erased_at = transaction_timestamp(),
              erasure_transaction_id = pg_current_xact_id()::text
          WHERE user_id = OLD.id AND disposition = 'Linked';
          RETURN OLD;
        END $$;
        CREATE TRIGGER erase_order_billing_snapshot_owner_links_before_user_delete
          BEFORE DELETE ON "Users"
          FOR EACH ROW EXECUTE FUNCTION erase_order_billing_snapshot_owner_links_before_user_delete();

        CREATE FUNCTION verify_order_billing_snapshot_owner_after_order_update() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF NEW.user_id IS NOT DISTINCT FROM OLD.user_id THEN
            RETURN NULL;
          END IF;
          IF EXISTS (
              SELECT 1
              FROM order_billing_snapshot_owner_links link
              WHERE link.order_id = NEW.id
                AND ((link.disposition = 'Linked'
                      AND (NEW.user_id IS NULL OR link.user_id IS DISTINCT FROM NEW.user_id))
                  OR (link.disposition = 'Erased'
                      AND (NEW.user_id IS NOT NULL
                        OR link.erasure_transaction_id IS DISTINCT FROM pg_current_xact_id()::text)))) THEN
            RAISE EXCEPTION 'A snapshot owner can be unlinked only by its same-transaction user erasure' USING ERRCODE = '23514';
          END IF;
          RETURN NULL;
        END $$;
        CREATE CONSTRAINT TRIGGER verify_order_billing_snapshot_owner_after_order_update
          AFTER UPDATE OF user_id ON orders
          DEFERRABLE INITIALLY DEFERRED
          FOR EACH ROW EXECUTE FUNCTION verify_order_billing_snapshot_owner_after_order_update();

        CREATE FUNCTION protect_order_billing_snapshot_source_facts() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF EXISTS (SELECT 1 FROM order_billing_snapshots WHERE order_id = OLD.id)
             AND (NEW.sub_total IS DISTINCT FROM OLD.sub_total
                  OR NEW.tax IS DISTINCT FROM OLD.tax
                  OR NEW.delivery_fee IS DISTINCT FROM OLD.delivery_fee
                  OR NEW.discount IS DISTINCT FROM OLD.discount
                  OR NEW.customer_discount_amount IS DISTINCT FROM OLD.customer_discount_amount
                  OR NEW.fidelity_points_discount IS DISTINCT FROM OLD.fidelity_points_discount
                  OR NEW.fidelity_points_redeemed IS DISTINCT FROM OLD.fidelity_points_redeemed
                  OR NEW.tip IS DISTINCT FROM OLD.tip
                  OR NEW.total IS DISTINCT FROM OLD.total
                  OR NEW.fidelity_points_earned IS DISTINCT FROM OLD.fidelity_points_earned) THEN
            RAISE EXCEPTION 'Accepted order billing facts are immutable after snapshot creation' USING ERRCODE = '23514';
          END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER immutable_order_billing_snapshot_source_facts
          BEFORE UPDATE ON orders
          FOR EACH ROW EXECUTE FUNCTION protect_order_billing_snapshot_source_facts();

        CREATE FUNCTION protect_order_billing_snapshot_unit_insert() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE
          source_item "OrderItems"%ROWTYPE;
          source_minor bigint;
          expected_minor bigint;
        BEGIN
          -- The snapshot header and graph-mutation triggers serialize through the parent Order row.
          -- Avoid an item-row lock here: PostgreSQL takes UPDATE/DELETE tuple locks before row triggers,
          -- so taking an OrderItem lock after the parent lock would invert that boundary.
          SELECT * INTO source_item FROM "OrderItems"
          WHERE id = NEW.order_item_id AND order_id = NEW.order_id;
          IF NOT FOUND OR source_item.parent_order_item_id IS NOT NULL
             OR NEW.unit_ordinal < 1 OR NEW.unit_ordinal > source_item.quantity THEN
            RAISE EXCEPTION 'Snapshot units must reference an in-range root item unit' USING ERRCODE = '23514';
          END IF;
          source_minor := round(source_item.item_total, 2) * 100;
          expected_minor := source_minor / source_item.quantity
              + CASE WHEN NEW.unit_ordinal <= source_minor % source_item.quantity THEN 1 ELSE 0 END;
          IF NEW.gross_food_minor <> expected_minor OR NEW.earning_basis_minor <> expected_minor THEN
            RAISE EXCEPTION 'Snapshot unit gross and earning basis must match its frozen root item' USING ERRCODE = '23514';
          END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER validate_order_billing_snapshot_unit_insert
          BEFORE INSERT ON order_billing_snapshot_units
          FOR EACH ROW EXECUTE FUNCTION protect_order_billing_snapshot_unit_insert();

        CREATE FUNCTION protect_order_billing_snapshot_item_graph_insert_delete() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE
          source_order_id uuid;
        BEGIN
          source_order_id := CASE WHEN TG_OP = 'INSERT' THEN NEW.order_id ELSE OLD.order_id END;
          PERFORM 1 FROM orders WHERE id = source_order_id FOR UPDATE;
          IF NOT FOUND THEN
            RAISE EXCEPTION 'An order item graph change requires its source order' USING ERRCODE = '23514';
          END IF;
          IF EXISTS (SELECT 1 FROM order_billing_snapshots WHERE order_id = source_order_id) THEN
            RAISE EXCEPTION 'Order items cannot be added or removed after billing snapshot creation' USING ERRCODE = '23514';
          END IF;
          IF TG_OP = 'INSERT' THEN
            RETURN NEW;
          END IF;
          RETURN OLD;
        END $$;
        CREATE TRIGGER immutable_order_billing_snapshot_item_graph_insert_delete
          BEFORE INSERT OR DELETE ON "OrderItems"
          FOR EACH ROW EXECUTE FUNCTION protect_order_billing_snapshot_item_graph_insert_delete();

        CREATE FUNCTION protect_order_billing_snapshot_item_fact_update() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE
          affected_order_id uuid;
        BEGIN
          IF NEW.id IS NOT DISTINCT FROM OLD.id AND NEW.order_id IS NOT DISTINCT FROM OLD.order_id
             AND NEW.parent_order_item_id IS NOT DISTINCT FROM OLD.parent_order_item_id
             AND NEW.quantity IS NOT DISTINCT FROM OLD.quantity AND NEW.item_total IS NOT DISTINCT FROM OLD.item_total THEN
            RETURN NEW;
          END IF;

          -- Sort and deduplicate both parent IDs before locking, so a reparent never locks orders in opposite order.
          FOR affected_order_id IN
            SELECT candidate.order_id
            FROM (VALUES (OLD.order_id), (NEW.order_id)) AS candidate(order_id)
            GROUP BY candidate.order_id
            ORDER BY candidate.order_id
          LOOP
            PERFORM 1 FROM orders WHERE id = affected_order_id FOR UPDATE;
            IF NOT FOUND THEN
              RAISE EXCEPTION 'An order item graph change requires both source orders' USING ERRCODE = '23514';
            END IF;
          END LOOP;

          IF EXISTS (SELECT 1 FROM order_billing_snapshots WHERE order_id = OLD.order_id)
             OR EXISTS (SELECT 1 FROM order_billing_snapshots WHERE order_id = NEW.order_id) THEN
            RAISE EXCEPTION 'An item in a snapshotted order cannot change its accepted charge or root ownership' USING ERRCODE = '23514';
          END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER immutable_order_billing_snapshot_item_facts
          BEFORE UPDATE ON "OrderItems"
          FOR EACH ROW EXECUTE FUNCTION protect_order_billing_snapshot_item_fact_update();

        CREATE FUNCTION protect_fidelity_snapshot_redemption_update() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF EXISTS (SELECT 1 FROM order_billing_snapshots WHERE redemption_transaction_id = OLD.id)
             AND (NEW.id IS DISTINCT FROM OLD.id OR NEW.user_id IS DISTINCT FROM OLD.user_id
                  OR NEW.order_id IS DISTINCT FROM OLD.order_id
                  OR NEW.transaction_type IS DISTINCT FROM OLD.transaction_type
                  OR NEW.points IS DISTINCT FROM OLD.points
                  OR NEW.order_total IS DISTINCT FROM OLD.order_total
                  OR NEW.created_at IS DISTINCT FROM OLD.created_at) THEN
            RAISE EXCEPTION 'A redemption source referenced by a billing snapshot is immutable' USING ERRCODE = '23514';
          END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER immutable_fidelity_snapshot_redemption_facts
          BEFORE UPDATE ON fidelity_points_transactions
          FOR EACH ROW EXECUTE FUNCTION protect_fidelity_snapshot_redemption_update();

        CREATE FUNCTION verify_deleted_fidelity_snapshot_redemption() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE
          frozen order_billing_snapshots%ROWTYPE;
        BEGIN
          SELECT * INTO frozen FROM order_billing_snapshots
          WHERE redemption_transaction_id = OLD.id;
          IF NOT FOUND THEN
            RETURN NULL;
          END IF;
          IF OLD.order_id IS DISTINCT FROM frozen.order_id
             OR OLD.transaction_type::text IS DISTINCT FROM frozen.redemption_transaction_type
             OR OLD.points IS DISTINCT FROM frozen.redemption_transaction_points
             OR OLD.order_total IS DISTINCT FROM frozen.redemption_transaction_order_total
             OR OLD.created_at IS DISTINCT FROM frozen.redemption_transaction_created_at THEN
            RAISE EXCEPTION 'The deleted redemption row does not match its frozen source facts' USING ERRCODE = '23514';
          END IF;
          IF EXISTS (SELECT 1 FROM "Users" WHERE id = OLD.user_id)
             OR NOT EXISTS (
                 SELECT 1 FROM order_billing_snapshot_owner_links
                 WHERE order_id = frozen.order_id AND slot = 'Redemption'
                   AND disposition = 'Erased' AND user_id IS NULL AND erased_at IS NOT NULL
                   AND erasure_transaction_id = pg_current_xact_id()::text) THEN
            RAISE EXCEPTION 'A referenced redemption can be deleted only with its owner erasure' USING ERRCODE = '23514';
          END IF;
          RETURN NULL;
        END $$;
        CREATE CONSTRAINT TRIGGER verify_deleted_fidelity_snapshot_redemption
          AFTER DELETE ON fidelity_points_transactions
          DEFERRABLE INITIALLY DEFERRED
          FOR EACH ROW EXECUTE FUNCTION verify_deleted_fidelity_snapshot_redemption();

        CREATE FUNCTION verify_order_billing_snapshot_complete() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE
          frozen order_billing_snapshots%ROWTYPE;
          accepted_order orders%ROWTYPE;
          expected_units bigint;
          totals record;
          accepted_sale numeric;
          accepted_rounded_sale numeric;
        BEGIN
          SELECT * INTO frozen FROM order_billing_snapshots WHERE order_id = NEW.order_id;
          IF NOT FOUND THEN
            RETURN NULL;
          END IF;
          IF ((frozen.earned_points_candidate IS NOT NULL) <> EXISTS (
                SELECT 1 FROM order_billing_snapshot_owner_links
                WHERE order_id = frozen.order_id AND slot = 'Earning'))
             OR ((frozen.redemption_transaction_id IS NOT NULL) <> EXISTS (
                SELECT 1 FROM order_billing_snapshot_owner_links
                WHERE order_id = frozen.order_id AND slot = 'Redemption')) THEN
            RAISE EXCEPTION 'Snapshot owner slots do not match frozen loyalty evidence' USING ERRCODE = '23514';
          END IF;

          SELECT * INTO accepted_order FROM orders WHERE id = frozen.order_id FOR SHARE;
          IF NOT FOUND THEN
            RAISE EXCEPTION 'A billing snapshot requires its accepted native order' USING ERRCODE = '23514';
          END IF;
          accepted_sale := accepted_order.sub_total + accepted_order.tax + accepted_order.delivery_fee
              - frozen.raw_order_discount_amount - frozen.raw_customer_discount_amount;
          IF frozen.raw_order_discount_amount + frozen.raw_customer_discount_amount > 0 THEN
            IF accepted_sale - floor(accepted_sale) < 0.10 THEN
              accepted_rounded_sale := floor(accepted_sale);
            ELSE
              accepted_rounded_sale := ceil(accepted_sale);
            END IF;
          ELSE
            accepted_rounded_sale := round(accepted_sale, 2);
          END IF;
          IF frozen.raw_courtesy_rounding_amount IS DISTINCT FROM accepted_rounded_sale - accepted_sale
             OR frozen.total_minor <> round(
                  greatest(0::numeric, accepted_rounded_sale - accepted_order.fidelity_points_discount)
                  + greatest(0::numeric, accepted_order.tip), 2) * 100 THEN
            RAISE EXCEPTION 'Snapshot courtesy and accepted total do not match the native pricing formula' USING ERRCODE = '23514';
          END IF;
          IF frozen.gross_food_minor <> round(accepted_order.sub_total, 2) * 100
             OR frozen.tax_minor <> round(accepted_order.tax, 2) * 100
             OR frozen.delivery_fee_minor <> round(accepted_order.delivery_fee, 2) * 100
             OR frozen.order_discount_minor <> round(accepted_order.discount, 2) * 100
             OR frozen.customer_discount_minor <> round(accepted_order.customer_discount_amount, 2) * 100
             OR frozen.redemption_discount_minor <> round(accepted_order.fidelity_points_discount, 2) * 100
             OR frozen.redeemed_points <> accepted_order.fidelity_points_redeemed
             OR frozen.tip_minor <> round(accepted_order.tip, 2) * 100
             OR frozen.total_minor <> round(accepted_order.total, 2) * 100
             OR frozen.charged_delivery_fee_minor <> LEAST(
                  round(accepted_order.delivery_fee, 2) * 100,
                  frozen.total_minor - frozen.tip_minor)
             OR frozen.payable_food_minor <> frozen.total_minor - frozen.tip_minor
                  - frozen.charged_delivery_fee_minor
             OR COALESCE(frozen.earned_points_candidate, 0) <> accepted_order.fidelity_points_earned THEN
            RAISE EXCEPTION 'Snapshot evidence does not match the accepted order billing facts' USING ERRCODE = '23514';
          END IF;
          IF EXISTS (
              SELECT 1 FROM order_billing_snapshot_owner_links link
              WHERE link.order_id = frozen.order_id
                AND ((link.disposition = 'Linked' AND link.user_id IS DISTINCT FROM accepted_order.user_id)
                  OR (link.disposition = 'Erased' AND (accepted_order.user_id IS NOT NULL
                      OR link.erasure_transaction_id IS DISTINCT FROM pg_current_xact_id()::text)))) THEN
            RAISE EXCEPTION 'Snapshot owner state is not bound to the live order or current erasure' USING ERRCODE = '23514';
          END IF;

          SELECT COALESCE(SUM(quantity), 0) INTO expected_units FROM "OrderItems"
          WHERE order_id = frozen.order_id AND parent_order_item_id IS NULL;
          IF expected_units < 0 OR expected_units > 10000 THEN
            RAISE EXCEPTION 'The accepted item graph exceeds snapshot capacity' USING ERRCODE = '23514';
          END IF;

          IF EXISTS (
              SELECT 1 FROM (
                SELECT item.id, item.quantity, COUNT(unit.id) AS unit_count,
                       MIN(unit.unit_ordinal) AS first_ordinal, MAX(unit.unit_ordinal) AS last_ordinal,
                       COALESCE(SUM(unit.gross_food_minor), 0) AS line_gross
                FROM "OrderItems" item
                LEFT JOIN order_billing_snapshot_units unit
                  ON unit.order_id = item.order_id AND unit.order_item_id = item.id
                WHERE item.order_id = frozen.order_id AND item.parent_order_item_id IS NULL
                GROUP BY item.id, item.quantity, item.item_total
                HAVING item.quantity <= 0 OR COUNT(unit.id) <> item.quantity OR MIN(unit.unit_ordinal) <> 1
                    OR MAX(unit.unit_ordinal) <> item.quantity
                    OR COALESCE(SUM(unit.gross_food_minor), 0) <> round(item.item_total, 2) * 100
              ) invalid_root_units) THEN
            RAISE EXCEPTION 'Snapshot units do not completely conserve each accepted root item' USING ERRCODE = '23514';
          END IF;

          SELECT COUNT(*) AS row_count,
                 COALESCE(SUM(gross_food_minor), 0) AS gross_food,
                 COALESCE(SUM(tax_minor), 0) AS tax,
                 COALESCE(SUM(order_discount_minor), 0) AS order_discount,
                 COALESCE(SUM(customer_discount_minor), 0) AS customer_discount,
                 COALESCE(SUM(courtesy_rounding_minor), 0) AS courtesy,
                 COALESCE(SUM(redeemed_points), 0) AS redeemed_points,
                 COALESCE(SUM(redemption_discount_minor), 0) AS redemption_discount,
                 COALESCE(SUM(payable_food_minor), 0) AS payable_food,
                 COALESCE(SUM(food_reconciliation_minor), 0) AS food_reconciliation,
                 COALESCE(SUM(earning_basis_minor), 0) AS earning_basis,
                 COALESCE(SUM(earned_points), 0) AS earned_points
          INTO totals FROM order_billing_snapshot_units WHERE order_id = frozen.order_id;
          IF totals.row_count <> expected_units OR totals.row_count > 10000
             OR totals.gross_food <> frozen.gross_food_minor OR totals.tax <> frozen.tax_minor
             OR totals.order_discount <> frozen.order_discount_minor
             OR totals.customer_discount <> frozen.customer_discount_minor
             OR totals.courtesy <> frozen.courtesy_rounding_minor
             OR totals.redeemed_points <> frozen.redeemed_points
             OR totals.redemption_discount <> frozen.redemption_discount_minor
             OR totals.payable_food <> frozen.payable_food_minor
             OR totals.food_reconciliation <> frozen.food_reconciliation_minor
             OR totals.earning_basis <> frozen.earning_basis_minor
             OR totals.earned_points <> COALESCE(frozen.earned_points_candidate, 0) THEN
            RAISE EXCEPTION 'Snapshot unit rows do not conserve their accepted header' USING ERRCODE = '23514';
          END IF;
          RETURN NULL;
        END $$;
        CREATE CONSTRAINT TRIGGER verify_order_billing_snapshot_header_complete
          AFTER INSERT ON order_billing_snapshots
          DEFERRABLE INITIALLY DEFERRED
          FOR EACH ROW EXECUTE FUNCTION verify_order_billing_snapshot_complete();
        CREATE CONSTRAINT TRIGGER verify_order_billing_snapshot_owner_link_complete
          AFTER INSERT OR UPDATE ON order_billing_snapshot_owner_links
          DEFERRABLE INITIALLY DEFERRED
          FOR EACH ROW EXECUTE FUNCTION verify_order_billing_snapshot_complete();
        """);

    private static void RequireNoOrderBillingSnapshotHistory(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        LOCK TABLE order_billing_snapshot_owner_links, order_billing_snapshot_units, order_billing_snapshots IN ACCESS EXCLUSIVE MODE;
        DO $$ BEGIN
          IF EXISTS (SELECT 1 FROM order_billing_snapshot_owner_links)
            OR EXISTS (SELECT 1 FROM order_billing_snapshot_units)
            OR EXISTS (SELECT 1 FROM order_billing_snapshots) THEN
            RAISE EXCEPTION 'Native order billing snapshot history must be retained' USING ERRCODE = '23514';
          END IF;
        END $$;
        DROP TRIGGER verify_order_billing_snapshot_owner_link_complete ON order_billing_snapshot_owner_links;
        DROP TRIGGER verify_order_billing_snapshot_header_complete ON order_billing_snapshots;
        DROP TRIGGER lock_order_billing_snapshot_source ON order_billing_snapshots;
        DROP TRIGGER verify_order_billing_snapshot_owner_after_order_update ON orders;
        DROP TRIGGER immutable_order_billing_snapshot_source_facts ON orders;
        DROP TRIGGER verify_deleted_fidelity_snapshot_redemption ON fidelity_points_transactions;
        DROP TRIGGER immutable_fidelity_snapshot_redemption_facts ON fidelity_points_transactions;
        DROP TRIGGER immutable_order_billing_snapshot_item_facts ON "OrderItems";
        DROP TRIGGER immutable_order_billing_snapshot_item_graph_insert_delete ON "OrderItems";
        DROP TRIGGER validate_order_billing_snapshot_unit_insert ON order_billing_snapshot_units;
        DROP TRIGGER erase_order_billing_snapshot_owner_links_before_user_delete ON "Users";
        DROP TRIGGER immutable_order_billing_snapshot_owner_links ON order_billing_snapshot_owner_links;
        DROP TRIGGER validate_order_billing_snapshot_owner_link_insert ON order_billing_snapshot_owner_links;
        DROP TRIGGER immutable_order_billing_snapshot_units ON order_billing_snapshot_units;
        DROP TRIGGER immutable_order_billing_snapshots ON order_billing_snapshots;
        DROP FUNCTION verify_order_billing_snapshot_complete();
        DROP FUNCTION lock_order_billing_snapshot_source_before_insert();
        DROP FUNCTION verify_order_billing_snapshot_owner_after_order_update();
        DROP FUNCTION protect_order_billing_snapshot_source_facts();
        DROP FUNCTION verify_deleted_fidelity_snapshot_redemption();
        DROP FUNCTION protect_fidelity_snapshot_redemption_update();
        DROP FUNCTION protect_order_billing_snapshot_item_fact_update();
        DROP FUNCTION protect_order_billing_snapshot_item_graph_insert_delete();
        DROP FUNCTION protect_order_billing_snapshot_unit_insert();
        DROP FUNCTION erase_order_billing_snapshot_owner_links_before_user_delete();
        DROP FUNCTION protect_order_billing_snapshot_owner_link_mutation();
        DROP FUNCTION validate_order_billing_snapshot_owner_link_insert();
        DROP FUNCTION reject_order_billing_snapshot_mutation();
        """);
}
