using Microsoft.EntityFrameworkCore.Migrations;

namespace RestaurantSystem.Infrastructure.Persistence.Migrations;

public partial class AddTableOccupancyRecoveryAndEqualShareRounding
{
    private const string ProtectTableOccupancyRecoveryHistory = """
        CREATE FUNCTION reject_table_occupancy_recovery_mutation() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
          RAISE EXCEPTION 'Table occupancy recovery audit history must be retained'
            USING ERRCODE = '23514';
        END;
        $$;

        CREATE TRIGGER table_occupancy_recovery_operations_immutable
        BEFORE UPDATE OR DELETE ON table_occupancy_recovery_operations
        FOR EACH ROW EXECUTE FUNCTION reject_table_occupancy_recovery_mutation();
        CREATE TRIGGER table_occupancy_recovery_operations_no_truncate
        BEFORE TRUNCATE ON table_occupancy_recovery_operations
        FOR EACH STATEMENT EXECUTE FUNCTION reject_table_occupancy_recovery_mutation();

        CREATE TRIGGER table_occupancy_recovery_dispositions_immutable
        BEFORE UPDATE OR DELETE ON table_occupancy_recovery_dispositions
        FOR EACH ROW EXECUTE FUNCTION reject_table_occupancy_recovery_mutation();
        CREATE TRIGGER table_occupancy_recovery_dispositions_no_truncate
        BEFORE TRUNCATE ON table_occupancy_recovery_dispositions
        FOR EACH STATEMENT EXECUTE FUNCTION reject_table_occupancy_recovery_mutation();
        """;

    private const string RequireUnusedTableOccupancyRecoveryHistory = """
        LOCK TABLE table_occupancy_recovery_dispositions,
          table_occupancy_recovery_operations, account_equal_share_plans IN ACCESS EXCLUSIVE MODE;
        DO $guard$
        BEGIN
          IF EXISTS (SELECT 1 FROM table_occupancy_recovery_dispositions)
            OR EXISTS (SELECT 1 FROM table_occupancy_recovery_operations) THEN
            RAISE EXCEPTION 'Table occupancy recovery audit history must be retained'
              USING ERRCODE = '23514';
          END IF;
          IF EXISTS (SELECT 1 FROM account_equal_share_plans WHERE rounding_increment_minor <> 1) THEN
            RAISE EXCEPTION 'Equal share rounding history must be retained; use a forward migration'
              USING ERRCODE = '23514';
          END IF;
        END;
        $guard$;
        """;
}
