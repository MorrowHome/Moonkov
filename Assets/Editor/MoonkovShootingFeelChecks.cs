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
        CheckReleaseContinuity();
        CheckRecoil();
        CheckSustainedRecoil();
        Debug.Log("Shooting feel checks passed: buffered press/expiry/cancellation, reload handoff, burst rearm, " +
            "independent draw time, slot recovery, 30/60/144 Hz and jitter cadence, hitch cap, zoom sensitivity, micro-aim, recoil compensation/settling/aim alignment and release continuity.");
    }

    public static void CheckRecoil()
    {
        var recoil = new HaloRecoilResponse(); float pitch = 0;
        recoil.Kick(DollSingerWeapons.Halo, 1); recoil.Apply(ref pitch, 0, 1f / 60);
        Require(pitch < -.3f && pitch > -.4f, "Rifle recoil did not reach actual look input.");
        recoil.Apply(ref pitch, .34f, 1f / 60); pitch += .34f;
        for (int i = 0; i < 120; i++) recoil.Apply(ref pitch, 0, 1f / 60);
        Require(Mathf.Abs(pitch) < .0001f, "Automatic recovery dragged a compensated aim downward.");
        foreach (float dt in new[] { 1f / 30, 1f / 60, 1f / 144 })
        {
            recoil.Reset(); pitch = 0; recoil.Kick(DollSingerWeapons.Shotgun, 1);
            for (int i = 0; i < Mathf.RoundToInt(.5f / dt); i++) recoil.Apply(ref pitch, 0, dt);
            Require(Mathf.Abs(pitch) < .012f, "Recoil failed to settle at " + dt);
        }
        recoil.Reset(); pitch = -80; recoil.Kick(DollSingerWeapons.Sniper, 1);
        for (int i = 0; i < 60; i++) recoil.Apply(ref pitch, 0, 1f / 60);
        Require(pitch == -80, "Pitch clamp generated phantom recovery.");
        recoil.Reset(); pitch = 0; recoil.Kick(DollSingerWeapons.Sniper, 6); recoil.Apply(ref pitch, 0, .01f);
        Require(Mathf.Abs(pitch + .2f) < .0001f, "Scoped recoil ignored magnification.");
        recoil.Reset(); pitch = 0;
        for (int i = 0; i < 100; i++) { recoil.Kick(DollSingerWeapons.Halo, 1); recoil.Apply(ref pitch, 0, .05f); }
        Require(pitch >= -3.001f && recoil.Remaining <= 3.001f, "Sustained recoil exceeded bounded travel.");
        recoil.Reset(); float rest = pitch; recoil.Apply(ref pitch, 0, 1);
        Require(pitch == rest && recoil.Remaining == 0, "Reset left a delayed kick.");
    }

    public static void CheckSustainedRecoil()
    {
        foreach (float interval in new[] { .05f, .075f, .1f, .16f })
        foreach (float frame in new[] { 1f / 30, 1f / 60, 1f / 144 })
        foreach (float zoom in new[] { 1f, 6f })
        {
            var recoil = new HaloRecoilResponse(); float pitch = 0;
            for (int shot = 0; shot < 40; shot++)
            {
                float before = pitch;
                recoil.Kick(DollSingerWeapons.Halo, zoom, interval);
                float firstStep = Mathf.Min(frame, interval);
                recoil.Apply(ref pitch, 0, firstStep);
                Require(before - pitch > .02f / zoom,
                    $"Sustained fire lost its kick: shot={shot}, interval={interval}, frame={frame}, zoom={zoom}.");
                for (float remaining = interval - firstStep; remaining > .000001f;)
                {
                    float step = Mathf.Min(frame, remaining);
                    recoil.Apply(ref pitch, 0, step); remaining -= step;
                }
                Require(pitch >= -3.001f / zoom && pitch <= 0,
                    "Sustained settling escaped its bounded aim travel.");
            }
            for (int i = 0; i < Mathf.CeilToInt(.7f / frame); i++) recoil.Apply(ref pitch, 0, frame);
            Require(Mathf.Abs(pitch) < .001f, "Long burst did not settle after trigger release.");
        }
    }

    public static void CheckReleaseContinuity()
    {
        foreach (var rates in new[] { new Vector2(9, 50), new Vector2(14, 70), new Vector2(17, 75) })
        {
            var response = new HaloShotResponse(); response.Play(100);
            response.Step(.1f, rates.x, rates.y);
            float before = response.Release;
            Require(response.Play(101) && Mathf.Abs(response.Release - before) < .00001f,
                "Follow-up shot snapped the hand release back to rest.");
            Require(!response.Play(101) && !response.Play(99) && Mathf.Abs(response.Release - before) < .00001f,
                "Replayed shot changed the release.");
            for (uint tick = 102; tick < 302; tick++)
            {
                response.Step(.01f, rates.x, rates.y); response.Play(tick);
                Require(float.IsFinite(response.Release) && response.Release >= 0 && response.Release <= 1,
                    "Sustained release escaped its cosmetic bounds.");
            }
            response.Step(2, rates.x, rates.y);
            Require(response.Release < .0001f, "Release failed to settle after firing.");
            response.Reset(); Require(response.Release == 0 && response.Play(1), "Equip reset retained old tick or motion.");
            var coarse = new HaloShotResponse(); var fine = new HaloShotResponse(); coarse.Play(1); fine.Play(1);
            coarse.Step(.1f, rates.x, rates.y);
            for (int i = 0; i < 10; i++) fine.Step(.01f, rates.x, rates.y);
            Require(Mathf.Abs(coarse.Release - fine.Release) < .00001f, "Release depends on rendering frame rate.");
        }
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
            var cameraObject = new GameObject("Recoil command camera"); cameraObject.transform.SetParent(root.transform, false);
            view.camera = cameraObject.AddComponent<Camera>();
            foreach (bool firstPerson in new[] { false, true })
            {
                view.SetFirstPerson(firstPerson);
                look = float2.zero; free = float2.zero;
                typeof(DollSingerInput).GetField("<Look>k__BackingField", flags).SetValue(input, Vector2.zero);
                var recoil = new HaloRecoilResponse(); recoil.Kick(DollSingerWeapons.Sniper, 6);
                recoil.Apply(ref look.y, 0, 1f / 60);
                DollSingerLookController.Update(ref look, ref free, ref lean, out var rotation, input, model, view, false, 1f / 60);
                var ray = view.CaptureAimRay(rotation, Quaternion.Euler(0, look.x, 0), lean);
                Require(Vector3.Angle(ray.direction, view.camera.transform.forward) < .001f && ray.direction.y > 0,
                    "Recoil moved the camera away from the commanded shot direction.");
            }
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
