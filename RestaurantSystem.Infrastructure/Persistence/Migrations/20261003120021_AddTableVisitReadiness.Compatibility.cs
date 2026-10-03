namespace RestaurantSystem.Infrastructure.Persistence.Migrations;

public partial class AddTableVisitReadiness
{
    private const string ProtectReadyOperationHistory = """
        CREATE FUNCTION prevent_table_ready_operations_mutation() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN RAISE EXCEPTION 'Table readiness operation history is immutable' USING ERRCODE = '23514'; END $$;
        CREATE TRIGGER immutable_table_ready_operations BEFORE UPDATE OR DELETE ON table_ready_operations
        FOR EACH ROW EXECUTE FUNCTION prevent_table_ready_operations_mutation();
        """;

    private const string RequireUnusedReadiness = """
        DO $$ BEGIN
            IF EXISTS (SELECT 1 FROM table_ready_operations)
                OR EXISTS (SELECT 1 FROM "Tables" WHERE readiness_version <> 1)
            THEN RAISE EXCEPTION 'Table readiness history must be retained' USING ERRCODE = '23514';
            END IF;
        END $$;
        DROP TRIGGER immutable_table_ready_operations ON table_ready_operations;
        DROP FUNCTION prevent_table_ready_operations_mutation();
        """;
}
