using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.Collections;
using Unity.MP_FPS;
using Unity.MP_FPS.Client;
using Unity.MP_FPS.DollSinger;
using Unity.NetCode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

public static class HaloLightingChecks
{
    private sealed class LightingCheckWindow : EditorWindow { }
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void Require(bool value, string message, List<string> passed)
    {
        if (!value) throw new InvalidOperationException("Halo lighting: " + message);
        passed.Add(message);
    }

    [MenuItem("Tools/Moonkov/Check Halo Lighting")]
    public static string Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Run in Edit mode.");
        var passed = new List<string>();
        var press = new HaloLightPress();
        Require(!press.Step(true, true, 0, true), "key down does not toggle", passed);
        Require(press.Step(false, false, .15f, true), "short release toggles once", passed);
        Require(!press.Step(false, false, .16f, true), "release is not repeated", passed);
        press.Step(true, true, 1, true); press.Step(false, true, 1.31f, true);
        Require(press.Adjusting, "hold opens adjustment after 0.3 seconds", passed);
        Require(!press.Step(false, false, 2, true) && !press.Adjusting, "long release closes without toggle", passed);
        press.Step(true, true, 3, true); press.Step(false, true, 3.31f, true); press.Step(false, true, 3.4f, false);
        Require(!press.Adjusting && !press.Step(false, false, 3.45f, true), "menu/focus cancellation does not toggle", passed);
        Require(HaloLighting.Range(0) == 3 && HaloLighting.AimRange(0) == 80 && HaloLighting.IdlePercent(0) == 100 && HaloLighting.AimPercent(0) == 100,
            "zero state preserves existing defaults", passed);
        uint settings = HaloLighting.Pack(8.7f, 35, 180, 110);
        Require(Mathf.Approximately(HaloLighting.Range(settings), 8.7f) && HaloLighting.IdlePercent(settings) == 35 &&
            HaloLighting.AimPercent(settings) == 180 && HaloLighting.AimRange(settings) == 110, "both ranges and both intensities remain independent", passed);
        uint hostile = new PlayerInput { HaloLightingSettings = uint.MaxValue }.Sanitized().HaloLightingSettings;
        Require(HaloLighting.Range(hostile) == 12 && HaloLighting.IdlePercent(hostile) == 200 &&
            HaloLighting.AimPercent(hostile) == 200 && HaloLighting.AimRange(hostile) == 160, "untrusted command values are bounded", passed);
        Require(HaloLighting.Range(HaloLighting.Pack(float.NaN, -10, 999)) == 3,
            "non-finite range is replaced safely", passed);
        var command = new PlayerInput { HaloLightingSettings = settings };
        var buffered = new PlayerInput(); buffered.UpdateFrom(command);
        Require(buffered.HaloLightingSettings == settings, "continuous command buffering retains lighting", passed);
        buffered.UpdateFrom(new PlayerInput(), false);
        Require(buffered.HaloLightingSettings == settings, "event-only buffering cannot erase lighting", passed);
        var state = new FirstPersonController.ControllerState();
        FirstPersonController.ProcessInputs(ref state, command.Sanitized(), .02f);
        Require(state.HaloLightingSettings == settings, "server simulation applies lighting intent", passed);
        CheckSerialization(command, state, passed);
        CheckRestrictedLighting(passed);
        CheckPresentation(settings, passed);
        string result = "Passed " + passed.Count + " halo lighting checks:\n" + string.Join("\n", passed);
        System.IO.Directory.CreateDirectory("Temp/HaloLightingValidation");
        System.IO.File.WriteAllText("Temp/HaloLightingValidation/checks.txt", result);
        return result;
    }

    // Small contract check: no windows, preview scenes, network worlds or clients.
    public static string RunReplicationContract()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Run in Edit mode.");
        var passed = new List<string>();
        CheckRestrictedLighting(passed);
        return "Passed " + passed.Count + " restricted-light replication checks:\n" + string.Join("\n", passed);
    }

    private static void CheckRestrictedLighting(List<string> passed)
    {
        uint custom = HaloLighting.Pack(9.4f, 45, 165, 125);
        foreach (var sample in new[] {
            (Life: ClashLife.Downed, Settings: custom, Enabled: false),
            (Life: ClashLife.Waiting, Settings: uint.MaxValue, Enabled: true),
            (Life: ClashLife.Downed, Settings: 0u, Enabled: true) })
        {
            var ghost = new PredictedPlayerGhost { ClashEnabled = true, ClashCanAct = false, ClashLife = sample.Life };
            var requested = new PlayerInput { HaloLightingSettings = sample.Settings,
                MoveInput = new Unity.Mathematics.float2(1, 1), Lean = 1, FreeLooking = true,
                InputFlags = (uint)(PlayerInput.InputFlag.Jump | PlayerInput.InputFlag.Shoot | PlayerInput.InputFlag.Sprint |
                    PlayerInput.InputFlag.Reload | PlayerInput.InputFlag.Aim | PlayerInput.InputFlag.EquipHalo |
                    PlayerInput.InputFlag.EquipRevolver | PlayerInput.InputFlag.EquipPistol | PlayerInput.InputFlag.ThirdPerson) };
            requested.SetFlag(PlayerInput.InputFlag.HaloLightDisabled, !sample.Enabled);
            var restricted = RaidHealth.RestrictInput(ghost, requested);
            uint expected = HaloLighting.Sanitize(sample.Settings);
            string label = sample.Life + "/" + sample.Settings + ": ";
            Require(restricted.HaloLightingSettings == expected && restricted.HaloLightEnabled == sample.Enabled,
                label + "incapacitation retains bounded light settings and switch state", passed);
            Require(!restricted.Jump && !restricted.Shoot && !restricted.Sprint && !restricted.Reload && !restricted.Aim &&
                !restricted.EquipHalo && !restricted.EquipRevolver && !restricted.EquipPistol &&
                Unity.Mathematics.math.all(restricted.MoveInput == Unity.Mathematics.float2.zero) &&
                restricted.Lean == 0 && !restricted.FreeLooking,
                label + "movement, combat and equipment actions remain suppressed", passed);
            Require(sample.Life != ClashLife.Downed || restricted.Stance == DollSingerStance.Prone,
                label + "downed stance remains prone", passed);
            FirstPersonController.ProcessInputs(ref ghost.ControllerState, restricted.Sanitized(), .02f);
            Require(ghost.ControllerState.HaloLightingSettings == expected && ghost.ControllerState.HaloLightEnabled == sample.Enabled,
                label + "authoritative controller retains the same light state", passed);
            CheckSerialization(restricted, ghost.ControllerState, passed);
        }
    }

    private static void CheckSerialization(PlayerInput input, FirstPersonController.ControllerState state, List<string> passed)
    {
        var assembly = typeof(ClientCommandInput).Assembly;
        var commandType = assembly.GetType("Assembly_CSharp_Generated.Assembly_CSharp_Generated_ClientCommandInputSerializer", true);
        var serializer = (ICommandDataSerializer<ClientCommandInput>)Activator.CreateInstance(commandType);
        var bytes = new NativeArray<byte>(512, Allocator.Temp);
        var writer = new DataStreamWriter(bytes);
        try
        {
            var sent = new ClientCommandInput { PlayerInput = input };
            serializer.Serialize(ref writer, default, sent);
            var reader = new DataStreamReader(writer.AsNativeArray());
            var received = new ClientCommandInput();
            serializer.Deserialize(ref reader, default, ref received);
            Require(received.PlayerInput.HaloLightingSettings == input.HaloLightingSettings &&
                received.PlayerInput.HaloLightEnabled == input.HaloLightEnabled,
                "generated command serializer round trip preserves light parameters and switch intent", passed);
        }
        finally { bytes.Dispose(); }
        var ghostType = assembly.GetType("Assembly_CSharp_Generated.Assembly_CSharp_Generated_PredictedPlayerGhostGhostComponentSerializer", true);
        var snapshotType = ghostType.GetNestedType("Snapshot", BindingFlags.NonPublic);
        object snapshot = Activator.CreateInstance(snapshotType);
        object[] to = { default(GhostSerializerState), snapshot, new PredictedPlayerGhost { ControllerState = state } };
        ghostType.GetMethod("CopyToSnapshotGenerated", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, to);
        object[] from = { default(GhostDeserializerState), new PredictedPlayerGhost(), 0f, 0f, to[1], to[1] };
        ghostType.GetMethod("CopyFromSnapshotGenerated", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, from);
        var observed = ((PredictedPlayerGhost)from[1]).ControllerState;
        Require(observed.HaloLightingSettings == input.HaloLightingSettings && observed.HaloLightEnabled == input.HaloLightEnabled,
            "generated snapshot mapping preserves observer and late-join light parameters and switch state", passed);
    }

    private static void CheckPresentation(uint settings, List<string> passed)
    {
        var scene = EditorSceneManager.NewPreviewScene();
        var player = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/DollSinger/Prefabs/DollSingerNetworkPlayer.prefab"));
        SceneManager.MoveGameObjectToScene(player, scene);
        HaloLightPanel panel = null;
        LightingCheckWindow window = null;
        try
        {
            foreach (var b in player.GetComponentsInChildren<MonoBehaviour>(true)) b.enabled = false;
            foreach (var camera in player.GetComponentsInChildren<Camera>(true)) camera.enabled = false;
            var halo = player.GetComponentInChildren<DollSingerHaloAim>(true);
            halo.gameObject.SetActive(true);
            halo.GetComponent<Animator>().Rebind(); halo.GetComponent<Animator>().Update(0);
            typeof(DollSingerHaloAim).GetMethod("Awake", Private).Invoke(halo, null);
            typeof(DollSingerHaloAim).GetField("bakedGestures", Private).SetValue(halo, null);
            halo.LightingSettings = settings;
            foreach (DollSingerHaloAim.HaloWeapon weapon in new[] { DollSingerHaloAim.HaloWeapon.Rifle, DollSingerHaloAim.HaloWeapon.Revolver,
                DollSingerHaloAim.HaloWeapon.Shotgun, DollSingerHaloAim.HaloWeapon.Sniper })
            {
                halo.SetNetworkWeapon(weapon, 4, false, 0, 0, 0);
                halo.SetNetworkPresentation(true, Vector3.forward, Vector3.zero);
                typeof(DollSingerHaloAim).GetField("aimBlend", Private).SetValue(halo, 1f);
                typeof(DollSingerHaloAim).GetMethod("LateUpdate", Private).Invoke(halo, null);
                Color expected = weapon == DollSingerHaloAim.HaloWeapon.Rifle ? halo.rifleVisual.lightColor :
                    weapon == DollSingerHaloAim.HaloWeapon.Revolver ? halo.revolverVisual.lightColor :
                    weapon == DollSingerHaloAim.HaloWeapon.Shotgun ? halo.shotgunVisual.lightColor : halo.sniperVisual.lightColor;
                Require(halo.haloLight.color == expected && halo.haloFaceLightFront.color == expected && halo.haloFaceLightBack.color == expected,
                    weapon + " all three lights follow the halo colour while aiming", passed);
                Require(Mathf.Approximately(halo.haloFaceLightFront.range, 110), weapon + " aimed range responds independently", passed);
                Require(Mathf.Approximately(halo.haloFaceLightFront.intensity, halo.flashlightIntensity * 1.8f),
                    weapon + " aimed intensity responds to the selected setting", passed);
            }
            typeof(DollSingerHaloAim).GetField("aimBlend", Private).SetValue(halo, 0f);
            typeof(DollSingerHaloAim).GetMethod("LateUpdate", Private).Invoke(halo, null);
            Require(Mathf.Approximately(halo.haloLight.range, 8.7f) && Mathf.Approximately(halo.haloLight.intensity, halo.idleLightIntensity * .35f),
                "leaving aim restores independent everyday range and intensity", passed);
            halo.SetNetworkPresentation(false, Vector3.forward, Vector3.zero, false);
            typeof(DollSingerHaloAim).GetMethod("LateUpdate", Private).Invoke(halo, null);
            Require(!halo.haloLight.enabled && !halo.haloFaceLightFront.enabled && !halo.haloFaceLightBack.enabled,
                "light switch disables all three lights", passed);
            var input = halo.GetComponent<DollSingerInput>();
            input.LightingSettings = settings;
            var press = new HaloLightPress(); press.Step(true, true, 0, true); press.Step(false, true, .4f, true);
            typeof(DollSingerInput).GetField("lightPress", Private).SetValue(input, press);
            window = ScriptableObject.CreateInstance<LightingCheckWindow>(); window.ShowUtility();
            var host = window.rootVisualElement; panel = new HaloLightPanel(host, input, halo); panel.Refresh(true);
            host.Q<Slider>("haloRange").value = 5.2f;
            host.Q<SliderInt>("haloIdleStrength").value = 42;
            host.Q<SliderInt>("haloAimStrength").value = 160;
            host.Q<Slider>("haloAimRange").value = 130;
            Require(Mathf.Approximately(HaloLighting.Range(input.LightingSettings), 5.2f) && HaloLighting.IdlePercent(input.LightingSettings) == 42 &&
                HaloLighting.AimPercent(input.LightingSettings) == 160 && HaloLighting.AimRange(input.LightingSettings) == 130, "UI slider events update all four settings (actual " + HaloLighting.Range(input.LightingSettings) + ", " + HaloLighting.IdlePercent(input.LightingSettings) + ", " + HaloLighting.AimPercent(input.LightingSettings) + ")", passed);
            panel.Dispose(); panel = null;
            Require(host.childCount == 0, "panel teardown removes UI and listeners", passed);
        }
        finally
        {
            panel?.Dispose();
            if (window) window.Close();
            UnityEngine.Object.DestroyImmediate(player);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }
}
