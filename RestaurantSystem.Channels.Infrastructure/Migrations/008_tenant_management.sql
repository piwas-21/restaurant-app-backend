-- Tenant-scoped Uber sandbox management. Apply explicitly to the dedicated channels database.
BEGIN;
ALTER TABLE channel_catalogue_publications ADD COLUMN IF NOT EXISTS mapping_snapshot jsonb;

CREATE TABLE channel_tenant_oauth_flows (
    flow_id uuid PRIMARY KEY,
    client_id varchar(128) NOT NULL,
    store_id uuid NOT NULL,
    tenant_id varchar(100) NOT NULL,
    actor_id uuid NOT NULL,
    state_hash char(64) NOT NULL UNIQUE CHECK (state_hash ~ '^[a-f0-9]{64}$'),
    verifier_cipher text NOT NULL,
    enable_order_acceptance boolean NOT NULL DEFAULT false,
    status varchar(12) NOT NULL CHECK (status IN ('Pending', 'Processing', 'Connected', 'Failed', 'Expired')),
    error_code varchar(48),
    created_at timestamptz NOT NULL,
    expires_at timestamptz NOT NULL,
    completed_at timestamptz,
    FOREIGN KEY (client_id, store_id, tenant_id)
        REFERENCES channel_availability_bindings(client_id, store_id, tenant_id),
    CHECK (expires_at > created_at),
    CHECK (status <> 'Connected' OR completed_at IS NOT NULL AND error_code IS NULL)
);
CREATE INDEX ix_channel_tenant_oauth_tenant ON channel_tenant_oauth_flows(client_id, store_id, tenant_id, created_at DESC);

CREATE TABLE channel_catalogue_mapping_drafts (
    client_id varchar(128) NOT NULL,
    store_id uuid NOT NULL,
    tenant_id varchar(100) NOT NULL,
    draft_revision uuid NOT NULL,
    mapping_revision char(64) NOT NULL CHECK (mapping_revision ~ '^[a-f0-9]{64}$'),
    mapping_snapshot jsonb NOT NULL CHECK (jsonb_typeof(mapping_snapshot) = 'object'),
    actor_id uuid NOT NULL,
    updated_at timestamptz NOT NULL,
    PRIMARY KEY (client_id, store_id, tenant_id),
    FOREIGN KEY (client_id, store_id, tenant_id)
        REFERENCES channel_availability_bindings(client_id, store_id, tenant_id)
);

CREATE TABLE channel_availability_overrides (
    client_id varchar(128) NOT NULL,
    store_id uuid NOT NULL,
    tenant_id varchar(100) NOT NULL,
    paused boolean NOT NULL,
    paused_until timestamptz,
    actor_id uuid NOT NULL,
    updated_at timestamptz NOT NULL,
    PRIMARY KEY (client_id, store_id, tenant_id),
    FOREIGN KEY (client_id, store_id, tenant_id)
        REFERENCES channel_availability_bindings(client_id, store_id, tenant_id),
    CHECK (paused OR paused_until IS NULL)
);

CREATE TABLE channel_management_audit (
    sequence bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    client_id varchar(128) NOT NULL,
    store_id uuid NOT NULL,
    tenant_id varchar(100) NOT NULL,
    actor_id uuid NOT NULL,
    action varchar(40) NOT NULL,
    result_code varchar(48) NOT NULL,
    operation_id uuid,
    occurred_at timestamptz NOT NULL,
    FOREIGN KEY (client_id, store_id, tenant_id)
        REFERENCES channel_availability_bindings(client_id, store_id, tenant_id)
);
CREATE INDEX ix_channel_management_audit_tenant ON channel_management_audit(client_id, store_id, tenant_id, sequence DESC);

CREATE TABLE channel_management_connections (
    client_id varchar(128) NOT NULL,
    store_id uuid NOT NULL,
    tenant_id varchar(100) NOT NULL,
    disconnected boolean NOT NULL,
    actor_id uuid,
    updated_at timestamptz,
    PRIMARY KEY (client_id, store_id, tenant_id),
    FOREIGN KEY (client_id, store_id, tenant_id)
        REFERENCES channel_availability_bindings(client_id, store_id, tenant_id),
    CHECK ((actor_id IS NULL) = (updated_at IS NULL))
);
COMMIT;
