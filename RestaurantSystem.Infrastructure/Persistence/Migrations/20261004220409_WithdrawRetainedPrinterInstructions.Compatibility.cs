namespace RestaurantSystem.Infrastructure.Persistence.Migrations;

public partial class WithdrawRetainedPrinterInstructions
{
    private const string RetainedTextErasureUp = """
        CREATE FUNCTION erase_order_operational_text(value jsonb) RETURNS jsonb
          LANGUAGE plpgsql IMMUTABLE STRICT AS $$
        BEGIN
          IF jsonb_typeof(value) = 'object' THEN
            RETURN COALESCE((SELECT jsonb_object_agg(key,
              CASE WHEN lower(key) IN ('specialinstructions', 'reason', 'providerconsentnote')
                   THEN 'null'::jsonb ELSE erase_order_operational_text(item) END)
              FROM jsonb_each(value) AS property(key, item)), '{}'::jsonb);
          ELSIF jsonb_typeof(value) = 'array' THEN
            RETURN COALESCE((SELECT jsonb_agg(erase_order_operational_text(item) ORDER BY ordinal)
              FROM jsonb_array_elements(value) WITH ORDINALITY AS element(item, ordinal)), '[]'::jsonb);
          END IF;
          RETURN value;
        END $$;

        CREATE OR REPLACE FUNCTION protect_order_billing_award_source_amendment() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF EXISTS (SELECT 1 FROM order_billing_unit_award_suppressions
                     WHERE order_id = OLD.source_order_id AND amendment_id = OLD.id)
             OR EXISTS (SELECT 1 FROM order_amendment_loyalty_compensations
                        WHERE source_order_id = OLD.source_order_id AND amendment_id = OLD.id) THEN
            IF TG_OP = 'DELETE' THEN
              RAISE EXCEPTION 'Amendment removal facts referenced by loyalty evidence are immutable' USING ERRCODE = '23514';
            END IF;
            IF (to_jsonb(NEW) - ARRAY['request_json','changes_json','source_snapshot_json',
                 'supplement_snapshot_json','commit_result_json','financial_resolution_json','updated_at','updated_by'])
               IS DISTINCT FROM
               (to_jsonb(OLD) - ARRAY['request_json','changes_json','source_snapshot_json',
                 'supplement_snapshot_json','commit_result_json','financial_resolution_json','updated_at','updated_by']) THEN
              RAISE EXCEPTION 'Amendment removal facts referenced by loyalty evidence are immutable' USING ERRCODE = '23514';
            END IF;
            IF NEW.request_json IS DISTINCT FROM OLD.request_json OR
              NEW.changes_json IS DISTINCT FROM OLD.changes_json OR
              NEW.source_snapshot_json IS DISTINCT FROM OLD.source_snapshot_json OR
              NEW.supplement_snapshot_json IS DISTINCT FROM OLD.supplement_snapshot_json OR
              NEW.commit_result_json IS DISTINCT FROM OLD.commit_result_json THEN
              IF NEW.updated_by IS DISTINCT FROM 'RetainedOrderInstructionsScrubber'
                 OR NEW.financial_resolution_json IS DISTINCT FROM OLD.financial_resolution_json
                 OR NEW.request_json IS DISTINCT FROM erase_order_operational_text(OLD.request_json) OR
                  NEW.changes_json IS DISTINCT FROM erase_order_operational_text(OLD.changes_json) OR
                  NEW.source_snapshot_json IS DISTINCT FROM erase_order_operational_text(OLD.source_snapshot_json) OR
                  NEW.supplement_snapshot_json IS DISTINCT FROM erase_order_operational_text(OLD.supplement_snapshot_json) OR
                  NEW.commit_result_json IS DISTINCT FROM erase_order_operational_text(OLD.commit_result_json) THEN
                RAISE EXCEPTION 'Referenced amendment text can only be erased without changing its financial structure' USING ERRCODE = '23514';
              END IF;
            END IF;
            RETURN NEW;
          END IF;
          IF TG_OP = 'DELETE' THEN RETURN OLD; END IF;
          RETURN NEW;
        END $$;

        CREATE FUNCTION verify_retained_amendment_text_erasure() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          IF (NEW.request_json IS DISTINCT FROM OLD.request_json OR
              NEW.changes_json IS DISTINCT FROM OLD.changes_json OR
              NEW.source_snapshot_json IS DISTINCT FROM OLD.source_snapshot_json OR
              NEW.supplement_snapshot_json IS DISTINCT FROM OLD.supplement_snapshot_json OR
              NEW.commit_result_json IS DISTINCT FROM OLD.commit_result_json)
             AND (EXISTS (SELECT 1 FROM order_billing_unit_award_suppressions
                          WHERE order_id = NEW.source_order_id AND amendment_id = NEW.id)
                  OR EXISTS (SELECT 1 FROM order_amendment_loyalty_compensations
                             WHERE source_order_id = NEW.source_order_id AND amendment_id = NEW.id)) THEN
            IF NOT EXISTS (SELECT 1 FROM order_billing_snapshot_owner_links
                           WHERE order_id = NEW.source_order_id AND disposition = 'Erased'
                             AND user_id IS NULL AND erased_at = transaction_timestamp()
                             AND erasure_transaction_id = pg_current_xact_id()::text)
               OR EXISTS (SELECT 1 FROM orders WHERE id = NEW.source_order_id AND user_id IS NOT NULL) THEN
              RAISE EXCEPTION 'Referenced amendment text requires the same-transaction customer erasure' USING ERRCODE = '23514';
            END IF;
          END IF;
          RETURN NULL;
        END $$;
        CREATE CONSTRAINT TRIGGER verify_retained_amendment_text_erasure
          AFTER UPDATE ON order_amendments DEFERRABLE INITIALLY DEFERRED
          FOR EACH ROW EXECUTE FUNCTION verify_retained_amendment_text_erasure();
        """;

    private const string RetainedTextErasureDown = """
        DROP TRIGGER verify_retained_amendment_text_erasure ON order_amendments;
        DROP FUNCTION verify_retained_amendment_text_erasure();
        CREATE OR REPLACE FUNCTION protect_order_billing_award_source_amendment() RETURNS trigger LANGUAGE plpgsql AS $$
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
        DROP FUNCTION erase_order_operational_text(jsonb);
        """;
}
