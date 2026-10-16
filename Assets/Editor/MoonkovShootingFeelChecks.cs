using System;
using System.Reflection;
using Unity.Mathematics;
using Unity.MP_FPS;
using Unity.MP_FPS.DollSinger;
using Unity.MP_FPS.Inventory;
using UnityEditor;
using UnityEngine;

public static class MoonkovShootingFeelChecks
{
    [MenuItem("Tools/Moonkov/Check Shooting Feel")]
    public static void Run()
    {
        var registry = AssetDatabase.LoadAssetAtPath<WeaponRegistry>("Assets/Data/Weapons/WeaponRegistry.asset");
        var rifle = registry.GetWeaponData(DollSingerWeapons.Halo);
        var revolver = registry.GetWeaponData(DollSingerWeapons.Revolver);
        var sniper = registry.GetWeaponData(DollSingerWeapons.Sniper);
        CheckBufferedPress(revolver);
        CheckBurst(rifle);
        CheckEquip(registry, sniper);
        CheckCadence(rifle);
        CheckAimSensitivity();
        Debug.Log("Shooting feel checks passed: buffered press/expiry/cancellation, reload handoff, burst rearm, " +
            "independent draw time, slot recovery, 30/60/144 Hz and jitter cadence, hitch cap, zoom sensitivity and micro-aim.");
    }

    private static PredictedPlayerGhost Ready(uint id) => new PredictedPlayerGhost
    { CurrentHealth = 100, CurrentAmmo = 100, EquippedWeaponID = id, WeaponCooldown = 10 };

    private static void CheckBufferedPress(WeaponData weapon)
    {
        var state = Ready(DollSingerWeapons.Revolver);
        state.WeaponCooldown = weapon.CooldownInMs - .08f;
        Require(!HaloModuleFire.TryFire(ref state, weapon, true, true, false), "Early press fired before recovery.");
        HaloModuleFire.AdvanceTimers(ref state, .08f);
        Require(HaloModuleFire.TryFire(ref state, weapon, false, true, false), "Press 80 ms early was lost.");
        HaloModuleFire.CommitShot(ref state, weapon, .02f);
        HaloModuleFire.AdvanceTimers(ref state, weapon.CooldownInMs);
        Require(!HaloModuleFire.TryFire(ref state, weapon, false, true, false), "Buffered press fired more than once.");

        state = Ready(DollSingerWeapons.Revolver);
        state.WeaponCooldown = 0;
        HaloModuleFire.TryFire(ref state, weapon, true, true, false);
        HaloModuleFire.AdvanceTimers(ref state, weapon.CooldownInMs);
        Require(!HaloModuleFire.TryFire(ref state, weapon, false, true, false), "Expired press fired late.");
        foreach (int reason in new[] { 0, 1, 2 })
        {
            state = Ready(DollSingerWeapons.Revolver); state.WeaponCooldown = weapon.CooldownInMs - .05f;
            HaloModuleFire.TryFire(ref state, weapon, true, true, false);
            if (reason == 2) state.CurrentHealth = 0;
            Require(!HaloModuleFire.TryFire(ref state, weapon, false, reason != 0, reason == 1) &&
                state.FireBufferRemaining == 0, "Aim loss, switch or death kept a pending shot.");
        }
        state = Ready(DollSingerWeapons.Revolver);
        state.ControllerState.IsReloadingState = true; state.ReloadTimer = .08f;
        state.ReloadTargetAmmo = 6; state.CurrentAmmo = 0;
        Require(!HaloModuleFire.TryFire(ref state, weapon, true, true, false), "Reload allowed a premature shot.");
        HaloModuleFire.AdvanceTimers(ref state, .08f);
        DollSingerWeapons.CompleteReload(ref state, weapon);
        Require(HaloModuleFire.TryFire(ref state, weapon, false, true, false) && state.CurrentAmmo == 6,
            "Press at the end of reload did not hand off to fire.");
        state = Ready(DollSingerWeapons.Revolver);
        state.ControllerState.IsReloadingState = true; state.ReloadTimer = .8f;
        HaloModuleFire.TryFire(ref state, weapon, true, true, false);
        Require(state.FireBufferRemaining == 0, "Early reload click remained queued.");
    }

    private static void CheckBurst(WeaponData weapon)
    {
        var state = Ready(DollSingerWeapons.Halo);
        state.EquippedModules = (byte)HaloModule.PulseController;
        Require(HaloModuleFire.TryFire(ref state, weapon, true, true, false), "Burst did not start.");
        HaloModuleFire.CommitShot(ref state, weapon, .02f);
        for (int i = 1; i < 3; i++)
        {
            HaloModuleFire.AdvanceTimers(ref state, weapon.CooldownInMs);
            Require(HaloModuleFire.TryFire(ref state, weapon, true, true, false) && state.BurstSequence == i,
                "Committed burst did not release exactly three shots.");
            HaloModuleFire.CommitShot(ref state, weapon, .02f);
        }
        HaloModuleFire.AdvanceTimers(ref state, .5f);
        Require(!HaloModuleFire.TryFire(ref state, weapon, true, true, false), "Holding the trigger restarted a burst.");
        HaloModuleFire.TryFire(ref state, weapon, false, true, false);
        state.WeaponCooldown = .35f;
        Require(!HaloModuleFire.TryFire(ref state, weapon, true, true, false), "Inter-burst recovery was bypassed.");
        HaloModuleFire.AdvanceTimers(ref state, .07f);
        Require(HaloModuleFire.TryFire(ref state, weapon, false, true, false), "Buffered burst rearm was lost.");
    }

