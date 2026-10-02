-- Dedicated channels sandbox database only. Apply explicitly before opting into stock synchronization.
BEGIN;
CREATE TABLE IF NOT EXISTS channel_availability_bindings (
    client_id varchar(128) NOT NULL,
    store_id uuid NOT NULL,
    tenant_id varchar(100) NOT NULL,
    PRIMARY KEY (client_id, store_id),
    UNIQUE (client_id, store_id, tenant_id)
);
CREATE TABLE IF NOT EXISTS channel_availability_states (
    client_id varchar(128) NOT NULL,
    store_id uuid NOT NULL,
    tenant_id varchar(100) NOT NULL,
    catalogue_revision varchar(128) NOT NULL,
    provider_item_id varchar(128) NOT NULL,
    source_revision char(64) NOT NULL CHECK (source_revision ~ '^[a-f0-9]{64}$'),
    desired_available boolean NOT NULL,
    source_reason varchar(32) NOT NULL,
    state varchar(24) NOT NULL DEFAULT 'Pending'
        CHECK (state IN ('Pending', 'Verified', 'Uncertain', 'Mismatch', 'ContractRejected')),
    observed_available boolean,
    provider_hash char(64) CHECK (provider_hash IS NULL OR provider_hash ~ '^[a-f0-9]{64}$'),
    verified_at timestamptz,
    updated_at timestamptz NOT NULL,
    PRIMARY KEY (client_id, store_id, catalogue_revision, provider_item_id),
    FOREIGN KEY (client_id, store_id, tenant_id) REFERENCES channel_availability_bindings (client_id, store_id, tenant_id),
    CHECK (state <> 'Verified' OR provider_hash IS NOT NULL AND verified_at IS NOT NULL
        AND observed_available IS NOT NULL AND observed_available = desired_available)
);
COMMIT;
