using Microsoft.EntityFrameworkCore.Migrations;

namespace RestaurantSystem.Infrastructure.Persistence.Migrations;

public partial class AddOrderAmendmentLoyaltyCompensation
{
    private static void ProtectOrderBillingAwardJournal(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE FUNCTION order_billing_amendment_change_removes_unit(change_doc jsonb, target_item uuid, target_ordinal integer)
        RETURNS boolean LANGUAGE plpgsql IMMUTABLE AS $$
        DECLARE
          start_text text;
          quantity_text text;
          start_ordinal bigint;
          removal_quantity bigint;
        BEGIN
          IF jsonb_typeof(change_doc) IS DISTINCT FROM 'object'
             OR (change_doc ->> 'kind' IS DISTINCT FROM 'Void'
                 AND change_doc ->> 'kind' IS DISTINCT FROM 'Replace')
             OR change_doc ->> 'orderItemId' IS DISTINCT FROM target_item::text THEN
            RETURN FALSE;
          END IF;

          start_text := change_doc ->> 'startOrdinal';
          quantity_text := change_doc ->> 'quantity';
          IF start_text IS NULL OR quantity_text IS NULL
             OR start_text !~ '^[0-9]{1,10}$' OR quantity_text !~ '^[0-9]{1,10}$' THEN
            RETURN FALSE;
          END IF;
          start_ordinal := start_text::bigint;
          removal_quantity := quantity_text::bigint;
          RETURN start_ordinal > 0 AND removal_quantity > 0
            AND start_ordinal <= target_ordinal
            AND start_ordinal + removal_quantity > target_ordinal;
        END $$;

        CREATE FUNCTION validate_order_billing_award_witness_insert() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE
          frozen order_billing_snapshots%ROWTYPE;
          owner_link order_billing_snapshot_owner_links%ROWTYPE;
          accepted_owner uuid;
          recorded_suppression bigint;
        BEGIN
          SELECT * INTO frozen FROM order_billing_snapshots
          WHERE order_id = NEW.order_id FOR KEY SHARE;
          IF NOT FOUND OR frozen.earned_points_candidate IS NULL
             OR frozen.earned_points_candidate IS DISTINCT FROM NEW.candidate_points THEN
            RAISE EXCEPTION 'An award witness requires an evaluated frozen earning candidate' USING ERRCODE = '23514';
          END IF;

          SELECT * INTO owner_link FROM order_billing_snapshot_owner_links
          WHERE order_id = NEW.order_id AND id = NEW.owner_link_id AND slot = 'Earning'
          FOR KEY SHARE;
          IF NOT FOUND OR owner_link.disposition <> 'Linked' OR owner_link.user_id IS NULL
             OR owner_link.erased_at IS NOT NULL OR owner_link.erasure_transaction_id IS NOT NULL THEN
            RAISE EXCEPTION 'An award witness requires its linked earning owner' USING ERRCODE = '23514';
          END IF;

          SELECT user_id INTO accepted_owner FROM orders WHERE id = NEW.order_id FOR KEY SHARE;
          IF accepted_owner IS DISTINCT FROM owner_link.user_id OR NOT EXISTS (
              SELECT 1 FROM "Users" WHERE id = owner_link.user_id AND is_deleted = FALSE FOR KEY SHARE) THEN
            RAISE EXCEPTION 'An award witness owner must match the live accepted-order owner' USING ERRCODE = '23514';
          END IF;

          SELECT COALESCE(SUM(suppressed_earned_points), 0) INTO recorded_suppression
          FROM order_billing_unit_award_suppressions WHERE order_id = NEW.order_id;
          IF recorded_suppression IS DISTINCT FROM NEW.suppressed_points::bigint THEN
            RAISE EXCEPTION 'An award witness must equal its recorded pre-award suppression total' USING ERRCODE = '23514';
          END IF;
          IF NEW.created_by <> 'OrderBillingAwardBoundary' OR NEW.updated_at IS NOT NULL
             OR NEW.updated_by IS NOT NULL THEN
            RAISE EXCEPTION 'An award witness must use its immutable journal identity' USING ERRCODE = '23514';
          END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER validate_order_billing_award_witness_insert
          BEFORE INSERT ON order_billing_award_witnesses
          FOR EACH ROW EXECUTE FUNCTION validate_order_billing_award_witness_insert();

        CREATE FUNCTION validate_order_billing_unit_award_suppression_insert() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE
          frozen order_billing_snapshots%ROWTYPE;
          unit order_billing_snapshot_units%ROWTYPE;
          amendment order_amendments%ROWTYPE;
          matching_scopes bigint;
        BEGIN
          PERFORM 1 FROM orders WHERE id = NEW.order_id FOR UPDATE;
          IF NOT FOUND THEN
            RAISE EXCEPTION 'An award suppression requires its source order' USING ERRCODE = '23514';
          END IF;

          IF EXISTS (SELECT 1 FROM order_billing_award_witnesses WHERE order_id = NEW.order_id) THEN
            RAISE EXCEPTION 'A post-award removal requires compensation, not pre-award suppression' USING ERRCODE = '23514';
          END IF;
          SELECT * INTO frozen FROM order_billing_snapshots
          WHERE order_id = NEW.order_id FOR KEY SHARE;
          SELECT * INTO unit FROM order_billing_snapshot_units
          WHERE order_id = NEW.order_id AND id = NEW.snapshot_unit_id FOR KEY SHARE;
          SELECT * INTO amendment FROM order_amendments
          WHERE source_order_id = NEW.order_id AND id = NEW.amendment_id FOR SHARE;
          IF NOT FOUND OR frozen.earned_points_candidate IS NULL OR unit.id IS NULL
             OR unit.earned_points <= 0 OR unit.earned_points IS DISTINCT FROM NEW.suppressed_earned_points
             OR amendment.state <> 'Committed' OR amendment.created_by IS NULL
             OR NEW.created_by <> 'OrderBillingAwardSuppression' OR NEW.updated_at IS NOT NULL
             OR NEW.updated_by IS NOT NULL THEN
            RAISE EXCEPTION 'An award suppression requires a committed positive earning-unit removal' USING ERRCODE = '23514';
          END IF;

          SELECT COUNT(*) INTO matching_scopes
          FROM jsonb_array_elements(
              CASE WHEN jsonb_typeof(amendment.changes_json) = 'array'
                   THEN amendment.changes_json ELSE '[]'::jsonb END) AS change(value)
          WHERE order_billing_amendment_change_removes_unit(
              change.value, unit.order_item_id, unit.unit_ordinal);
          IF matching_scopes <> 1 THEN
            RAISE EXCEPTION 'An award suppression must match exactly one committed removal range' USING ERRCODE = '23514';
          END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER validate_order_billing_unit_award_suppression_insert
          BEFORE INSERT ON order_billing_unit_award_suppressions
          FOR EACH ROW EXECUTE FUNCTION validate_order_billing_unit_award_suppression_insert();

        CREATE FUNCTION protect_order_billing_award_suppression_mutation() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF TG_OP = 'DELETE' THEN
            RAISE EXCEPTION 'Award suppression history cannot be deleted' USING ERRCODE = '23514';
          END IF;
          IF NEW.id IS DISTINCT FROM OLD.id OR NEW.order_id IS DISTINCT FROM OLD.order_id
             OR NEW.snapshot_unit_id IS DISTINCT FROM OLD.snapshot_unit_id
             OR NEW.amendment_id IS DISTINCT FROM OLD.amendment_id
             OR NEW.suppressed_earned_points IS DISTINCT FROM OLD.suppressed_earned_points
             OR NEW.created_at IS DISTINCT FROM OLD.created_at
             OR NEW.updated_at IS NULL OR NEW.created_by IS DISTINCT FROM 'RetainedLoyaltyEvidenceScrubber'
             OR NEW.updated_by IS DISTINCT FROM 'RetainedLoyaltyEvidenceScrubber' THEN
            RAISE EXCEPTION 'Award suppression facts are immutable outside customer erasure' USING ERRCODE = '23514';
          END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER immutable_order_billing_unit_award_suppressions
          BEFORE UPDATE OR DELETE ON order_billing_unit_award_suppressions
          FOR EACH ROW EXECUTE FUNCTION protect_order_billing_award_suppression_mutation();

        CREATE FUNCTION validate_order_billing_award_unit_coverage_insert() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE
          unit order_billing_snapshot_units%ROWTYPE;
          witness order_billing_award_witnesses%ROWTYPE;
        BEGIN
          SELECT * INTO witness FROM order_billing_award_witnesses
          WHERE order_id = NEW.order_id AND id = NEW.award_witness_id FOR KEY SHARE;
          SELECT * INTO unit FROM order_billing_snapshot_units
          WHERE order_id = NEW.order_id AND id = NEW.snapshot_unit_id FOR KEY SHARE;
          IF witness.id IS NULL OR witness.outcome <> 'Awarded' OR unit.id IS NULL
             OR unit.earned_points <= 0 OR unit.earned_points IS DISTINCT FROM NEW.eligible_earned_points
             OR EXISTS (SELECT 1 FROM order_billing_unit_award_suppressions
                        WHERE order_id = NEW.order_id AND snapshot_unit_id = NEW.snapshot_unit_id)
             OR NEW.created_by <> 'OrderBillingAwardBoundary' OR NEW.updated_at IS NOT NULL
             OR NEW.updated_by IS NOT NULL THEN
            RAISE EXCEPTION 'Award-unit coverage must match one unsuppressed positive earning unit' USING ERRCODE = '23514';
          END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER validate_order_billing_award_unit_coverage_insert
          BEFORE INSERT ON order_billing_award_unit_coverages
          FOR EACH ROW EXECUTE FUNCTION validate_order_billing_award_unit_coverage_insert();

        CREATE FUNCTION reject_order_billing_award_journal_mutation() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          RAISE EXCEPTION 'Order billing award journals are immutable' USING ERRCODE = '23514';
        END $$;
        CREATE TRIGGER immutable_order_billing_award_witnesses
          BEFORE UPDATE OR DELETE ON order_billing_award_witnesses
          FOR EACH ROW EXECUTE FUNCTION reject_order_billing_award_journal_mutation();
        CREATE TRIGGER immutable_order_billing_award_unit_coverages
          BEFORE UPDATE OR DELETE ON order_billing_award_unit_coverages
          FOR EACH ROW EXECUTE FUNCTION reject_order_billing_award_journal_mutation();

        CREATE FUNCTION protect_order_billing_award_source_amendment() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF EXISTS (SELECT 1 FROM order_billing_unit_award_suppressions
                     WHERE order_id = OLD.source_order_id AND amendment_id = OLD.id)
             OR EXISTS (SELECT 1 FROM order_amendment_loyalty_compensations
                        WHERE source_order_id = OLD.source_order_id AND amendment_id = OLD.id) THEN
            IF TG_OP = 'DELETE' THEN
              RAISE EXCEPTION 'Amendment removal facts referenced by loyalty evidence are immutable' USING ERRCODE = '23514';
            END IF;
            IF NEW.id IS DISTINCT FROM OLD.id
               OR NEW.source_order_id IS DISTINCT FROM OLD.source_order_id
               OR NEW.service_session_id IS DISTINCT FROM OLD.service_session_id
               OR NEW.supplement_order_id IS DISTINCT FROM OLD.supplement_order_id
               OR NEW.client_operation_id IS DISTINCT FROM OLD.client_operation_id
               OR NEW.actor_user_id IS DISTINCT FROM OLD.actor_user_id
               OR NEW.actor_role IS DISTINCT FROM OLD.actor_role
               OR NEW.state IS DISTINCT FROM OLD.state
               OR NEW.payload_hash IS DISTINCT FROM OLD.payload_hash
               OR NEW.commit_payload_hash IS DISTINCT FROM OLD.commit_payload_hash
               OR NEW.expected_order_version IS DISTINCT FROM OLD.expected_order_version
               OR NEW.expected_account_revision IS DISTINCT FROM OLD.expected_account_revision
               OR NEW.committed_account_revision IS DISTINCT FROM OLD.committed_account_revision
               OR NEW.expires_at IS DISTINCT FROM OLD.expires_at
               OR NEW.committed_at IS DISTINCT FROM OLD.committed_at
               OR NEW.request_json IS DISTINCT FROM OLD.request_json
               OR NEW.changes_json IS DISTINCT FROM OLD.changes_json
               OR NEW.source_snapshot_json IS DISTINCT FROM OLD.source_snapshot_json
               OR NEW.supplement_snapshot_json IS DISTINCT FROM OLD.supplement_snapshot_json
               OR NEW.commit_result_json IS DISTINCT FROM OLD.commit_result_json
               OR NEW.created_at IS DISTINCT FROM OLD.created_at
               OR NEW.created_by IS DISTINCT FROM OLD.created_by THEN
              RAISE EXCEPTION 'Amendment removal facts referenced by loyalty evidence are immutable' USING ERRCODE = '23514';
            END IF;
            RETURN NEW;
          END IF;
          IF TG_OP = 'DELETE' THEN RETURN OLD; END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER immutable_order_billing_award_source_amendments
          BEFORE UPDATE OR DELETE ON order_amendments
          FOR EACH ROW EXECUTE FUNCTION protect_order_billing_award_source_amendment();

        CREATE FUNCTION verify_order_billing_award_witness_complete() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE
          witness order_billing_award_witnesses%ROWTYPE;
          frozen order_billing_snapshots%ROWTYPE;
          earned_count bigint;
        BEGIN
          SELECT * INTO witness FROM order_billing_award_witnesses WHERE id = NEW.id;
          IF NOT FOUND THEN RETURN NULL; END IF;
          SELECT * INTO frozen FROM order_billing_snapshots WHERE order_id = witness.order_id;
          IF NOT FOUND THEN
            RAISE EXCEPTION 'An award witness requires its frozen billing snapshot' USING ERRCODE = '23514';
          END IF;

          IF EXISTS (
              SELECT 1 FROM order_billing_snapshot_units unit
              WHERE unit.order_id = witness.order_id AND unit.earned_points > 0
                AND EXISTS (
                    SELECT 1 FROM order_amendments amendment
                    CROSS JOIN LATERAL jsonb_array_elements(
                        CASE WHEN jsonb_typeof(amendment.changes_json) = 'array'
                             THEN amendment.changes_json ELSE '[]'::jsonb END) AS change(value)
                    WHERE amendment.source_order_id = witness.order_id
                      AND amendment.state = 'Committed'
                      AND amendment.created_at <= witness.created_at
                      AND order_billing_amendment_change_removes_unit(
                          change.value, unit.order_item_id, unit.unit_ordinal))
                AND NOT EXISTS (
                    SELECT 1 FROM order_billing_unit_award_suppressions suppression
                    WHERE suppression.order_id = witness.order_id
                      AND suppression.snapshot_unit_id = unit.id)) THEN
            RAISE EXCEPTION 'Award witness requires complete pre-award removal coverage' USING ERRCODE = '23514';
          END IF;

          SELECT COUNT(*) INTO earned_count FROM fidelity_points_transactions
          WHERE order_id = witness.order_id AND transaction_type = 'Earned';
          IF witness.outcome = 'Awarded' THEN
            IF earned_count <> 1 OR NOT EXISTS (
                SELECT 1 FROM fidelity_points_transactions transaction
                WHERE transaction.id = witness.earned_transaction_id
                  AND transaction.order_id = witness.order_id
                  AND transaction.transaction_type = 'Earned'
                  AND transaction.points = witness.applied_points
                  AND transaction.order_total = frozen.earning_basis_minor::numeric / 100
                  AND transaction.user_id = (
                      SELECT user_id FROM order_billing_snapshot_owner_links
                      WHERE order_id = witness.order_id AND id = witness.owner_link_id
                        AND disposition = 'Linked')) THEN
              RAISE EXCEPTION 'An awarded witness requires exactly one matching earned row' USING ERRCODE = '23514';
            END IF;
            IF EXISTS (
                SELECT 1 FROM order_billing_snapshot_units unit
                WHERE unit.order_id = witness.order_id AND unit.earned_points > 0
                  AND NOT EXISTS (
                      SELECT 1 FROM order_billing_unit_award_suppressions suppression
                      WHERE suppression.order_id = witness.order_id
                        AND suppression.snapshot_unit_id = unit.id)
                  AND NOT EXISTS (
                      SELECT 1 FROM order_billing_award_unit_coverages coverage
                      WHERE coverage.order_id = witness.order_id
                        AND coverage.award_witness_id = witness.id
                        AND coverage.snapshot_unit_id = unit.id
                        AND coverage.eligible_earned_points = unit.earned_points))
               OR EXISTS (
                    SELECT 1 FROM order_billing_award_unit_coverages coverage
                    JOIN order_billing_snapshot_units unit
                      ON unit.order_id = coverage.order_id AND unit.id = coverage.snapshot_unit_id
                    WHERE coverage.order_id = witness.order_id AND coverage.award_witness_id = witness.id
                      AND (unit.earned_points <> coverage.eligible_earned_points
                           OR EXISTS (SELECT 1 FROM order_billing_unit_award_suppressions suppression
                                      WHERE suppression.order_id = witness.order_id
                                        AND suppression.snapshot_unit_id = unit.id)))
               OR COALESCE((SELECT SUM(eligible_earned_points) FROM order_billing_award_unit_coverages
                            WHERE order_id = witness.order_id AND award_witness_id = witness.id), 0)
                    <> witness.applied_points THEN
              RAISE EXCEPTION 'Award witness requires complete immutable per-unit award coverage' USING ERRCODE = '23514';
            END IF;
          ELSE
            IF earned_count <> 0 OR EXISTS (SELECT 1 FROM order_billing_award_unit_coverages
                WHERE order_id = witness.order_id AND award_witness_id = witness.id) THEN
              RAISE EXCEPTION 'A no-award witness cannot carry earned ledger or unit coverage' USING ERRCODE = '23514';
            END IF;
          END IF;
          RETURN NULL;
        END $$;
        CREATE CONSTRAINT TRIGGER verify_order_billing_award_witness_complete
          AFTER INSERT ON order_billing_award_witnesses
          DEFERRABLE INITIALLY DEFERRED
          FOR EACH ROW EXECUTE FUNCTION verify_order_billing_award_witness_complete();

        CREATE FUNCTION protect_order_billing_awarded_transaction() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF EXISTS (SELECT 1 FROM order_billing_award_witnesses
                     WHERE earned_transaction_id = OLD.id) THEN
            IF TG_OP = 'DELETE' THEN
              RAISE EXCEPTION 'A witnessed earned movement must be retained' USING ERRCODE = '23514';
            END IF;
            IF NEW.id IS DISTINCT FROM OLD.id OR NEW.order_id IS DISTINCT FROM OLD.order_id
               OR NEW.transaction_type IS DISTINCT FROM OLD.transaction_type
               OR NEW.points IS DISTINCT FROM OLD.points
               OR NEW.original_transaction_id IS DISTINCT FROM OLD.original_transaction_id
               OR NEW.order_total IS DISTINCT FROM OLD.order_total
               OR NEW.expires_at IS DISTINCT FROM OLD.expires_at
               OR NEW.created_at IS DISTINCT FROM OLD.created_at THEN
              RAISE EXCEPTION 'A witnessed earned movement financial fact is immutable' USING ERRCODE = '23514';
            END IF;
            IF NEW.user_id IS DISTINCT FROM OLD.user_id OR NEW.description IS DISTINCT FROM OLD.description
               OR NEW.created_by IS DISTINCT FROM OLD.created_by OR NEW.updated_at IS DISTINCT FROM OLD.updated_at
               OR NEW.updated_by IS DISTINCT FROM OLD.updated_by THEN
              IF OLD.user_id IS NULL OR NEW.user_id IS NOT NULL
                 OR NEW.description IS DISTINCT FROM '[erased]'
                 OR NEW.created_by IS DISTINCT FROM 'RetainedLoyaltyEvidenceScrubber'
                 OR NEW.updated_by IS DISTINCT FROM 'RetainedLoyaltyEvidenceScrubber' OR NEW.updated_at IS NULL
                 OR NOT EXISTS (
                     SELECT 1 FROM order_billing_award_witnesses witness
                     JOIN order_billing_snapshot_owner_links owner_link
                       ON owner_link.order_id = witness.order_id AND owner_link.id = witness.owner_link_id
                     WHERE witness.earned_transaction_id = OLD.id
                       AND owner_link.disposition = 'Linked' AND owner_link.user_id = OLD.user_id) THEN
                RAISE EXCEPTION 'A witnessed earned movement can only be anonymized by customer erasure' USING ERRCODE = '23514';
              END IF;
            END IF;
          END IF;
          IF TG_OP = 'DELETE' THEN
            RETURN OLD;
          END IF;
          RETURN NEW;
        END $$;
        CREATE TRIGGER protect_order_billing_awarded_transactions
          BEFORE UPDATE OR DELETE ON fidelity_points_transactions
          FOR EACH ROW EXECUTE FUNCTION protect_order_billing_awarded_transaction();

        CREATE FUNCTION verify_order_billing_awarded_transaction_erasure() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE
          owner_link order_billing_snapshot_owner_links%ROWTYPE;
        BEGIN
          IF OLD.user_id IS NOT NULL AND NEW.user_id IS NULL AND EXISTS (
              SELECT 1 FROM order_billing_award_witnesses
              WHERE earned_transaction_id = NEW.id) THEN
            SELECT link.* INTO owner_link
            FROM order_billing_award_witnesses witness
            JOIN order_billing_snapshot_owner_links link
              ON link.order_id = witness.order_id AND link.id = witness.owner_link_id
            WHERE witness.earned_transaction_id = NEW.id;
            IF NOT FOUND OR owner_link.disposition <> 'Erased' OR owner_link.user_id IS NOT NULL
               OR owner_link.erased_at IS DISTINCT FROM transaction_timestamp()
               OR owner_link.erasure_transaction_id IS DISTINCT FROM pg_current_xact_id()::text
               OR EXISTS (SELECT 1 FROM "Users" WHERE id = OLD.user_id) THEN
              RAISE EXCEPTION 'A witnessed earned movement can be anonymized only with its owner erasure' USING ERRCODE = '23514';
            END IF;
          END IF;
          RETURN NULL;
        END $$;
        CREATE CONSTRAINT TRIGGER verify_order_billing_awarded_transaction_erasure
          AFTER UPDATE ON fidelity_points_transactions
          DEFERRABLE INITIALLY DEFERRED
          FOR EACH ROW EXECUTE FUNCTION verify_order_billing_awarded_transaction_erasure();

        CREATE FUNCTION verify_native_earned_transaction_has_witness() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF NEW.transaction_type = 'Earned'
             AND EXISTS (SELECT 1 FROM order_billing_snapshots WHERE order_id = NEW.order_id)
             AND NOT EXISTS (SELECT 1 FROM order_billing_award_witnesses
                             WHERE order_id = NEW.order_id AND earned_transaction_id = NEW.id) THEN
            RAISE EXCEPTION 'A native order earning requires its matching immutable award witness' USING ERRCODE = '23514';
          END IF;
          RETURN NULL;
        END $$;
        CREATE CONSTRAINT TRIGGER verify_native_earned_transaction_has_witness
          AFTER INSERT ON fidelity_points_transactions
          DEFERRABLE INITIALLY DEFERRED
          FOR EACH ROW EXECUTE FUNCTION verify_native_earned_transaction_has_witness();
        """);

    private static void RemoveOrderBillingAwardJournalProtection(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DROP TRIGGER verify_order_billing_awarded_transaction_erasure ON fidelity_points_transactions;
        DROP TRIGGER verify_native_earned_transaction_has_witness ON fidelity_points_transactions;
        DROP TRIGGER protect_order_billing_awarded_transactions ON fidelity_points_transactions;
        DROP TRIGGER verify_order_billing_award_witness_complete ON order_billing_award_witnesses;
        DROP TRIGGER immutable_order_billing_award_source_amendments ON order_amendments;
        DROP TRIGGER immutable_order_billing_award_unit_coverages ON order_billing_award_unit_coverages;
        DROP TRIGGER immutable_order_billing_award_witnesses ON order_billing_award_witnesses;
        DROP TRIGGER validate_order_billing_award_unit_coverage_insert ON order_billing_award_unit_coverages;
        DROP TRIGGER immutable_order_billing_unit_award_suppressions ON order_billing_unit_award_suppressions;
        DROP TRIGGER validate_order_billing_unit_award_suppression_insert ON order_billing_unit_award_suppressions;
        DROP TRIGGER validate_order_billing_award_witness_insert ON order_billing_award_witnesses;
        DROP FUNCTION verify_order_billing_awarded_transaction_erasure();
        DROP FUNCTION verify_native_earned_transaction_has_witness();
        DROP FUNCTION protect_order_billing_awarded_transaction();
        DROP FUNCTION verify_order_billing_award_witness_complete();
        DROP FUNCTION protect_order_billing_award_source_amendment();
        DROP FUNCTION reject_order_billing_award_journal_mutation();
        DROP FUNCTION validate_order_billing_award_unit_coverage_insert();
        DROP FUNCTION protect_order_billing_award_suppression_mutation();
        DROP FUNCTION validate_order_billing_unit_award_suppression_insert();
        DROP FUNCTION validate_order_billing_award_witness_insert();
        DROP FUNCTION order_billing_amendment_change_removes_unit(jsonb, uuid, integer);
        """);
}
