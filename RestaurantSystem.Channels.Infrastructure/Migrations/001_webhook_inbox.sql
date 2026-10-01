-- Apply once to the dedicated channels SANDBOX database before starting the gateway.
-- No automatic schema changes on API startup; never apply to a tenant/control-plane DB.
BEGIN;
CREATE TABLE channel_webhook_receipts (
    client_id varchar(128) NOT NULL,
    event_id varchar(128) NOT NULL,
    event_type varchar(128) NOT NULL,
    store_id uuid NOT NULL,
    resource_id varchar(128),
    event_time bigint NOT NULL CHECK (event_time > 0),
    body_hash char(64) NOT NULL,
    received_at timestamptz NOT NULL,
    state varchar(24) NOT NULL DEFAULT 'Received',
    PRIMARY KEY (client_id, event_id)
);
CREATE INDEX ix_channel_receipts_received ON channel_webhook_receipts (received_at);
CREATE INDEX ix_channel_receipts_store ON channel_webhook_receipts (store_id, received_at);
COMMIT;
