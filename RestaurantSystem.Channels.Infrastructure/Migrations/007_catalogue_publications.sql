-- Additive reviewed menu publication ledger, dedicated channels sandbox DB only.
BEGIN;
CREATE TABLE channel_catalogue_publications (
    sequence bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    id uuid NOT NULL UNIQUE,
    client_id varchar(128) NOT NULL,
    store_id uuid NOT NULL,
    tenant_id varchar(100) NOT NULL,
    catalogue_revision varchar(128) NOT NULL,
    mapping_hash char(64) NOT NULL CHECK (mapping_hash ~ '^[a-f0-9]{64}$'),
    source_revision char(64) NOT NULL CHECK (source_revision ~ '^[a-f0-9]{64}$'),
    publication_revision char(64) NOT NULL CHECK (publication_revision ~ '^[a-f0-9]{64}$'),
    menu jsonb NOT NULL CHECK (jsonb_typeof(menu) = 'object'),
    previous_menu jsonb NOT NULL CHECK (jsonb_typeof(previous_menu) = 'object'),
    state varchar(12) NOT NULL DEFAULT 'Pending' CHECK (state IN ('Pending', 'Verified', 'Abandoned')),
    provider_hash char(64) CHECK (provider_hash IS NULL OR provider_hash ~ '^[a-f0-9]{64}$'),
    verified_at timestamptz,
    created_at timestamptz NOT NULL DEFAULT now(),
    FOREIGN KEY (client_id, store_id, tenant_id) REFERENCES channel_availability_bindings(client_id, store_id, tenant_id),
    CHECK (state <> 'Verified' OR provider_hash IS NOT NULL AND verified_at IS NOT NULL)
);
CREATE INDEX ix_channel_catalogue_latest ON channel_catalogue_publications(client_id, store_id, tenant_id, sequence DESC);
COMMIT;
