using System;
using Unity.MP_FPS;

public static class PmcWeaponTacticsChecks
{
    private static int s_Checks;
    private static void Check(bool value, string message)
    { s_Checks++; if (!value) throw new InvalidOperationException(message); }
    private static PredictedPlayerGhost Rifle()
    {
        var health = new PredictedPlayerGhost(); RaidHealth.Initialize(ref health);
        health.InventoryWeapons = true; health.EquippedWeaponSlot = 1; health.EquippedWeaponID = DollSingerWeapons.Halo;
        health.PrimaryWeaponID = DollSingerWeapons.Halo; health.PrimaryAmmo = health.CurrentAmmo = 30;
        health.SecondaryWeaponID = DollSingerWeapons.Shotgun; health.SecondaryAmmo = 4;
        health.PistolWeaponID = DollSingerWeapons.Revolver; health.PistolAmmo = 6;
        return health;
    }

    public static string Run()
    {
        s_Checks = 0;
        var health = Rifle();
        Check(PmcWeaponTactics.PreferredSlot(health, 8) == 2 && PmcWeaponTactics.PreferredSlot(health, 11) == 2,
            "nearby target selects the loaded shotgun");
        Check(PmcWeaponTactics.PreferredSlot(health, 11.1f) == 0 && PmcWeaponTactics.PreferredSlot(health, 17.9f) == 0,
            "the transition band holds the current weapon");
        Check(PmcWeaponTactics.PreferredSlot(health, 25) == 0, "a rifle already equipped needs no switch");
        health.EquippedWeaponSlot = 2; health.EquippedWeaponID = DollSingerWeapons.Shotgun; health.CurrentAmmo = 4;
        Check(PmcWeaponTactics.PreferredSlot(health, 18) == 1 && PmcWeaponTactics.PreferredSlot(health, 30) == 1,
            "distant target selects the loaded rifle");
        Check(PmcWeaponTactics.PreferredSlot(health, 17.9f) == 0 && PmcWeaponTactics.PreferredSlot(health, 8) == 0,
            "shotgun remains equipped within its current band");
        health.PrimaryAmmo = 0;
        Check(PmcWeaponTactics.PreferredSlot(health, 30) == 0, "an empty rifle is not selected");
        health = Rifle(); health.SecondaryAmmo = 0;
        Check(PmcWeaponTactics.PreferredSlot(health, 8) == 0, "an empty shotgun is not selected");
        health = Rifle(); health.ControllerState.IsReloadingState = true;
        Check(PmcWeaponTactics.PreferredSlot(health, 8) == 0, "charging blocks tactical switching");
        health.ControllerState.IsReloadingState = false; health.WeaponEquipRemaining = .1f;
        Check(PmcWeaponTactics.PreferredSlot(health, 8) == 0, "an unfinished weapon switch is not restarted");
        health = Rifle(); health.InventoryWeapons = false;
        Check(PmcWeaponTactics.PreferredSlot(health, 8) == 0, "the policy only applies to physical inventory weapons");
        health = Rifle(); health.CurrentHealth = 0;
        Check(PmcWeaponTactics.PreferredSlot(health, 8) == 0, "a dead actor cannot switch");
        health = Rifle();
        Check(PmcWeaponTactics.PreferredSlot(health, -1) == 0 && PmcWeaponTactics.PreferredSlot(health, float.NaN) == 0 &&
            PmcWeaponTactics.PreferredSlot(health, float.PositiveInfinity) == 0, "invalid distance does not select a weapon");
        health.PrimaryWeaponID = DollSingerWeapons.Shotgun; health.PrimaryAmmo = 4;
        health.SecondaryWeaponID = DollSingerWeapons.Halo; health.SecondaryAmmo = 30;
        health.EquippedWeaponSlot = 2; health.EquippedWeaponID = DollSingerWeapons.Halo;
        Check(PmcWeaponTactics.PreferredSlot(health, 5) == 1, "selection follows weapon identity instead of assuming slot order");
        health.EquippedWeaponSlot = 1; health.EquippedWeaponID = DollSingerWeapons.Shotgun;
        Check(PmcWeaponTactics.PreferredSlot(health, 30) == 2, "reversed slots also work at long range");
        foreach (uint close in new[] { DollSingerWeapons.Cicada, DollSingerWeapons.Thorn })
        {
            health = Rifle(); health.SecondaryWeaponID = close;
            Check(PmcWeaponTactics.PreferredSlot(health, 8) == 2, "a varied close-range weapon replaces the rifle nearby");
            health.EquippedWeaponSlot = 2; health.EquippedWeaponID = close; health.CurrentAmmo = 4;
            Check(PmcWeaponTactics.PreferredSlot(health, 30) == 1, "a varied close-range weapon yields to the rifle at distance");
        }
        foreach (uint longGun in new[] { DollSingerWeapons.Sniper, DollSingerWeapons.Vesper, DollSingerWeapons.Nocturne,
            DollSingerWeapons.Trine, DollSingerWeapons.Aegis, DollSingerWeapons.Rondo })
        {
            health = Rifle(); health.PrimaryWeaponID = longGun; health.EquippedWeaponID = longGun;
            Check(PmcWeaponTactics.PreferredSlot(health, 8) == 2, "a varied long gun yields to the shotgun nearby");
            health.EquippedWeaponSlot = 2; health.EquippedWeaponID = DollSingerWeapons.Shotgun; health.CurrentAmmo = 4;
            Check(PmcWeaponTactics.PreferredSlot(health, 60) == 1, "a varied long gun is selected for a distant contact");
        }
        health = Rifle(); health.SecondaryWeaponID = DollSingerWeapons.Sniper;
        Check(PmcWeaponTactics.PreferredSlot(health, 30) == 0 && PmcWeaponTactics.PreferredSlot(health, 50) == 2,
            "a sniper is preferred over the rifle only for the distant band");
        health = Rifle(); health.SecondaryWeaponID = DollSingerWeapons.Trine;
        Check(PmcWeaponTactics.PreferredSlot(health, 30) == 0, "equally suitable weapons do not cause switching");
        health = Rifle(); health.PrimaryAmmo = 30; health.CurrentAmmo = 0; health.SecondaryAmmo = 0;
        Check(PmcWeaponTactics.PreferredSlot(health, 30) == 3, "a loaded sidearm replaces an empty active weapon despite stale slot ammo");
        return "PASS " + s_Checks + " PMC weapon tactics checks";
    }
}
