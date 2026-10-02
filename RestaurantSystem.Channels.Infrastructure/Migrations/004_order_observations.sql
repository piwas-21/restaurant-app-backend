-- Dedicated channels SANDBOX database only. Additive state metadata; no customer payloads.
BEGIN;
CREATE TABLE channel_order_observations (
    client_id varchar(128) NOT NULL,
    store_id uuid NOT NULL,
    order_id uuid NOT NULL,
    tenant_id varchar(100) NOT NULL,
    tenant_order_id uuid NOT NULL,
    lease_id uuid,
    lease_until timestamptz,
    available_at timestamptz NOT NULL DEFAULT now(),
    canonical_state varchar(24),
    canonical_hash char(64),
    observed_at timestamptz,
    terminal boolean NOT NULL DEFAULT false,
    PRIMARY KEY (client_id, store_id, order_id),
    FOREIGN KEY (client_id, store_id, order_id) REFERENCES channel_import_jobs(client_id, store_id, order_id),
    CHECK (canonical_state IS NULL OR canonical_state IN ('CREATED','ACCEPTED','CANCELED','DENIED','FINISHED')),
    CHECK (canonical_hash IS NULL OR canonical_hash ~ '^[a-f0-9]{64}$'),
    CHECK (NOT terminal OR canonical_state IN ('CANCELED','DENIED','FINISHED'))
);
CREATE INDEX ix_channel_observations_available ON channel_order_observations (terminal, available_at);
COMMIT;
