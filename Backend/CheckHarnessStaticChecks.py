"""Offline drift guards; these do not compile C#, run PowerShell, or contact a database.

Run from any directory with Python 3: python Backend/CheckHarnessStaticChecks.py
Real PowerShell/.NET/PostgreSQL checks are still required before merging.
"""
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parent.parent


def source(path):
    file = ROOT / path
    return file.read_text(encoding="utf-8") if file.exists() else ""


class HarnessStaticChecks(unittest.TestCase):
    def setUp(self):
        self.smoke = source("Backend/Smoke-Test.ps1")
        self.cleanup = source("Backend/Smoke-Test.Cleanup.sql")
        self.schema = source("Backend/MoonPersistence/schema.sql")
        self.shop = source("Backend/ShopChecks/Program.cs")

    def test_shop_checks_complete_catalogue_and_fields(self):
        self.assertIn("offers.Length == ShopCatalog.Offers.Length", self.shop)
        self.assertNotRegex(self.shop, r"offers\.Length\s*==\s*\d+")
        self.assertIn("Distinct(StringComparer.Ordinal)", self.shop)
        self.assertIn("foreach (var expected in ShopCatalog.Offers)", self.shop)
        for field in ("Code", "Name", "BuyDust", "SellDust"):
            self.assertIn("expected." + field, self.shop)
        for code in ("sniper", "medkit"):
            self.assertIn('o.Code == "' + code + '"', self.shop)

    def test_isolation_is_explicit_and_fail_closed(self):
        guard = "if ($IsolatedDatabase -cnotmatch '^moon_smoke_check_[0-9a-f]{32}$')"
        self.assertIn(guard, self.smoke)
        self.assertLess(self.smoke.index(guard), self.smoke.index("Get-Content"))
        self.assertNotIn("moon-server.local.json", self.smoke)
        self.assertNotIn("postgres-admin.password", self.smoke)
        self.assertNotRegex(self.smoke, r"-d\s+moonkov\b")
        self.assertIn("$settings['Database'] = $IsolatedDatabase", self.smoke)

    def test_preflight_precedes_service_and_fixture(self):
        for guard in ("pg_get_userbyid(d.datdba) = current_user", "pg_class", "pg_stat_activity"):
            self.assertIn(guard, self.smoke)
        self.assertLess(self.smoke.index("Invoke-TestSql $preflight"), self.smoke.index("[System.Diagnostics.Process]::Start"))
        self.assertLess(self.smoke.index("if (!$ready -or $service.HasExited)"), self.smoke.index("$registered = Send-Api"))
        self.assertIn("$start.EnvironmentVariables['ConnectionStrings__Postgres'] = $settings.ConnectionString", self.smoke)
        self.assertIn('$base = "http://127.0.0.1:$servicePort"', self.smoke)
        self.assertIn("$start.EnvironmentVariables['ServerKey'] = $serverKey", self.smoke)

    def test_loopback_url_cannot_be_overridden_by_kestrel(self):
        self.assertIn("'^(?:(?:ASPNETCORE|DOTNET)_)?Kestrel(?::|__|$)'", self.smoke)
        self.assertIn("'^(?:(?:ASPNETCORE|DOTNET)_)?ContentRoot$'", self.smoke)
        self.assertIn("-Filter 'appsettings*.json'", self.smoke)
        self.assertIn("$handler.UseProxy = $false", self.smoke)
        self.assertIn("$_ -imatch '^Kestrel(?::|$)'", self.smoke)
        self.assertLess(self.smoke.index("foreach ($entry in Get-ChildItem Env:)"), self.smoke.index("Invoke-TestSql $preflight"))
        pattern = r"^(?:(?:ASPNETCORE|DOTNET)_)?Kestrel(?::|__|$)"
        for key in ("Kestrel__Endpoints__Http__Url", "ASPNETCORE_Kestrel__Endpoints__Http__Url", "DOTNET_Kestrel:Endpoints:Http:Url"):
            self.assertIsNotNone(re.match(pattern, key, re.I))

    def test_empty_database_guard_does_not_hide_user_schema_pgdata(self):
        # SQL LIKE 'pg_%' treats '_' as a wildcard and wrongly skips pgdata.
        self.assertNotIn("NOT LIKE 'pg_%'", self.smoke)
        self.assertIn("n.nspname !~ '^pg_'", self.smoke)
        self.assertIsNone(re.match(r"^pg_", "pgdata"))
        self.assertIsNotNone(re.match(r"^pg_", "pg_catalog"))

    def test_cleanup_is_transactional_and_not_the_compatibility_view(self):
        sql = re.sub(r"--[^\n]*", "", self.cleanup).strip()
        self.assertTrue(sql.startswith("BEGIN;"))
        self.assertTrue(sql.endswith("COMMIT;"))
        self.assertNotRegex(sql, r"(?i)\b(TRUNCATE|CASCADE)\b|DROP\s+(TABLE|DATABASE)")
        self.assertNotRegex(sql, r"(?i)DELETE\s+FROM\s+(?:public\.)?stashes\b")
        self.assertIn("DELETE FROM public.legacy_stashes_v2", sql)

    def test_cleanup_covers_schema_foreign_keys_in_order(self):
        deletes = re.findall(r"DELETE FROM public\.(\w+)\s+WHERE", self.cleanup)
        self.assertEqual(len(deletes), len(set(deletes)), "duplicate cleanup statement")
        edges = []
        for table, body in re.findall(r"CREATE TABLE IF NOT EXISTS (\w+)\s*\((.*?)\n\);", self.schema, re.S):
            table = "legacy_stashes_v2" if table == "stashes" else table
            edges.extend((table, parent) for parent in re.findall(r"REFERENCES (\w+)\(", body))
        edges.extend(re.findall(r"ALTER TABLE (\w+) ADD COLUMN[^;]*REFERENCES (\w+)\(", self.schema))
        player_tables = {table for table, parent in edges if parent == "players"}
        self.assertGreaterEqual(len(player_tables), 8, "schema parser must discover all current player dependencies")
        self.assertEqual(set(deletes), player_tables | {"players"})
        for child, parent in edges:
            if child in deletes and parent in deletes:
                self.assertLess(deletes.index(child), deletes.index(parent), f"{child} must be deleted before {parent}")

    def test_every_delete_is_scoped_to_the_validated_fixture(self):
        statements = re.findall(r"DELETE FROM[^;]+;", self.cleanup)
        self.assertTrue(statements)
        for statement in statements:
            self.assertRegex(statement, r"^DELETE FROM public\.\w+ WHERE (player_id|id) IN \(SELECT player_id FROM pg_temp\.smoke_target\);$")
        for guard in ("c.database_name = current_database()", "pg_get_userbyid(d.datdba) = current_user",
                      "^moon_smoke_check_[0-9a-f]{32}$", "^smoke_[0-9a-f]{16}$",
                      "a.username_key = c.username AND p.display_name = c.username", "a.player_id = c.player_id",
                      "RAISE EXCEPTION"):
            self.assertIn(guard, self.cleanup)
        self.assertIn("NULLIF(:'player_id', '')::uuid", self.cleanup)
        self.assertIn("c.player_id IS NULL", self.cleanup)

    def test_cleanup_failure_cannot_print_pass(self):
        cleanup = self.smoke.index("Invoke-TestSql $sql")
        failure = self.smoke.index("if ($cleanupFailures.Count)")
        primary_failure = self.smoke.index("if ($testFailure) { throw $testFailure }")
        success = self.smoke.index("Write-Host 'PASS:")
        self.assertLess(cleanup, failure)
        self.assertLess(failure, primary_failure)
        self.assertLess(primary_failure, success)
        self.assertIn("throw ($cleanupFailures -join ' ')", self.smoke)
        self.assertIn("if ($LASTEXITCODE -ne 0) { throw 'Isolated smoke-test SQL failed.' }", self.smoke)
        self.assertIn("-v ON_ERROR_STOP=1", self.smoke)
        self.assertLess(self.smoke.index("$service.WaitForExit()"), cleanup)


if __name__ == "__main__":
    unittest.main(verbosity=2)
