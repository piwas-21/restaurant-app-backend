using Microsoft.EntityFrameworkCore.Migrations;

namespace RestaurantSystem.Infrastructure.Persistence.Migrations;

public partial class AddOrderBillingCredits
{
    private static void PrepareProvenLegacyCreditScopes(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        -- Automatic history repair is intentionally narrow: one undiscounted root and one void-only
        -- amendment. Other histories remain blocked for reviewed reconciliation, never guessed.
        CREATE TEMP TABLE proven_legacy_credit_scopes ON COMMIT DROP AS
        WITH sources AS (
          SELECT a.id, a.source_order_id, a.changes_json, i.id AS item_id, i.quantity,
            (o.total * 100)::bigint AS total_minor,
            a.financial_resolution_json -> 'potentialCreditMinor' AS claimed_credit
          FROM order_amendments a JOIN orders o ON o.id = a.source_order_id
            JOIN "OrderItems" i ON i.order_id = o.id AND i.parent_order_item_id IS NULL
          WHERE a.state = 'Committed' AND a.supplement_order_id IS NULL
            AND a.expected_order_version > 0 AND i.quantity > 0 AND i.item_total > 0
            AND o.total = o.sub_total AND o.total = i.item_total
            AND o.discount = 0 AND o.discount_percentage = 0 AND o.customer_discount_amount = 0
            AND (SELECT count(*) FROM "OrderItems" root
              WHERE root.order_id = o.id AND root.parent_order_item_id IS NULL) = 1
            AND (SELECT count(*) FROM order_amendments prior
              WHERE prior.source_order_id = o.id AND prior.state = 'Committed') = 1
            AND jsonb_typeof(a.source_snapshot_json) = 'object'
            AND a.source_snapshot_json ->> 'orderId' = o.id::text
            AND a.source_snapshot_json ->> 'serviceSessionId' = o.service_session_id::text
            AND a.source_snapshot_json ->> 'type' = 'DineIn'
            AND a.source_snapshot_json ->> 'currency' = a.financial_resolution_json ->> 'currency'
            AND a.source_snapshot_json -> 'total' = to_jsonb(o.total)
            AND a.source_snapshot_json -> 'version' = to_jsonb(a.expected_order_version)
            AND CASE WHEN jsonb_typeof(a.source_snapshot_json -> 'items') = 'array'
              THEN jsonb_array_length(a.source_snapshot_json -> 'items') = 1 ELSE false END
            AND a.source_snapshot_json -> 'items' -> 0 ->> 'id' = i.id::text
            AND a.source_snapshot_json -> 'items' -> 0 -> 'quantity' = to_jsonb(i.quantity)
            AND a.source_snapshot_json -> 'items' -> 0 -> 'itemTotal' = to_jsonb(i.item_total)
            AND CASE WHEN jsonb_typeof(a.changes_json) = 'array'
              THEN jsonb_array_length(a.changes_json) > 0 ELSE false END
            AND a.financial_resolution_json -> 'addedAmountMinor' = '0'::jsonb
        ), ranges AS (
          SELECT s.*, entry.change, entry.position,
            CASE WHEN jsonb_typeof(entry.change -> 'startOrdinal') = 'number'
              AND (entry.change ->> 'startOrdinal') ~ '^[1-9][0-9]{0,9}$'
              THEN (entry.change ->> 'startOrdinal')::numeric END AS start_ordinal,
            CASE WHEN jsonb_typeof(entry.change -> 'quantity') = 'number'
              AND (entry.change ->> 'quantity') ~ '^[1-9][0-9]{0,9}$'
              THEN (entry.change ->> 'quantity')::numeric END AS unit_count
          FROM sources s CROSS JOIN LATERAL jsonb_array_elements(
            CASE WHEN jsonb_typeof(s.changes_json) = 'array' THEN s.changes_json ELSE '[]'::jsonb END)
            WITH ORDINALITY entry(change, position)
        ), valid_ranges AS (
          SELECT r.*, floor(total_minor::numeric / quantity) * unit_count
            + greatest(0, least(start_ordinal + unit_count - 1, total_minor % quantity)
              - start_ordinal + 1) AS removed_minor
          FROM ranges r WHERE jsonb_typeof(change) = 'object'
            AND change ->> 'kind' = 'Void' AND change -> 'wholeLine' = 'false'::jsonb
            AND change ->> 'orderItemId' = item_id::text
            AND change -> 'previous' ->> 'id' = item_id::text
            AND change -> 'previous' -> 'quantity' = to_jsonb(unit_count)
            AND change -> 'current' = 'null'::jsonb
            AND start_ordinal <= quantity AND unit_count <= quantity
            AND start_ordinal + unit_count <= quantity + 1
        )
        SELECT s.id AS amendment_id FROM sources s
        JOIN valid_ranges r ON r.id = s.id
        GROUP BY s.id, s.changes_json, s.claimed_credit
        HAVING count(*) = jsonb_array_length(s.changes_json)
          AND to_jsonb(sum(r.removed_minor)) = s.claimed_credit
          AND NOT EXISTS (SELECT 1 FROM valid_ranges left_range JOIN valid_ranges right_range
            ON left_range.id = right_range.id AND left_range.position < right_range.position
            AND left_range.start_ordinal < right_range.start_ordinal + right_range.unit_count
            AND right_range.start_ordinal < left_range.start_ordinal + left_range.unit_count
            WHERE left_range.id = s.id);
        """);
}
