-- Dedicated channels SANDBOX database only. Apply explicitly; previous migrations are immutable.
BEGIN;
CREATE TABLE channel_import_jobs (
    client_id varchar(128) NOT NULL,
    store_id uuid NOT NULL,
    order_id uuid NOT NULL,
    tenant_id varchar(100) NOT NULL,
    catalogue_revision varchar(128) NOT NULL,
    state varchar(24) NOT NULL DEFAULT 'Pending' CHECK (state IN ('Pending', 'Prepared', 'Imported', 'Quarantined')),
    lease_id uuid,
    lease_until timestamptz,
    attempts integer NOT NULL DEFAULT 0,
    available_at timestamptz NOT NULL DEFAULT now(),
    encrypted_request text,
    request_hash char(64),
    payload_expires_at timestamptz,
    tenant_order_id uuid,
    last_code varchar(40),
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (client_id, store_id, order_id),
    CHECK ((encrypted_request IS NULL) OR (request_hash IS NOT NULL AND payload_expires_at IS NOT NULL)),
    CHECK (state <> 'Imported' OR tenant_order_id IS NOT NULL)
);
CREATE INDEX ix_channel_import_available ON channel_import_jobs (state, available_at, created_at);
CREATE UNIQUE INDEX ix_channel_import_tenant_order ON channel_import_jobs (tenant_id, tenant_order_id) WHERE tenant_order_id IS NOT NULL;
COMMIT;
