using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Unity.MP_FPS;
using Unity.MP_FPS.DollSinger;

// Opt-in local acceptance probe. Uses the actual prefab capsule, live frame updates,
// camera ray/button entry point and existing suit rule; never runs on the formal map.
public static class AirlockPlayValidation
{
    static string Evidence => s_ReportScene == AirlockApollo12Tools.ScenePath ?
        "Temp/MoonAirlockIntegration/DOT00_map_play_validation.json" : "Temp/MoonAirlockIntegration/DOT00_play_validation.json";
    static string s_ReportScene;
    const string RestoreKey = "Moonkov.EVA.DOT00.SceneSetup";
    [Serializable] class Setup { public SceneSetup[] scenes; }
    [Serializable] class Observation
    {
        public string check; public Vector3 actorLocal;
        public float pressureKPa, outer, inner, suitOxygen;
        public bool breathable;
    }
    [Serializable] class Report
    {
        public string utc, scene, status, failure, method;
        public string[] audioEvents;
        public bool pooledAudibleSourceObserved;
        public float maxListenerOutputRms, maxPumpOutputRms, maxOuterMotorOutputRms;
        public string characterPrefab;
        public Observation[] observations;
    }
    static readonly List<Observation> s_Observations = new List<Observation>();
    static readonly HashSet<string> s_AudioEvents = new HashSet<string>();
    static IEnumerator s_Run;
    static AirlockController s_Owner;
    static AirlockPrototypeDemo s_Demo;
    static CharacterController s_Capsule;
    static DollSingerMovement s_Movement;
    static DollSingerInput s_Input;
    static AirlockGameplayPlayer s_GameplayDriver;
    static AirlockAudio s_Audio;
    static Camera s_Camera;
    static int s_Frame;
    static double s_Deadline;
    static bool s_Audible, s_MovementEnabled, s_InputEnabled;
    static readonly float[] s_OutputSamples = new float[1024];
    static float s_OutputRms, s_PumpRms, s_OuterMotorRms;
    static Keyboard s_TestKeyboard;

    public static string Prepare(string scenePath = null)
    {
        if (EditorApplication.isPlaying || EditorApplication.isCompiling) throw new InvalidOperationException("Stable edit mode required");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Unsaved user scene; validation refuses to switch");
        var setup = EditorSceneManager.GetSceneManagerSetup();
        if (setup.Any(s => string.IsNullOrEmpty(s.path))) throw new InvalidOperationException("Unsaved scene path");
        scenePath = scenePath ?? AirlockCloudDriveTools.ScenePath;
        if (scenePath != AirlockCloudDriveTools.ScenePath && scenePath != AirlockApollo12Tools.ScenePath) throw new InvalidOperationException("Only isolated EVA scenes may be validated");
        SessionState.SetString(RestoreKey, JsonUtility.ToJson(new Setup { scenes = setup }));
        EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        return "Independent EVA scene loaded; original scene setup retained for restoration: " + scenePath;
    }

