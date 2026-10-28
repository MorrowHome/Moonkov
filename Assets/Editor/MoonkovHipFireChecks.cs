using System;
using System.Reflection;
using Unity.MP_FPS;
using Unity.MP_FPS.DollSinger;
using UnityEditor;
using UnityEngine;

public static class MoonkovHipFireChecks
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int assertions;

    [MenuItem("Tools/Moonkov/Check Hip Fire")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Use Edit mode.");
        assertions = 0;
        var registry = AssetDatabase.LoadAssetAtPath<WeaponRegistry>("Assets/Data/Weapons/WeaponRegistry.asset");
        foreach (float dt in new[] { 1f / 30, 1f / 60, 1f / 144 })
        {
            CheckWindup(registry, dt);
            CheckHeldTrigger(registry, dt);
            CheckTransition(registry, dt);
        }
        CheckCancellation(registry);
        CheckEquipAndReload(registry);
        CheckSpread(registry);
        CheckRecoil();
        CheckPresentation();
        Debug.Log($"Hip fire checks passed: {assertions} assertions; 30/60/144 Hz windup, single click, held auto/semi/burst, " +
            "ADS handoff, cancellation, deterministic spread, stronger/slower recoil, separate pose and lens.");
    }

    private static PredictedPlayerGhost Ready(uint id) => new PredictedPlayerGhost
    { CurrentHealth = 100, MaxHealth = 100, CurrentAmmo = 100, EquippedWeaponID = id, WeaponCooldown = 10, EquippedWeaponSlot = 1 };

    private static PlayerInput Intent(bool shoot = false, bool held = false, bool aim = false)
    {
        var input = new PlayerInput();
        input.SetFlag(PlayerInput.InputFlag.Shoot, shoot);
        input.SetFlag(PlayerInput.InputFlag.FireHeld, held);
        input.SetFlag(PlayerInput.InputFlag.Aim, aim);
        return input;
    }

    private static bool Step(ref PredictedPlayerGhost state, WeaponData weapon, PlayerInput input, float dt, bool switched = false)
    {
        HaloModuleFire.AdvanceTimers(ref state, dt);
        bool fired = HaloModuleFire.TryFire(ref state, weapon, input, switched, dt);
        if (fired) { state.CurrentAmmo--; HaloModuleFire.CommitShot(ref state, weapon, dt); }
        return fired;
    }

    private static void CheckWindup(WeaponRegistry registry, float dt)
    {
        foreach (uint id in new[] { DollSingerWeapons.Halo, DollSingerWeapons.Revolver, DollSingerWeapons.Shotgun, DollSingerWeapons.Sniper, DollSingerWeapons.Trine })
        {
            var state = Ready(id); var weapon = registry.GetWeaponData(id);
            int shots = 0; float first = -1;
            for (int frame = 0; frame < Mathf.CeilToInt(2f / dt); frame++)
                if (Step(ref state, weapon, Intent(frame == 0, frame == 0), dt))
                {
                    shots++; if (first < 0) first = (frame + 1) * dt;
                    Require(state.WeaponRaiseProgress >= .99999f, "Shot escaped the complete hand/halo windup.");
                }
            Require(first >= DollSingerHaloAim.RaiseSeconds - .0001f && first < DollSingerHaloAim.RaiseSeconds + dt + .0001f,
                $"Single click was lost or fired before windup: {id}, {dt}, {first}.");
            Require(shots == (weapon.BurstCount > 1 ? weapon.BurstCount : 1), "Single click duplicated or dropped a shot.");
            Require(state.WeaponRaiseProgress == 0 && !state.HaloPendingShot, "Released hip pose never returned to idle.");
        }
    }

    private static void CheckHeldTrigger(WeaponRegistry registry, float dt)
    {
        foreach (uint id in new[] { DollSingerWeapons.Halo, DollSingerWeapons.Revolver, DollSingerWeapons.Trine })
        {
            var state = Ready(id); var weapon = registry.GetWeaponData(id); int shots = 0;
            for (int frame = 0; frame < Mathf.CeilToInt(2 / dt); frame++)
            {
                bool shoot = weapon.Automatic || weapon.BurstCount > 1 || frame == 0;
                if (Step(ref state, weapon, Intent(shoot, true), dt)) shots++;
            }
            Require(weapon.BurstCount > 1 ? shots == weapon.BurstCount : weapon.Automatic ? shots > 8 : shots == 1,
                "Held hip trigger changed auto/semi/burst semantics.");
            Require(state.WeaponRaiseProgress == 1, "Held fire lowered the hands.");
        }
    }

    private static void CheckTransition(WeaponRegistry registry, float dt)
    {
        var state = Ready(DollSingerWeapons.Halo); var weapon = registry.GetWeaponData(state.EquippedWeaponID);
        for (int i = 0; i < Mathf.CeilToInt(.4f / dt); i++) Step(ref state, weapon, Intent(aim: true), dt);
        Require(Step(ref state, weapon, Intent(true, true), dt), "Releasing ADS imposed another hip windup.");
        for (int i = 0; i < Mathf.CeilToInt(.3f / dt); i++) Step(ref state, weapon, Intent(held: true), dt);
        Require(Step(ref state, weapon, Intent(true, true, true), dt), "Entering ADS imposed another pose windup.");
    }

    private static void CheckCancellation(WeaponRegistry registry)
    {
        var weapon = registry.GetWeaponData(DollSingerWeapons.Revolver);
        foreach (int reason in new[] { 0, 1, 2, 3, 4 })
        {
            var state = Ready(DollSingerWeapons.Revolver);
            Step(ref state, weapon, Intent(true, true), .02f);
            var input = Intent();
            if (reason == 0) input.SetFlag(PlayerInput.InputFlag.FireSuppressed, true);
            if (reason == 1) input.FreeLooking = true;
            if (reason == 2) state.CurrentHealth = 0;
            if (reason == 4) { state.ControllerState.IsReloadingState = true; state.ReloadTimer = 2; }
            Require(!Step(ref state, weapon, input, .02f, reason == 3) && !state.HaloPendingShot,
                "Blocked input, free look, death, switch or reload kept a pending shot.");
            state.CurrentHealth = 100; state.ControllerState.IsReloadingState = false;
            for (int i = 0; i < 100; i++) Require(!Step(ref state, weapon, Intent(), .02f), "Cancelled windup fired later.");
        }
        var buffered = Intent(true, true);
        var blocked = Intent(); blocked.SetFlag(PlayerInput.InputFlag.FireSuppressed, true);
        buffered.UpdateFrom(blocked);
        Require(!buffered.Shoot && !buffered.FireHeld && buffered.FireSuppressed, "Command merge revived a suppressed shot.");
        var malformed = Intent(true, true); malformed.LookYawPitchDegrees.x = float.NaN;
        Require(malformed.Sanitized().FireSuppressed && !malformed.Sanitized().Shoot, "Malformed aim retained hip fire.");
        var dark = Ready(DollSingerWeapons.Halo); dark.ClashEnabled = true; dark.ClashDarkSight = true;
        Require(RaidHealth.RestrictInput(dark, Intent(true, true)).FireSuppressed, "Dark sight allowed a pending shot.");
    }

    private static void CheckSpread(WeaponRegistry registry)
    {
        foreach (uint id in new[] { DollSingerWeapons.Halo, DollSingerWeapons.Revolver, DollSingerWeapons.Shotgun, DollSingerWeapons.Sniper, DollSingerWeapons.Nocturne })
        {
            var weapon = registry.GetWeaponData(id); float hipTotal = 0, adsTotal = 0;
            for (uint tick = 1; tick <= 128; tick++)
            for (int pellet = 0; pellet < weapon.PelletCount; pellet++)
            {
                Vector3 hip = WeaponSpread.Direction(Vector3.forward, weapon, pellet, tick, false);
                Require(hip == WeaponSpread.Direction(Vector3.forward, weapon, pellet, tick, false) &&
                    Vector3.Angle(Vector3.forward, hip) <= WeaponSpread.ConeDegrees(weapon, false) + .001f,
                    "Prediction/server hip cone diverged or escaped its bounds.");
                hipTotal += Vector3.Angle(Vector3.forward, hip);
                adsTotal += Vector3.Angle(Vector3.forward, WeaponSpread.Direction(Vector3.forward, weapon, pellet, tick));
            }
            Require(hipTotal > adsTotal * 1.9f && WeaponSpread.ConeDegrees(weapon, false) >= 6,
                "Hip fire was not substantially less accurate than ADS.");
        }
    }

    private static void CheckEquipAndReload(WeaponRegistry registry)
    {
        var weapon = registry.GetWeaponData(DollSingerWeapons.Aegis);
        var state = Ready(DollSingerWeapons.Aegis);
        HaloModuleFire.BeginEquip(ref state, weapon);
        float first = -1;
        for (int frame = 0; frame < 60; frame++)
            if (Step(ref state, weapon, Intent(frame == 0, frame == 0), .02f) && first < 0) first = (frame + 1) * .02f;
        Require(first >= weapon.EquipTime && first < weapon.EquipTime + .021f, "Draw readiness swallowed a click or fired early.");

        weapon = registry.GetWeaponData(DollSingerWeapons.Revolver);
        state = Ready(DollSingerWeapons.Revolver);
        state.CurrentAmmo = 0; state.ControllerState.IsReloadingState = true; state.ReloadTimer = .08f;
        state.ReloadTargetAmmo = weapon.MagazineSize;
        Require(!Step(ref state, weapon, Intent(true, true), .02f) && state.HaloPendingShot,
            "Reload ending discarded a click needed to raise the hands.");
        state.ReloadTimer = 0; DollSingerWeapons.CompleteReload(ref state, weapon);
        int shots = 0;
        for (int i = 0; i < 60; i++) if (Step(ref state, weapon, Intent(), .02f)) shots++;
        Require(shots == 1, "Reload-to-windup handoff duplicated or lost the shot.");
        state = Ready(DollSingerWeapons.Revolver);
        Step(ref state, weapon, Intent(true, true), .02f); HaloModuleFire.Reset(ref state);
        Require(!state.HaloPendingShot, "Action reset retained a pending windup shot.");
    }

    private static void CheckRecoil()
    {
        foreach (float dt in new[] { 1f / 30, 1f / 60, 1f / 144 })
        {
            var hip = new HaloRecoilResponse(); var ads = new HaloRecoilResponse(); float hp = 0, ap = 0;
            hip.Kick(DollSingerWeapons.Halo, 6, .1f, false); ads.Kick(DollSingerWeapons.Halo, 1, .1f);
            hip.Apply(ref hp, 0, dt); ads.Apply(ref ap, 0, dt);
            Require(-hp > -ap * 2.7f, "Hip kick did not reach the actual look angle.");
            for (int i = 1; i < Mathf.CeilToInt(.5f / dt); i++) { hip.Apply(ref hp, 0, dt); ads.Apply(ref ap, 0, dt); }
            Require(-hp > .15f && Mathf.Abs(ap) < .005f, "Hip muzzle recovered as quickly as ADS.");
            for (int i = 0; i < Mathf.CeilToInt(3f / dt); i++) hip.Apply(ref hp, 0, dt);
            Require(Mathf.Abs(hp) < .0001f, "Hip muzzle failed to settle after trigger release.");
            hip.Reset(); hp = 0; hip.Kick(DollSingerWeapons.Halo, 1, .1f, false); hip.Apply(ref hp, 0, dt);
            float compensation = -hp; hip.Apply(ref hp, compensation, dt); hp += compensation;
            hip.Apply(ref hp, 0, 2);
            Require(Mathf.Abs(hp) < .0001f, "Hip recovery pulled down a compensated aim.");
        }
    }

    private static void CheckPresentation()
    {
        var root = new GameObject("Inactive hip presentation fixture"); root.SetActive(false);
        DollSingerView view = null;
        try
        {
            var halo = root.AddComponent<DollSingerHaloAim>();
            halo.SetNetworkPresentation(false, Vector3.forward, Vector3.forward * 20, readying: true);
            Call(halo, "Update");
            Require((bool)Get(halo, "wantsAim") && halo.AdsBlend == 0, "Hip pose requested ADS zoom.");
            int aimSamples = 0, readySamples = 0;
            halo.OwnedAimIntent = () => { aimSamples++; return false; };
            halo.OwnedReadyIntent = () => { readySamples++; return true; };
            Call(halo, "Update");
            Require(aimSamples == 1 && readySamples == 1 && (bool)Get(halo, "wantsAim"), "Owned hip intent waited for a snapshot or was sampled twice.");
            root.AddComponent<DollSingerMovement>();
            var pivot = new GameObject("View"); pivot.transform.SetParent(root.transform, false);
            var cameraObject = new GameObject("Camera"); cameraObject.transform.SetParent(pivot.transform, false);
            var camera = cameraObject.AddComponent<Camera>(); camera.enabled = false;
            view = pivot.AddComponent<DollSingerView>(); view.camera = camera; view.ApplyViewSettings(); view.BindPlayer(root, true);
            foreach (bool first in new[] { true, false })
            foreach (float zoom in new[] { 1.35f, 6f, 8f })
            {
                view.SetFirstPerson(first); Set(view, "viewBlend", first ? 1f : 0f);
                view.SetWeaponAimMagnification(zoom); Set(view, "presentedWeaponMagnification", zoom);
                view.SetAimBlend(0); view.SetWeaponPoseBlend(1); Call(view, "LateUpdate");
                Require(Mathf.Abs(camera.fieldOfView - view.firstPersonFieldOfView) < .001f &&
                    Mathf.Abs(view.AimLookSensitivity - 1) < .001f, "Raised hip pose changed FOV or mouse sensitivity.");
            }
        }
        finally { if (view) view.BindPlayer(null, true); UnityEngine.Object.DestroyImmediate(root); }
    }

    private static object Get(object value, string name) => value.GetType().GetField(name, Private).GetValue(value);
    private static void Set(object value, string name, object data) => value.GetType().GetField(name, Private).SetValue(value, data);
    private static void Call(object value, string name) => value.GetType().GetMethod(name, Private).Invoke(value, null);
    private static void Require(bool condition, string message)
    { assertions++; if (!condition) throw new InvalidOperationException("Hip fire: " + message); }
}
