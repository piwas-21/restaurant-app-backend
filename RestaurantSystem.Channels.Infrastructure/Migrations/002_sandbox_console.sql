-- Additive migration for the isolated channels SANDBOX database only.
BEGIN;
CREATE TABLE channel_console_sessions (
    session_hash char(64) PRIMARY KEY,
    expires_at timestamptz NOT NULL
);
CREATE TABLE channel_authorization_states (
    state_hash char(64) PRIMARY KEY,
    session_hash char(64) NOT NULL REFERENCES channel_console_sessions(session_hash) ON DELETE CASCADE,
    store_id uuid NOT NULL,
    verifier_cipher text NOT NULL,
    enable_testing boolean NOT NULL,
    expires_at timestamptz NOT NULL,
    claimed_at timestamptz
);
CREATE TABLE channel_sandbox_tokens (
    client_id varchar(128) NOT NULL,
    store_id uuid NOT NULL,
    kind varchar(24) NOT NULL,
    token_cipher text NOT NULL,
    expires_at timestamptz NOT NULL,
    PRIMARY KEY (client_id, store_id, kind)
);
CREATE TABLE channel_sandbox_order_actions (
    client_id varchar(128) NOT NULL,
    store_id uuid NOT NULL,
    order_id uuid NOT NULL,
    action varchar(12) NOT NULL CHECK (action IN ('accept', 'deny')),
    state varchar(24) NOT NULL CHECK (state IN ('Pending', 'Succeeded', 'Failed', 'Unknown')),
    updated_at timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (client_id, store_id, order_id)
);
CREATE INDEX ix_channel_console_sessions_expiry ON channel_console_sessions (expires_at);
CREATE INDEX ix_channel_authorization_expiry ON channel_authorization_states (expires_at);
COMMIT;
