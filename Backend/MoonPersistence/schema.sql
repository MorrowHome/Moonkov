-- Version 1: stash belongs to a profile; raid receipts are immutable and idempotent.
CREATE TABLE IF NOT EXISTS schema_version (version integer PRIMARY KEY);
CREATE TABLE IF NOT EXISTS players (
    id uuid PRIMARY KEY,
    guest_token_hash text NOT NULL UNIQUE,
    display_name text NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now()
);
CREATE TABLE IF NOT EXISTS stashes (
    player_id uuid PRIMARY KEY REFERENCES players(id),
    dust integer NOT NULL DEFAULT 0 CHECK (dust >= 0),
    alloy integer NOT NULL DEFAULT 0 CHECK (alloy >= 0),
    cells integer NOT NULL DEFAULT 0 CHECK (cells >= 0)
);
CREATE TABLE IF NOT EXISTS raid_settlements (
    id uuid PRIMARY KEY,
    player_id uuid NOT NULL REFERENCES players(id),
    outcome text NOT NULL CHECK (outcome IN ('Extracted', 'Dead', 'TimedOut')),
    dust integer NOT NULL CHECK (dust >= 0),
    alloy integer NOT NULL CHECK (alloy >= 0),
    cells integer NOT NULL CHECK (cells >= 0),
    created_at timestamptz NOT NULL DEFAULT now(),
    CHECK (dust + alloy + cells <= 12)
);
CREATE INDEX IF NOT EXISTS raid_settlements_player_time ON raid_settlements (player_id, created_at);
INSERT INTO schema_version VALUES (1) ON CONFLICT DO NOTHING;

-- Version 2: accounts and revocable opaque sessions. Existing guest stashes can be claimed.
ALTER TABLE players ALTER COLUMN guest_token_hash DROP NOT NULL;
CREATE TABLE IF NOT EXISTS accounts (
    username_key text PRIMARY KEY,
    player_id uuid NOT NULL UNIQUE REFERENCES players(id),
    password_salt bytea NOT NULL,
    password_hash bytea NOT NULL,
    password_iterations integer NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now()
);
CREATE TABLE IF NOT EXISTS login_sessions (
    token_hash text PRIMARY KEY,
    player_id uuid NOT NULL REFERENCES players(id),
    expires_at timestamptz NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX IF NOT EXISTS login_sessions_expiry ON login_sessions (expires_at);
INSERT INTO schema_version VALUES (2) ON CONFLICT DO NOTHING;
