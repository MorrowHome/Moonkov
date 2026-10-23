using System;
using UnityEditor;
using UnityEngine;
using Unity.MP_FPS;
using Unity.MP_FPS.DollSinger;

public static class MoonkovShootingAudiovisualChecks
{
    [MenuItem("Tools/Moonkov/Check Shooting Audiovisual Feedback")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Use Edit mode.");
        var library = Resources.Load<RifleFeedbackLibrary>("Moonkov/RifleFeedback");
        CheckReports(library.Rifle, "RiflePunch"); CheckReports(library.Revolver, "RevolverPunch"); CheckReports(library.Sniper, "SniperPunch");
        Require(library.Shotgun.FireBody.name == "ShotgunShotBody" && library.Shotgun.FireSnap.name == "ShotgunShotSnap" &&
            library.Shotgun.FireTail.name == "ShotgunShotTail", "Shotgun presentation references changed.");
        var sound = library.Rifle.FireBody;
        float originalSpatial = sound.DistanceInfo.SpatialBlend;
        var emitter = new SoundEmitter(null);
        emitter.SoundDefOverrideInfo = new SoundEmitter.SoundDefOverrideData(sound) { SpatialBlend = 0 };
        emitter.CopyOverrideDataFromSoundDef(sound);
        Require(emitter.SoundDefOverrideInfo.SpatialBlend == null && sound.DistanceInfo.SpatialBlend == originalSpatial,
            "Owner spatial override leaked to the next emitter use or mutated the shared asset.");
        CheckRelease(); CheckImpactBudget(library); CheckTrails(); CheckHitCadence();
        Require(HaloProjectileVisual.TrailLengthLimit(false, true, false) < HaloProjectileVisual.TrailLengthLimit(false, false, false) &&
            HaloProjectileVisual.TrailLengthLimit(false, false, true) <= 6.5f, "Weapon trail hierarchy changed.");
        Debug.Log("Shooting audiovisual checks passed: CC0 report variants/PCM/headroom, preserved shotgun references, " +
            "pooled audio override reset, release deduplication/expiry/sight clearance, impact pool/contact reuse and trail budgets.");
    }

