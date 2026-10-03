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

-- Version 3: stable stash stack IDs, item definitions, and atomic raid loadouts.
CREATE TABLE IF NOT EXISTS item_definitions (
    code text PRIMARY KEY, display_name text NOT NULL,
    width integer NOT NULL CHECK (width > 0), height integer NOT NULL CHECK (height > 0),
    stackable boolean NOT NULL DEFAULT true
);
INSERT INTO item_definitions (code, display_name, width, height) VALUES
    ('dust','Moon dust',2,2), ('alloy','Lunar alloy',2,1), ('cells','Energy cell',1,2)
ON CONFLICT (code) DO NOTHING;
CREATE TABLE IF NOT EXISTS inventory_stacks (
    id uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    player_id uuid NOT NULL REFERENCES players(id),
    item_code text NOT NULL REFERENCES item_definitions(code),
    quantity integer NOT NULL DEFAULT 0 CHECK (quantity >= 0),
    grid_x integer NOT NULL CHECK (grid_x >= 0 AND grid_x < 10),
    grid_y integer NOT NULL DEFAULT 0 CHECK (grid_y >= 0 AND grid_y < 24),
    rotated boolean NOT NULL DEFAULT false,
    UNIQUE (player_id, item_code)
);
DO $$ BEGIN
    IF NOT EXISTS (SELECT 1 FROM schema_version WHERE version=3) THEN
        INSERT INTO inventory_stacks (player_id,item_code,quantity,grid_x)
        SELECT s.player_id, v.code, v.quantity, v.x FROM stashes s
        CROSS JOIN LATERAL (VALUES ('dust',s.dust,0),('alloy',s.alloy,2),('cells',s.cells,4)) v(code,quantity,x);
        ALTER TABLE stashes RENAME TO legacy_stashes_v2;
        -- Existing account/UI reads stay compatible. Quantities now have one source of truth.
        CREATE VIEW stashes AS
        SELECT p.id AS player_id,
            COALESCE(MAX(i.quantity) FILTER (WHERE i.item_code='dust'),0)::integer AS dust,
            COALESCE(MAX(i.quantity) FILTER (WHERE i.item_code='alloy'),0)::integer AS alloy,
            COALESCE(MAX(i.quantity) FILTER (WHERE i.item_code='cells'),0)::integer AS cells
        FROM players p LEFT JOIN inventory_stacks i ON i.player_id=p.id GROUP BY p.id;
        INSERT INTO schema_version VALUES (3);
    END IF;
END $$;
CREATE TABLE IF NOT EXISTS raid_deployments (
    id uuid PRIMARY KEY, player_id uuid NOT NULL REFERENCES players(id),
    cells integer NOT NULL CHECK (cells >= 0 AND cells <= 12),
    cell_stack_id uuid,
    status text NOT NULL DEFAULT 'Open' CHECK (status IN ('Open','Closed','Cancelled')),
    created_at timestamptz NOT NULL DEFAULT now()
);
ALTER TABLE raid_settlements ADD COLUMN IF NOT EXISTS deployment_id uuid REFERENCES raid_deployments(id);

-- Version 4: authoritative item/container tree. V3 resource stacks are a compatibility projection.
CREATE TABLE IF NOT EXISTS inventory_profiles (
    player_id uuid PRIMARY KEY REFERENCES players(id),
    revision integer NOT NULL,
    inventory jsonb NOT NULL
);
ALTER TABLE raid_deployments ADD COLUMN IF NOT EXISTS inventory jsonb;
ALTER TABLE raid_settlements ADD COLUMN IF NOT EXISTS inventory jsonb;
INSERT INTO schema_version VALUES (4) ON CONFLICT DO NOTHING;

-- Version 5: the old 12-supply receipt cap applies only to legacy, container-less raids.
ALTER TABLE raid_settlements DROP CONSTRAINT IF EXISTS raid_settlements_check;
DO $$ BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid='raid_settlements'::regclass AND conname='raid_settlements_legacy_capacity') THEN
        ALTER TABLE raid_settlements ADD CONSTRAINT raid_settlements_legacy_capacity
            CHECK (inventory IS NOT NULL OR dust::bigint + alloy::bigint + cells::bigint <= 12);
    END IF;
END $$;
INSERT INTO schema_version VALUES (5) ON CONFLICT DO NOTHING;
