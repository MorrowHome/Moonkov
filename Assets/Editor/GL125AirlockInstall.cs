using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using Unity.MP_FPS;
using Unity.MP_FPS.Moon.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class GL125AirlockInstall
{
    private const string ScenePath = SourceWorldSceneInstall.ScenePath;
    private const string AssetsPath = "Assets/MoonEnvironment/Variants/GL125/Airlocks";
    private const string LabelFontPath = "Assets/UnityTechnologies/ParticlePack/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";
    private const string StationsName = "GL125 Airlock controls";
    private const float ConsoleHeight = 1.3f;
    private static readonly Vector3 Up = Vector3.up;
    private static int SolidLayers => LayerMask.GetMask("Default", "Ground");

    // Jamb centers, floor and inner seal plane measured in the evaluated GL125 entry meshes.
    private static readonly Entry[] Additional =
    {
        new("EVA_WEST_CARGO", new Vector3(1587.85f, 211.636f, 2116.38f), new Vector3(.7986355f, 0, .601815f),
            5.7f, 4.5f, 5.94f, 4.54f, 3.39f, 8.4f, 5.15f),
        new("EVA_NORTH_CREW", new Vector3(1592.78f, 211.636f, 2177.88f), new Vector3(.601815f, 0, -.7986355f),
            4.2f, 3.4f, 4.44f, 3.44f, 3.34f, 6.2f, 4.1f)
    };

    private readonly struct Entry
    {
        public readonly string Id;
        public readonly Vector3 Origin, Forward;
        public readonly float OuterWidth, OuterHeight, InnerWidth, InnerHeight, InnerDepth, ChamberWidth, ChamberHeight;

        public Entry(string id, Vector3 origin, Vector3 forward, float outerWidth, float outerHeight,
            float innerWidth, float innerHeight, float innerDepth, float chamberWidth, float chamberHeight)
        {
            Id = id;
            Origin = origin;
            Forward = forward;
            OuterWidth = outerWidth;
            OuterHeight = outerHeight;
            InnerWidth = innerWidth;
            InnerHeight = innerHeight;
            InnerDepth = innerDepth;
            ChamberWidth = chamberWidth;
            ChamberHeight = chamberHeight;
        }
    }

    public static string Install(string receiptDirectory)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || BuildPipeline.isBuildingPlayer)
            throw new InvalidOperationException("The existing Unity Editor must be idle in Edit Mode.");
        var previous = SceneManager.GetActiveScene();
        var scene = SceneManager.GetSceneByPath(ScenePath);
        if (scene.isLoaded) throw new InvalidOperationException("Close the formal map before installing the airlocks.");
        Directory.CreateDirectory(receiptDirectory);
        string backup = Path.Combine(receiptDirectory, "TheophilusGameScene.before-GL125Airlocks.unity");
        if (!File.Exists(backup)) File.Copy(ScenePath, backup);
        var steel = MaterialAsset("Pressure door steel", new Color(.21f, .24f, .27f), .45f, Color.black);
        var amber = MaterialAsset("Airlock control amber", new Color(.89f, .48f, .09f), .12f, new Color(.17f, .07f, .012f));
        scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        try
        {
            SceneManager.SetActiveScene(scene);
            var roots = scene.GetRootGameObjects();
            var world = roots.Single(r => r.name == "GL125 World").transform;
            var structure = world.Find("MC00 structural collision");
            var east = roots.SelectMany(r => r.GetComponentsInChildren<AirlockController>()).Single(c => c.AirlockId == "EVA_EAST_01");
            foreach (var old in roots.Where(r => Additional.Any(gate => r.name == gate.Id))) Object.DestroyImmediate(old);
            var controllers = new[] { east }.Concat(Additional.Select(gate => CreateGate(gate, structure, steel, amber))).ToArray();
            foreach (var controller in controllers) InstallControls(controller, steel, amber);
            string checks = Verify(controllers);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save GL125 airlocks.");
            File.WriteAllText(Path.Combine(receiptDirectory, "UnityAirlocks.txt"), checks);
            return checks;
        }
        finally
        {
            EditorSceneManager.CloseScene(scene, true);
            SceneManager.SetActiveScene(previous);
        }
    }

    private static AirlockController CreateGate(Entry gate, Transform structure, Material steel, Material amber)
    {
        var root = new GameObject(gate.Id).transform;
        root.SetPositionAndRotation(gate.Origin, Quaternion.LookRotation(gate.Forward));
        const float outerDepth = -.08f;
        var outer = MakeDoor(root, "OUTER", gate.OuterWidth, gate.OuterHeight, outerDepth, steel, amber);
        var inner = MakeDoor(root, "INNER", gate.InnerWidth, gate.InnerHeight, gate.InnerDepth, steel, amber);
        var outerSensor = Volume(root, "Outer aperture", new Vector3(0, gate.OuterHeight / 2, outerDepth),
            new Vector3(gate.OuterWidth, gate.OuterHeight, .7f));
        var innerSensor = Volume(root, "Inner aperture", new Vector3(0, gate.InnerHeight / 2, gate.InnerDepth),
            new Vector3(gate.InnerWidth, gate.InnerHeight, .7f));

        float side = (gate.ChamberWidth - gate.InnerWidth) / 2;
        foreach (float sign in new[] { -1f, 1f })
            Part(root, "Inner bulkhead side", new Vector3(sign * (gate.InnerWidth + side) / 2, gate.ChamberHeight / 2, gate.InnerDepth),
                new Vector3(side + .04f, gate.ChamberHeight, .21f), steel, true);
        Part(root, "Inner bulkhead header", new Vector3(0, (gate.InnerHeight + gate.ChamberHeight) / 2, gate.InnerDepth),
            new Vector3(gate.InnerWidth, gate.ChamberHeight - gate.InnerHeight, .21f), steel, true);

        var chamber = Volume(root, "Chamber pressure", new Vector3(0, gate.ChamberHeight / 2, gate.InnerDepth / 2),
            new Vector3(gate.ChamberWidth, gate.ChamberHeight, gate.InnerDepth)).gameObject.AddComponent<BreathableZone>();
        var inside = Volume(root, "Interior pressure", new Vector3(0, gate.InnerHeight / 2, gate.InnerDepth + 1.45f),
            new Vector3(gate.InnerWidth, gate.InnerHeight, 2.9f)).gameObject.AddComponent<BreathableZone>();
        inside.AllowsSuitRefill = false;
        var owner = root.gameObject.AddComponent<AirlockController>();
        owner.Configure(gate.Id, new AirlockSettings { ChamberVolumeM3 = gate.InnerDepth * gate.ChamberWidth * gate.ChamberHeight },
            outer, inner, outerSensor, innerSensor, chamber, inside);
        owner.ConfigureApertureBridges(outerSensor.gameObject.AddComponent<BreathableZone>(), innerSensor.gameObject.AddComponent<BreathableZone>());
        SoundDef Sound(string name) => AssetDatabase.LoadAssetAtPath<SoundDef>("Assets/Audio/Moonkov/Airlock/SoundDefs/" + name + ".asset");
        var audio = root.gameObject.AddComponent<AirlockAudio>();
        audio.Configure(owner, null, chamber.transform, chamber.transform, outerSensor.transform, innerSensor.transform,
            Sound("ValveAir_Loop"), Sound("Pump_Loop"), Sound("DoorMotor_Loop"), Sound("LatchLock"), Sound("LatchUnlock"),
            outerSensor.transform, innerSensor.transform);
        audio.ConfigureStructure(structure);
        return owner;
    }

    private static AirlockPanelPose[] MakeDoor(Transform root, string side, float width, float height, float depth,
        Material steel, Material amber)
    {
        var panels = new List<AirlockPanelPose>(6);
        float segment = width / 6;
        foreach (float sign in new[] { -1f, 1f })
        for (int i = 0; i < 3; i++)
        {
            string id = $"{root.name}.{side}.{(sign < 0 ? 'L' : 'R')}{i + 1}";
            var motion = Child(root, id, new Vector3(sign * (i + .5f) * segment, height * .5f, depth));
            Part(motion, "Door leaf", Vector3.zero, new Vector3(segment + .018f, height - .012f, .106f), steel, true);
            Part(motion, "Seal stripe", new Vector3(0, -height * .21f, 0), new Vector3(segment, .09f, .115f), amber);
            panels.Add(new AirlockPanelPose { PanelId = id, Motion = motion, ClosedPosition = motion.localPosition,
                OpenPosition = new Vector3(sign * (width * .5f + segment * .5f + .035f), height * .5f, depth + (i - 1) * .115f) });
        }
        return panels.ToArray();
    }

    private static void InstallControls(AirlockController owner, Material steel, Material amber)
    {
        foreach (var button in owner.GetComponentsInChildren<AirlockButton>(true))
            Object.DestroyImmediate(button.gameObject);
        foreach (var visual in owner.GetComponentsInChildren<MeshRenderer>(true).Where(r =>
            r.name.StartsWith(owner.AirlockId + "_", StringComparison.Ordinal) &&
            (r.name.EndsWith("_Button", StringComparison.Ordinal) || r.name.EndsWith("_IndicatorSurface", StringComparison.Ordinal) ||
             r.name.EndsWith("_StaticMount", StringComparison.Ordinal))))
            if (visual) Object.DestroyImmediate(visual.gameObject);
        if (owner.transform.Find(StationsName) is { } old && old) Object.DestroyImmediate(old.gameObject);
        var stations = Child(owner.transform, StationsName, Vector3.zero);
        var outer = Aperture(owner, false);
        var inner = Aperture(owner, true);
        UnityEngine.Physics.SyncTransforms();
        CallStation(owner, stations, outer, -1, "OUTSIDE.CALL", false, steel, amber);
        CallStation(owner, stations, outer, 1, "CHAMBER.OUT", false, steel, amber);
        CallStation(owner, stations, inner, -1, "CHAMBER.IN", true, steel, amber);
        CallStation(owner, stations, inner, 1, "INSIDE.CALL", true, steel, amber);
    }

    private static void CallStation(AirlockController owner, Transform parent, BoxCollider aperture, int approach,
        string suffix, bool inside, Material steel, Material amber)
    {
        var frame = aperture.transform;
        var basePoint = frame.TransformPoint(aperture.center) - Up * aperture.size.y * .5f;
        var face = frame.forward * approach;
        foreach (float distance in new[] { .9f, 1.2f, .7f })
        foreach (float side in new[] { 1f, -1f })
        {
            float offset = Mathf.Min(aperture.size.x * .5f - .27f, 1.28f);
            var position = basePoint + face * distance + frame.right * (offset * side);
            if (!Floor(position, out var floor)) continue;
            var center = floor + Up * ConsoleHeight;
            var facing = Quaternion.LookRotation(face);
            if (UnityEngine.Physics.CheckBox(center, new Vector3(.24f, .24f, .075f), facing, SolidLayers, QueryTriggerInteraction.Ignore)) continue;
            if (!Approach(center, face, frame.right, side, out _)) continue;
            var station = Child(parent, suffix, parent.InverseTransformPoint(center));
            station.rotation = facing;
            var hit = station.gameObject.AddComponent<BoxCollider>();
            hit.size = new Vector3(.49f, .48f, .15f);
            Part(station, "Console", Vector3.zero, hit.size, steel);
            Part(station, "Stand", new Vector3(0, -.75f, 0), new Vector3(.12f, 1.09f, .12f), steel);
            var indicator = Part(station, "Call pad", new Vector3(0, -.115f, .087f), new Vector3(.19f, .13f, .06f), amber);
            var sign = Child(station, "Label", new Vector3(0, .09f, .08f));
            sign.localRotation = Quaternion.Euler(0, 180, 0);
            var text = sign.gameObject.AddComponent<TextMeshPro>();
            text.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(LabelFontPath);
            if (!text.font) throw new InvalidOperationException("Airlock label font is missing.");
            text.rectTransform.sizeDelta = new Vector2(.42f, .19f);
            text.alignment = TextAlignmentOptions.Center;
            text.enableAutoSizing = true;
            text.fontSizeMin = .6f;
            text.fontSizeMax = 1.1f;
            text.text = suffix.EndsWith(".CALL", StringComparison.Ordinal) ? "AIRLOCK\nCALL" : inside ? "AIRLOCK\nINSIDE" : "AIRLOCK\nOUTSIDE";
            station.gameObject.AddComponent<AirlockButton>().Configure(owner.AirlockId + "." + suffix,
                inside ? "OPEN INNER" : "OPEN OUTER", owner, inside ? AirlockCommand.CycleInner : AirlockCommand.CycleOuter, null, indicator);
            UnityEngine.Physics.SyncTransforms();
            return;
        }
        throw new InvalidOperationException(owner.AirlockId + " has no visible station location for " + suffix);
    }

    private static bool Approach(Vector3 center, Vector3 face, Vector3 lateral, float side, out Vector3 feet)
    {
        var point = center - Up * ConsoleHeight + face * 1.0f - lateral * (.38f * side);
        if (!Floor(point, out feet)) return false;
        if (UnityEngine.Physics.CheckCapsule(feet + Up * .34f, feet + Up * 1.42f, .26f, SolidLayers, QueryTriggerInteraction.Ignore)) return false;
        var origin = feet + Up * 1.52f;
        var delta = center - origin;
        return !UnityEngine.Physics.Raycast(origin, delta.normalized, delta.magnitude - .12f, SolidLayers, QueryTriggerInteraction.Ignore);
    }

    private static bool Floor(Vector3 point, out Vector3 floor)
    {
        if (UnityEngine.Physics.Raycast(point + Up * .6f, -Up, out var hit, 1.25f, LayerMask.GetMask("Ground"), QueryTriggerInteraction.Ignore) && hit.normal.y > .65f)
        {
            floor = hit.point;
            return true;
        }
        floor = default;
        return false;
    }

    private static BoxCollider Aperture(AirlockController owner, bool inside) =>
        new SerializedObject(owner).FindProperty(inside ? "m_InnerAperture" : "m_OuterAperture").objectReferenceValue as BoxCollider
        ?? throw new InvalidOperationException("Missing aperture for " + owner.AirlockId);

    private static string Verify(AirlockController[] owners)
    {
        int controls = 0;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var owner in owners)
        {
            owner.SendMessage("OnEnable", SendMessageOptions.RequireReceiver);
            var initial = owner.State;
            var buttons = owner.GetComponentsInChildren<AirlockButton>();
            var camera = new GameObject("Airlock control sightline check").AddComponent<Camera>();
            try
            {
                if (!owner.Ready || buttons.Length != 4) throw new InvalidOperationException("Incomplete airlock " + owner.AirlockId);
                foreach (var button in buttons) button.SendMessage("OnEnable", SendMessageOptions.RequireReceiver);
                var selection = new RaidInteractionSelection();
                foreach (var button in buttons)
                {
                    if (button.Owner != owner || !ids.Add(button.ButtonId)) throw new InvalidOperationException("Invalid control " + button.ButtonId);
                    var station = button.transform;
                    if (!(Approach(station.position, station.forward, station.right, 1, out var feet) ||
                          Approach(station.position, station.forward, station.right, -1, out feet)) ||
                        !button.InRange(feet) || !new RaidInteractionTarget(button).VisibleFrom(feet + Up * 1.52f))
                        throw new InvalidOperationException("Control has no clear player approach: " + button.ButtonId);
                    var eye = feet + Up * 1.52f;
                    camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(button.InteractionCenter - eye));
                    selection.Refresh(camera, feet, true);
                    if (selection.Selected.Airlock != button) throw new InvalidOperationException("Player aim does not select " + button.ButtonId);
                    controls++;
                }
                CheckPassage(owner, true, false);
                CheckPassage(owner, false, false);
                Cycle(owner, buttons.Single(b => b.ButtonId.EndsWith(".INSIDE.CALL", StringComparison.Ordinal)), true);
                Cycle(owner, buttons.Single(b => b.ButtonId.EndsWith(".CHAMBER.OUT", StringComparison.Ordinal)), false);
                Cycle(owner, buttons.Single(b => b.ButtonId.EndsWith(".CHAMBER.IN", StringComparison.Ordinal)), true);
                Cycle(owner, buttons.Single(b => b.ButtonId.EndsWith(".OUTSIDE.CALL", StringComparison.Ordinal)), false);
            }
            finally
            {
                Object.DestroyImmediate(camera.gameObject);
                owner.ApplyNetworkState(initial, true);
                foreach (var button in buttons) button.SendMessage("OnDisable", SendMessageOptions.RequireReceiver);
                owner.SendMessage("OnDisable", SendMessageOptions.RequireReceiver);
                UnityEngine.Physics.SyncTransforms();
            }
        }
        return $"{owners.Length} MC00 airlocks and {controls} reachable controls. Pressure cycles and all door passages passed.";
    }

    private static void Cycle(AirlockController owner, AirlockButton button, bool inside)
    {
        if (owner.RequestAuthoritative(button.Command) != AirlockFeedback.Accepted)
            throw new InvalidOperationException("Control did not start cycle: " + button.ButtonId);
        for (int i = 0; i < 120; i++)
        {
            owner.TickAuthoritative(.1f);
            UnityEngine.Physics.SyncTransforms();
            var state = owner.State;
            if (state.InnerFraction > .001f && state.OuterFraction > .001f) throw new InvalidOperationException("Airlock interlock failed.");
            if ((inside ? state.InnerFraction : state.OuterFraction) >= .999f) break;
        }
        var open = owner.State;
        if ((inside ? open.InnerFraction : open.OuterFraction) < .999f || open.Equalizing ||
            Mathf.Abs(open.PressureKPa - (inside ? owner.Settings.InsidePressureKPa : owner.Settings.OutsidePressureKPa)) > owner.Settings.OpeningPressureToleranceKPa)
            throw new InvalidOperationException("Cycle did not open the requested door: " + button.ButtonId);
        CheckPassage(owner, inside, true);
        CheckPassage(owner, !inside, false);
    }

    private static void CheckPassage(AirlockController owner, bool inside, bool open)
    {
        var aperture = Aperture(owner, inside);
        var frame = aperture.transform;
        var floor = frame.TransformPoint(aperture.center) - Up * aperture.size.y * .5f;
        var start = floor - frame.forward * .63f;
        bool blocked = UnityEngine.Physics.CapsuleCast(start + Up * .39f, start + Up * 1.46f, .26f, frame.forward,
            out var hit, 1.26f, SolidLayers, QueryTriggerInteraction.Ignore);
        if (blocked == open || !open && hit.collider.GetComponentInParent<AirlockController>() != owner)
            throw new InvalidOperationException($"{owner.AirlockId} {(inside ? "inner" : "outer")} {(open ? "open passage" : "closed seal")}: " +
                (blocked ? hit.collider.name : "no collision"));
    }

    private static Transform Child(Transform parent, string name, Vector3 position)
    {
        var child = new GameObject(name).transform;
        child.SetParent(parent, false);
        child.localPosition = position;
        return child;
    }

    private static BoxCollider Volume(Transform parent, string name, Vector3 position, Vector3 size)
    {
        var collider = Child(parent, name, position).gameObject.AddComponent<BoxCollider>();
        collider.isTrigger = true;
        collider.size = size;
        return collider;
    }

    private static Renderer Part(Transform parent, string name, Vector3 position, Vector3 size, Material material, bool solid = false)
    {
        var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
        part.name = name;
        part.transform.SetParent(parent, false);
        part.transform.localPosition = position;
        part.transform.localScale = size;
        if (!solid) Object.DestroyImmediate(part.GetComponent<BoxCollider>());
        var renderer = part.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        return renderer;
    }

    private static Material MaterialAsset(string name, Color color, float metallic, Color emission)
    {
        GL125WorldAssets.EnsureFolder(AssetsPath);
        string path = AssetsPath + "/" + name + ".mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing) return existing;
        var material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
        material.SetColor("_BaseColor", color);
        material.SetFloat("_Metallic", metallic);
        material.SetFloat("_Smoothness", .36f);
        if (emission.maxColorComponent > 0)
        {
            material.SetColor("_EmissionColor", emission);
            material.EnableKeyword("_EMISSION");
        }
        AssetDatabase.CreateAsset(material, path);
        return material;
    }
}
