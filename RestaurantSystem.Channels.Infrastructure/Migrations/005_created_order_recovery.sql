-- Dedicated channels SANDBOX database only. Apply explicitly; previous migrations are immutable.
BEGIN;
ALTER TABLE channel_import_jobs ADD COLUMN discovery_source varchar(24) NOT NULL DEFAULT 'webhook'
    CHECK (discovery_source IN ('webhook', 'provider_poll'));
ALTER TABLE channel_import_jobs ADD COLUMN discovery_hash char(64);
ALTER TABLE channel_import_jobs ADD COLUMN recovery_enrolled_at timestamptz;
ALTER TABLE channel_import_jobs ADD CONSTRAINT ck_channel_import_discovery_evidence
    CHECK (discovery_source <> 'provider_poll' OR discovery_hash IS NOT NULL AND discovery_hash ~ '^[a-f0-9]{64}$'
      AND recovery_enrolled_at IS NOT NULL AND created_at >= recovery_enrolled_at);
COMMIT;