    public static void CheckHitCadence()
    {
        var root = new GameObject("Inactive hit cadence fixture"); root.SetActive(false);
        try
        {
            var hud = root.AddComponent<InGameHUD>();
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var type = typeof(InGameHUD); var present = type.GetMethod("PresentRifleHit", flags);
            float Value(string name) => (float)type.GetField(name, flags).GetValue(hud);
            bool Killed() => (bool)type.GetField("m_RifleKill", flags).GetValue(hud);
            var profile = new HaloWeaponFeedbackProfile { HitDuration = .14f, KillDuration = .26f };
            void Hit(bool killed, float now) => present.Invoke(hud, new object[] { killed, profile, now });
            Hit(false, 1);
            Require(!Killed() && Mathf.Abs(Value("m_RifleHitUntil") - 1.14f) < .0001f, "Ordinary hit timing changed.");
            Hit(false, 1.08f);
            Require(Mathf.Abs(Value("m_RifleHitStarted") - 1.08f) < .0001f, "Continuous hits do not retrigger their pulse.");
            Hit(true, 1.1f);
            Require(Killed() && Mathf.Abs(Value("m_RifleHitUntil") - 1.36f) < .0001f, "Lethal hit did not upgrade feedback.");
            Hit(false, 1.3f);
            Require(Killed() && Mathf.Abs(Value("m_RifleHitUntil") - 1.36f) < .0001f &&
                Mathf.Abs(Value("m_RifleHitStarted") - 1.1f) < .0001f, "Body hit prolonged or restarted the kill marker.");
            Hit(false, 1.4f);
            Require(!Killed() && Mathf.Abs(Value("m_RifleHitUntil") - 1.54f) < .0001f, "Kill color leaked into the next target.");
            Hit(true, 2); Hit(true, 2.1f);
            Require(Killed() && Mathf.Abs(Value("m_RifleHitStarted") - 2.1f) < .0001f, "A new confirmed kill failed to retrigger.");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    private static void CheckReports(HaloWeaponFeedbackProfile profile, string prefix)
    {
        Require(profile != null && profile.FireBody && profile.FireBody.Clips.Count == 3, "Missing report variants: " + prefix);
        foreach (var clip in profile.FireBody.Clips)
        {
            Require(clip && clip.name.StartsWith(prefix) && clip.channels == 1 && clip.frequency == 48000 &&
                clip.loadType == AudioClipLoadType.DecompressOnLoad && clip.length < 1, "Invalid short report import: " + prefix);
            var samples = new float[clip.samples]; clip.LoadAudioData();
            Require(clip.GetData(samples, 0), "Cannot inspect report PCM.");
            float peak = 0;
            foreach (float value in samples) { Require(!float.IsNaN(value) && !float.IsInfinity(value), "Non-finite audio sample."); peak = Mathf.Max(peak, Mathf.Abs(value)); }
            Require(peak > .7f && peak < .95f, "Silent or clipped report: " + clip.name);
        }
        float bodyBound = Mathf.Pow(10, profile.FireBody.PitchAndVolumeInfo.VolumeMax / 20f);
        float snapBound = profile.FireSnap ? Mathf.Pow(10, profile.FireSnap.PitchAndVolumeInfo.VolumeMax / 20f) * .85f : 0;
        float tailBound = profile.FireTail ? Mathf.Pow(10, profile.FireTail.PitchAndVolumeInfo.VolumeMax / 20f) * .85f : 0;
        Require(bodyBound + snapBound + tailBound < .8f, "Shot layers exhausted mix headroom.");
    }

    private static void CheckRelease()
    {
        var root = new GameObject("Isolated discharge check");
        try
        {
            var release = root.AddComponent<HaloDischargeVisual>();
            var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/DollSinger/Prefabs/DollSingerNetworkPlayer.prefab")
                .GetComponentInChildren<DollSingerHaloAim>(true);
            release.Configure(source.beamCore.sharedMaterial);
            Require(release.Play(DollSingerHaloAim.HaloWeapon.Rifle, Color.red, 100), "First release rejected.");
            release.Step(.025f); float flash = release.Flash;
            Require(!release.Play(DollSingerHaloAim.HaloWeapon.Rifle, Color.red, 100) && Mathf.Approximately(flash, release.Flash),
                "Duplicate snapshot/RPC restarted the flash.");
            foreach (var vertex in root.GetComponent<MeshFilter>().sharedMesh.vertices)
                Require(new Vector2(vertex.x, vertex.y).magnitude > .14f && Mathf.Abs(vertex.z + .004f) < .000001f,
                    "Discharge covered the sight or left its plane.");
            release.Step(.2f);
            Require(!root.GetComponent<MeshRenderer>().enabled && release.Flash == 0 && release.VertexBudget == 144,
                "Discharge did not expire within its fixed budget.");
            release.Play(DollSingerHaloAim.HaloWeapon.Shotgun, Color.green, 101); release.Clear();
            Require(release.Flash == 0, "Switch/disable retained a discharge.");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    private static void CheckTrails()
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/DollSinger/Prefabs/DollSingerNetworkPlayer.prefab")
            .GetComponentInChildren<DollSingerHaloAim>(true);
        for (int weapon = 0; weapon < 4; weapon++)
        {
            var root = new GameObject("Spawn reconciliation trail check");
            try
            {
                bool revolver = weapon == 1, shotgun = weapon == 2, sniper = weapon == 3;
                var visual = root.AddComponent<HaloProjectileVisual>();
                visual.Configure(source, revolver, shotgun, sniper, false);
                visual.SetFlight(Vector3.zero, Vector3.forward * 100);
                foreach (var line in root.GetComponentsInChildren<LineRenderer>())
                    Require(Vector3.Distance(line.GetPosition(0), line.GetPosition(1)) <=
                        HaloProjectileVisual.TrailLengthLimit(revolver, shotgun, sniper) + .0001f,
                        "Snapshot arrival stretched the tracer beyond its weapon budget.");
                Require(!root.GetComponentInChildren<Light>().enabled, "A secondary pellet created flight illumination.");
                visual.SetFlight(Vector3.one, Vector3.one);
                foreach (var line in root.GetComponentsInChildren<LineRenderer>()) Require(!line.enabled, "Unemerged tracer remained visible.");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
    }

    private static void CheckImpactBudget(RifleFeedbackLibrary library)
    {
        var root = new GameObject("Isolated impact budget check");
        try
        {
            var feedback = root.AddComponent<RifleImpactFeedback>(); feedback.Warmup();
            int budget = 0;
            foreach (var ps in root.GetComponentsInChildren<ParticleSystem>()) budget += ps.main.maxParticles;
            Require(budget == 256 && root.GetComponentsInChildren<MeshRenderer>().Length == 16,
                "Impact/contact pool exceeded its budget.");
            for (uint i = 1; i <= 33; i++) feedback.Present(new ClientRifleImpactRpc { OwnerNetworkId = 1, ShotTick = i,
                WeaponId = DollSingerWeapons.Halo, Kind = (byte)RifleImpactKind.Metal, Normal = Vector3.forward }, false, true);
            Require(root.GetComponentsInChildren<MeshRenderer>().Length == 16, "Repeated hits grew the contact pool.");
            var before = root.GetComponentsInChildren<Transform>().Length;
            feedback.Present(new ClientRifleImpactRpc { OwnerNetworkId = 1, ShotTick = 33, WeaponId = DollSingerWeapons.Halo,
                Kind = (byte)RifleImpactKind.Metal, Normal = Vector3.forward }, true, true);
            Require(root.GetComponentsInChildren<Transform>().Length == before, "Confirmation allocated another effect.");
            // Plain MonoBehaviours do not receive Edit-mode lifecycle callbacks on this fixture.
            typeof(RifleImpactFeedback).GetMethod("OnDisable", System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic).Invoke(feedback, null);
            root.SetActive(false);
            foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true)) Require(!renderer.enabled, "Disabled pool retained a contact.");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException("Shooting audiovisual: " + message); }
}