    private static void CheckEquip(WeaponRegistry registry, WeaponData sniper)
    {
        var state = Ready(DollSingerWeapons.Sniper);
        state.InventoryWeapons = true; state.EquippedWeaponSlot = 1;
        state.PrimaryWeaponID = DollSingerWeapons.Sniper; state.SecondaryWeaponID = DollSingerWeapons.Halo;
        state.PrimaryAmmo = state.SecondaryAmmo = state.CurrentAmmo = 5;
        HaloModuleFire.BeginEquip(ref state, sniper);
        Require(!HaloModuleFire.TryFire(ref state, sniper, true, true, false), "Weapon fired during draw.");
        HaloModuleFire.AdvanceTimers(ref state, sniper.EquipTime);
        Require(HaloModuleFire.TryFire(ref state, sniper, true, true, false), "Sniper draw still waits a full firing interval.");
        HaloModuleFire.CommitShot(ref state, sniper, .02f); state.CurrentAmmo--;
        var input = new PlayerInput(); input.SetFlag(PlayerInput.InputFlag.EquipRevolver, true);
        Require(DollSingerWeapons.TryEquip(ref state, input, registry), "Could not switch away from sniper.");
        HaloModuleFire.AdvanceTimers(ref state, .2f);
        input = new PlayerInput(); input.SetFlag(PlayerInput.InputFlag.EquipHalo, true);
        Require(DollSingerWeapons.TryEquip(ref state, input, registry) && state.CurrentAmmo == 4,
            "Returning to sniper changed ammunition.");
        HaloModuleFire.AdvanceTimers(ref state, sniper.EquipTime);
        Require(!HaloModuleFire.TryFire(ref state, sniper, true, true, false), "Switching bypassed sniper recovery.");
        HaloModuleFire.AdvanceTimers(ref state, sniper.CooldownInMs);
        Require(HaloModuleFire.TryFire(ref state, sniper, true, true, false), "Sniper recovery never completed.");
    }

    private static void CheckCadence(WeaponData original)
    {
        var weapon = UnityEngine.Object.Instantiate(original);
        try
        {
            weapon.CooldownInMs = .13f;
            foreach (float step in new[] { 1f / 30, 1f / 60, 1f / 144, 0f })
            {
                var state = Ready(DollSingerWeapons.Halo);
                float time = 0, first = -1, maxError = 0;
                int shots = 0, frame = 0;
                while (time < 8)
                {
                    float dt = step > 0 ? step : (frame++ % 3 == 0 ? .041f : .011f);
                    time += dt;
                    HaloModuleFire.AdvanceTimers(ref state, dt);
                    if (!HaloModuleFire.TryFire(ref state, weapon, true, true, false)) continue;
                    if (first < 0) first = time;
                    maxError = Mathf.Max(maxError, Mathf.Abs(time - (first + shots * weapon.CooldownInMs)));
                    shots++;
                    HaloModuleFire.CommitShot(ref state, weapon, dt);
                }
                Require(shots >= 60 && maxError <= (step > 0 ? step : .041f) + .001f,
                    "Continuous fire accumulated tick drift at step " + step + ": " + maxError);
            }
            var hitch = Ready(DollSingerWeapons.Halo);
            HaloModuleFire.AdvanceTimers(ref hitch, .5f);
            Require(HaloModuleFire.TryFire(ref hitch, weapon, true, true, false), "Hitch blocked fire.");
            HaloModuleFire.CommitShot(ref hitch, weapon, .5f);
            Require(hitch.WeaponCooldown == 0 && !HaloModuleFire.TryFire(ref hitch, weapon, true, true, false),
                "A hitch banked extra shots.");
        }
        finally { UnityEngine.Object.DestroyImmediate(weapon); }
    }

    private static void CheckAimSensitivity()
    {
        var root = new GameObject("Inactive aim sensitivity fixture"); root.SetActive(false);
        try
        {
            var view = root.AddComponent<DollSingerView>();
            var input = root.AddComponent<DollSingerInput>();
            var model = root.AddComponent<DollSingerMovement>();
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
            typeof(DollSingerView).GetField("presentedWeaponMagnification", flags).SetValue(view, 8f);
            view.SetAimBlend(0);
            Require(Mathf.Approximately(view.AimLookSensitivity, 1), "Unaimed sensitivity changed.");
            view.SetAimBlend(1);
            Require(Mathf.Approximately(view.AimLookSensitivity, .125f), "8x aim did not preserve fine screen-space control.");
            typeof(DollSingerInput).GetField("<Look>k__BackingField", flags).SetValue(input, new Vector2(.008f, .008f));
            var look = float2.zero; var free = float2.zero; float lean = 0;
            DollSingerLookController.Update(ref look, ref free, ref lean, out _, input, model, view, false, .016f);
            Require(math.distance(look, new float2(.001f, -.001f)) < .000001f, "Micro-aim input was lost or magnified.");
            model.m_CinemachineCameraTarget = root;
            typeof(DollSingerMovement).GetField("m_LookInput", flags).SetValue(model, new Vector2(.001f, .001f));
            typeof(DollSingerMovement).GetMethod("CameraRotation", flags).Invoke(model, null);
            Require(Mathf.Abs(Mathf.DeltaAngle(root.transform.eulerAngles.y, .001f)) < .0001f,
                "Standalone camera discarded a sub-deadzone turn.");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException("Shooting feel: " + message); }
}
