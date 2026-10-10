-- Only for the isolated smoke harness. psql supplies quoted run-specific variables.
BEGIN;
SET LOCAL search_path = pg_catalog, public;
CREATE TEMP TABLE smoke_context ON COMMIT DROP AS
SELECT :'expected_database'::text AS database_name, :'username'::text AS username,
       NULLIF(:'player_id', '')::uuid AS player_id;
DO $$ BEGIN
    IF NOT EXISTS (
        SELECT 1 FROM pg_temp.smoke_context c JOIN pg_database d ON d.datname = c.database_name
        WHERE c.database_name = current_database()
          AND c.database_name ~ '^moon_smoke_check_[0-9a-f]{32}$'
          AND c.username ~ '^smoke_[0-9a-f]{16}$'
          AND pg_get_userbyid(d.datdba) = current_user
    ) THEN
        RAISE EXCEPTION 'Refusing cleanup outside the owned isolated smoke database';
    END IF;
END $$;
CREATE TEMP TABLE smoke_target ON COMMIT DROP AS
SELECT a.player_id FROM public.accounts a
JOIN public.players p ON p.id = a.player_id
JOIN pg_temp.smoke_context c ON a.username_key = c.username AND p.display_name = c.username
WHERE c.player_id IS NULL OR a.player_id = c.player_id;
DO $$ BEGIN
    IF EXISTS (SELECT 1 FROM public.accounts a JOIN pg_temp.smoke_context c ON a.username_key = c.username
               WHERE NOT EXISTS (SELECT 1 FROM pg_temp.smoke_target t WHERE t.player_id = a.player_id))
       OR EXISTS (SELECT 1 FROM public.players p JOIN pg_temp.smoke_context c ON p.id = c.player_id
                  WHERE NOT EXISTS (SELECT 1 FROM pg_temp.smoke_target t WHERE t.player_id = p.id)) THEN
        RAISE EXCEPTION 'Smoke fixture identity does not match; no rows were removed';
    END IF;
END $$;
-- Child tables first. raid_settlements also references raid_deployments.
-- stashes is an aggregate compatibility view; delete its backing rows instead.
DELETE FROM public.login_sessions WHERE player_id IN (SELECT player_id FROM pg_temp.smoke_target);
DELETE FROM public.inventory_trades WHERE player_id IN (SELECT player_id FROM pg_temp.smoke_target);
DELETE FROM public.raid_settlements WHERE player_id IN (SELECT player_id FROM pg_temp.smoke_target);
DELETE FROM public.raid_deployments WHERE player_id IN (SELECT player_id FROM pg_temp.smoke_target);
DELETE FROM public.inventory_profiles WHERE player_id IN (SELECT player_id FROM pg_temp.smoke_target);
DELETE FROM public.inventory_stacks WHERE player_id IN (SELECT player_id FROM pg_temp.smoke_target);
DELETE FROM public.legacy_stashes_v2 WHERE player_id IN (SELECT player_id FROM pg_temp.smoke_target);
DELETE FROM public.accounts WHERE player_id IN (SELECT player_id FROM pg_temp.smoke_target);
DELETE FROM public.players WHERE id IN (SELECT player_id FROM pg_temp.smoke_target);
COMMIT;