    public static string Begin()
    {
        if (!Application.isPlaying || s_Run != null) throw new InvalidOperationException("Require a single Play validation run");
        s_ReportScene = SceneManager.GetActiveScene().path;
        if (s_ReportScene != AirlockCloudDriveTools.ScenePath && s_ReportScene != AirlockApollo12Tools.ScenePath) throw new InvalidOperationException("Wrong scene");
        if (AirlockNetworkContext.HasNetworkWorlds) throw new InvalidOperationException("Local capsule validation requires no network simulation worlds");
        s_Owner = UnityEngine.Object.FindFirstObjectByType<AirlockController>();
        s_Demo = UnityEngine.Object.FindFirstObjectByType<AirlockPrototypeDemo>();
        if (!s_Owner || !s_Owner.Ready || !s_Demo || !s_Demo.Character) throw new InvalidOperationException("Actual character or bound airlock not ready");
        s_Capsule = s_Demo.Character.GetComponent<CharacterController>();
        s_Movement = s_Demo.Character.GetComponentInChildren<DollSingerMovement>(true);
        s_Input = s_Demo.Character.GetComponentInChildren<DollSingerInput>(true);
        s_GameplayDriver = s_Demo.Character.GetComponent<AirlockGameplayPlayer>();
        s_Camera = s_Demo.Character.GetComponentInChildren<DollSingerView>(true).camera;
        s_Audio = s_Owner.GetComponent<AirlockAudio>();
        if (!s_Capsule || !s_Movement || !s_Camera || !s_Audio) throw new InvalidOperationException("Missing runtime wiring");
        s_MovementEnabled = s_Movement.enabled; s_Movement.enabled = false;
        if (s_GameplayDriver) s_GameplayDriver.enabled = false;
        if (s_Input) { s_InputEnabled = s_Input.enabled; s_Input.enabled = false; }
        s_Observations.Clear(); s_AudioEvents.Clear(); s_Audible = false;
        s_OutputRms = s_PumpRms = s_OuterMotorRms = 0;
        s_Audio.EventRaised += AudioEvent;
        s_Frame = -1; s_Deadline = EditorApplication.timeSinceStartup + 150;
        s_Run = Cycle(); EditorApplication.update += Advance;
        return "Live capsule cycle running; result will be written to " + Evidence;
    }
    static void AudioEvent(AirlockSoundEvent evt, bool clip) => s_AudioEvents.Add(evt + (clip ? ":clip" : ":pending"));
    static Vector3 Local => s_Owner.transform.InverseTransformPoint(s_Capsule.transform.position);
    static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
    static void Observe(string check)
    {
        var s = s_Owner.State;
        s_Observations.Add(new Observation { check = check, actorLocal = Local,
            pressureKPa = s.PressureKPa, outer = s.OuterFraction, inner = s.InnerFraction,
            suitOxygen = s_Demo.Health.Oxygen, breathable = s_Demo.Health.BreathableAir });
    }
    static void Press(string suffix, AirlockFeedback expected)
    {
        var b = AirlockButton.Find(s_Owner.AirlockId + "." + suffix);
        Require(b && b.InRange(s_Capsule.transform.position), "Button out of actual capsule range: " + suffix);
        var collider = b.GetComponent<Collider>();
        // Point the actual player camera at the visible control, then use the gameplay ray entry point.
        s_Camera.transform.position = s_Capsule.transform.position + Vector3.up * 1.35f;
        s_Camera.transform.rotation = Quaternion.LookRotation(collider.bounds.center - s_Camera.transform.position);
        Require(Physics.Raycast(s_Camera.ViewportPointToRay(new Vector3(.5f, .5f)), out var hit, 4, ~0, QueryTriggerInteraction.Ignore)
            && hit.collider.GetComponentInParent<AirlockButton>() == b, "Button ray blocked: " + suffix + (hit.collider ? " by " + hit.collider.name : ""));
        Require(AirlockButton.TryPressFromCamera(s_Camera, s_Capsule.transform.position), "Gameplay button ray did not press " + suffix);
        Require(s_Owner.State.Feedback == expected, suffix + ": expected " + expected + ", got " + s_Owner.State.Feedback);
        var text = b.GetComponentInChildren<TextMesh>();
        // Cloud status text is a separate child of the interaction object.
        Require(text && text.text.Contains("kPa") && text.text.Contains(AirlockButton.FeedbackText(expected)), "Button status text mismatch " + suffix);
        Observe(suffix + " -> " + expected);
    }
    static IEnumerator Hold(float seconds)
    {
        float until = Time.time + seconds;
        while (Time.time < until) { s_Capsule.Move(Vector3.down * 2 * Time.deltaTime); yield return null; }
    }
    static IEnumerator Wait(Func<bool> ready, float limit, string label)
    {
        float until = Time.time + limit;
        while (!ready())
        { Require(Time.time < until, "Timed out: " + label); s_Capsule.Move(Vector3.down * 2 * Time.deltaTime); yield return null; }
        yield return null;
    }
    static IEnumerator Walk(float x, float z)
    {
        float until = Time.time + 15;
        while (new Vector2(Local.x - x, Local.z - z).magnitude > .08f)
        {
            Require(Time.time < until, "Capsule route blocked at local " + Local + ", target " + x + "," + z);
            var target = s_Owner.transform.TransformPoint(new Vector3(x, Local.y, z));
            var delta = target - s_Capsule.transform.position; delta.y = 0;
            s_Capsule.Move(Vector3.ClampMagnitude(delta, 2 * Time.deltaTime) + Vector3.down * 2 * Time.deltaTime);
            Require(Local.y > -.4f && Local.y < 1, "Capsule left native floor: " + Local);
            yield return null;
        }
        Observe("Walk reached " + x + "," + z);
    }
    static IEnumerable<IEnumerator> Steps()
    {
        float oxygen = s_Demo.Health.Oxygen;
        yield return Hold(1);
        Require(!s_Demo.Health.BreathableAir && s_Demo.Health.Oxygen < oxygen, "Outside vacuum did not consume suit oxygen");
        Observe("Outside vacuum drains existing suit reserve");
        if (s_GameplayDriver) yield return PressOuterWithF();
        else Press("OUTSIDE.OPEN_OUTER", AirlockFeedback.Accepted);
        yield return Wait(() => s_Owner.State.OuterFraction == 1, 4, "outer open");
        if (s_GameplayDriver) Require(s_OuterMotorRms > .000001f && s_Audio.AirGain == 0 && s_Audio.ContactGain > 0,
            "Metal-ramp motor sound did not reach listener through structure contact in vacuum");
        yield return Walk(0, -4.35f);
        Press("CHAMBER.CLOSE_OUTER", AirlockFeedback.Obstructed);
        yield return Hold(.2f);
        Require(s_Owner.State.OuterFraction == 1 && !s_Audio.OuterMotorActive, "Occupied aperture moved or stopped motor kept playing");
        Observe("Actual capsule in aperture blocks closing; stopped motor silent");
        yield return Walk(0, -3);
        Press("CHAMBER.CLOSE_OUTER", AirlockFeedback.Accepted);
        yield return Walk(0, -4.35f);
        yield return Wait(() => s_Owner.State.OuterFraction == 1, 4, "intrusion safety reopen");
        Require(s_Owner.State.OuterOpening, "Closing door did not reverse on actual capsule intrusion");
        Observe("Mid-close native capsule intrusion safely reopens outer door");
        yield return Walk(0, -3);
        Press("CHAMBER.CLOSE_OUTER", AirlockFeedback.Accepted);
        yield return Wait(() => s_Owner.State.OuterFraction == 0, 4, "outer seal");
        Press("CHAMBER.OPEN_INNER", AirlockFeedback.PressureMismatch);
        Press("CHAMBER.EQUALIZE_INSIDE", AirlockFeedback.Accepted);
        Press("CHAMBER.OPEN_INNER", AirlockFeedback.Equalizing);
        yield return Wait(() => !s_Owner.State.Equalizing, 9, "pressurize");
        if (s_GameplayDriver) Require(s_PumpRms > .000001f, "Pump sound did not reach actual AudioListener");
        Require(s_Owner.State.PressureKPa == s_Owner.Settings.InsidePressureKPa && s_Demo.Health.BreathableAir, "Chamber did not become breathable");
        oxygen = s_Demo.Health.Oxygen;
        yield return Hold(1);
        Require(s_Demo.Health.Oxygen >= oxygen, "Pressurized chamber still drains suit oxygen");
        Require(!BreathableZone.Contains(s_Owner.transform.TransformPoint(new Vector3(3, 1, -2.5f))) &&
            !BreathableZone.Contains(s_Owner.transform.TransformPoint(new Vector3(0, 1, -6.24f))), "Segment union included exterior vacuum");
        foreach (var point in new[] { new Vector3(0, 1, -2.5f), new Vector3(0, 1, -.72f), new Vector3(3, 1, 1.8f) })
            Require(BreathableZone.Contains(s_Owner.transform.TransformPoint(point)), "Chamber segment missing " + point);
        Observe("Three-segment union breathable; exterior vacuum excluded; suit drain stops");
        Press("CHAMBER.OPEN_INNER", AirlockFeedback.Accepted);
        yield return Wait(() => s_Owner.State.InnerFraction == 1, 4, "inner open");
        Require(!AirlockButton.Find(s_Owner.AirlockId + ".CHAMBER.OPEN_OUTER").InRange(s_Capsule.transform.position), "Far inner control incorrectly considered in range");
        yield return Walk(0, 5.2f);
        oxygen = s_Demo.Health.Oxygen;
        yield return Hold(1);
        Require(s_Demo.Health.BreathableAir && s_Demo.Health.Oxygen >= oxygen, "Inside gateway drains suit oxygen");
        Observe("Actual capsule crossed inner door; pressurized inside stops suit drain");
        yield return Walk(0, 3);
        Press("CHAMBER.OPEN_OUTER", AirlockFeedback.OtherDoorNotSealed);
        Press("CHAMBER.EQUALIZE_OUTSIDE", AirlockFeedback.DoorsNotSealed);
        Press("CHAMBER.CLOSE_INNER", AirlockFeedback.Accepted);
        yield return Wait(() => s_Owner.State.InnerFraction == 0, 4, "inner seal");
        Press("CHAMBER.OPEN_OUTER", AirlockFeedback.PressureMismatch);
        Press("CHAMBER.EQUALIZE_OUTSIDE", AirlockFeedback.Accepted);
        yield return Wait(() => !s_Owner.State.Equalizing, 9, "depressurize");
        Require(!s_Demo.Health.BreathableAir && s_Owner.State.PressureKPa == s_Owner.Settings.OutsidePressureKPa, "Chamber remains breathable after venting");
        oxygen = s_Demo.Health.Oxygen; yield return Hold(1);
        Require(s_Demo.Health.Oxygen < oxygen, "Vented chamber did not resume suit consumption");
        Observe("Depressurized chamber resumes suit drain");
        Press("CHAMBER.OPEN_OUTER", AirlockFeedback.Accepted);
        yield return Wait(() => s_Owner.State.OuterFraction == 1, 4, "outbound outer open");
        yield return Walk(0, -6.24f);
        yield return Hold(.3f);
        Require(!s_Demo.Health.BreathableAir && s_Audio.AirGain == 0, "Exterior vacuum carried airborne mechanism sound");
        foreach (var evt in new[] { "ValveFlow", "Pump", "OuterMotor", "InnerMotor", "OuterLocked", "InnerLocked", "OuterUnlock", "InnerUnlock" })
            Require(s_AudioEvents.Contains(evt + ":clip"), "Missing actual bound audio event " + evt);
        Require(s_Audible, "No nonzero pooled audio source observed in pressurized chamber");
        Observe("Returned outside through actual capsule route; air sound muted in vacuum");
    }
    static IEnumerator Cycle()
    {
        foreach (var step in Steps()) while (step.MoveNext()) yield return null;
    }
    static IEnumerator PressOuterWithF()
    {
        var b = AirlockButton.Find(s_Owner.AirlockId + ".OUTSIDE.OPEN_OUTER");
        Require(b && b.InRange(s_Capsule.transform.position), "Outside F control not in range");
        s_Camera.transform.position = s_Capsule.transform.position + Vector3.up * 1.35f;
        s_Camera.transform.rotation = Quaternion.LookRotation(b.GetComponent<Collider>().bounds.center - s_Camera.transform.position);
        Cursor.lockState = CursorLockMode.Locked;
        s_TestKeyboard = InputSystem.AddDevice<Keyboard>();
        InputSystem.QueueStateEvent(s_TestKeyboard, new KeyboardState(Key.F));
        float until = Time.time + 1;
        while (!s_Owner.State.OuterOpening)
        { Require(Time.time < until, "F key did not trigger gameplay interaction"); yield return null; }
        InputSystem.RemoveDevice(s_TestKeyboard); s_TestKeyboard = null;
        Observe("Input System F key opens outer door through actual Demo.Update and camera ray");
    }
    static void Advance()
    {
        if (!Application.isPlaying) { Finish("failed", "Play interrupted"); return; }
        if (Time.frameCount == s_Frame) return;
        s_Frame = Time.frameCount;
        try
        {
            Require(EditorApplication.timeSinceStartup < s_Deadline, "Overall cycle timeout");
            AudioListener.GetOutputData(s_OutputSamples, 0);
            double square = 0; foreach (float v in s_OutputSamples) square += v * v;
            float rms = (float)Math.Sqrt(square / s_OutputSamples.Length);
            s_OutputRms = Mathf.Max(s_OutputRms, rms);
            if (s_Audio.PumpActive) s_PumpRms = Mathf.Max(s_PumpRms, rms);
            if (s_Audio.OuterMotorActive) s_OuterMotorRms = Mathf.Max(s_OuterMotorRms, rms);
            if (!s_Audible) s_Audible = UnityEngine.Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None)
                .Any(a => a.isPlaying && a.volume > 0 && a.clip && a.clip.name.Contains("_Loop"));
            if (!s_Run.MoveNext()) Finish("passed", null);
        }
        catch (Exception e) { Finish("failed", e.Message); }
    }
    static void Finish(string status, string failure)
    {
        EditorApplication.update -= Advance; s_Run = null;
        if (s_Audio) s_Audio.EventRaised -= AudioEvent;
        if (s_Movement) s_Movement.enabled = s_MovementEnabled;
        if (s_Input) s_Input.enabled = s_InputEnabled;
        if (s_GameplayDriver) s_GameplayDriver.enabled = true;
        if (s_TestKeyboard != null) { InputSystem.RemoveDevice(s_TestKeyboard); s_TestKeyboard = null; }
        Directory.CreateDirectory(Path.GetDirectoryName(Evidence));
        File.WriteAllText(Evidence, JsonUtility.ToJson(new Report { utc = DateTime.UtcNow.ToString("o"),
            scene = s_ReportScene, status = status, failure = failure,
            method = "Live Play frames; actual DollSinger prefab CharacterController.Move with native collision and floor. Movement/input temporarily disabled for scripted route; real camera ray presses gameplay buttons. Existing RaidHealth tick drives suit diagnostics. No physical keyboard input, network peers or subjective audio audition.",
            audioEvents = s_AudioEvents.OrderBy(x => x).ToArray(), pooledAudibleSourceObserved = s_Audible,
            maxListenerOutputRms = s_OutputRms, maxPumpOutputRms = s_PumpRms, maxOuterMotorOutputRms = s_OuterMotorRms,
            characterPrefab = AssetDatabase.GetAssetPath(new SerializedObject(s_Demo).FindProperty("m_CharacterPrefab").objectReferenceValue),
            observations = s_Observations.ToArray() }, true));
        Debug.Log("EVA actual capsule validation " + status + (failure == null ? "" : ": " + failure));
    }
    public static string Restore()
    {
        if (EditorApplication.isPlaying || EditorApplication.isCompiling) throw new InvalidOperationException("Stop Play and wait for compilation first");
        var json = SessionState.GetString(RestoreKey, "");
        if (string.IsNullOrEmpty(json)) throw new InvalidOperationException("No saved original scene setup");
        var setup = JsonUtility.FromJson<Setup>(json);
        EditorSceneManager.RestoreSceneManagerSetup(setup.scenes);
        SessionState.EraseString(RestoreKey);
        return "Original scene setup restored without saving Play changes";
    }
}
