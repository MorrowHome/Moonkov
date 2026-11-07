using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Unity.MP_FPS;
using Unity.MP_FPS.Moon;
using Unity.MP_FPS.Moon.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class GL125LiftInstall
{
    private const string ScenePath = SourceWorldSceneInstall.ScenePath;
    private const string AssetRoot = "Assets/MoonEnvironment/Variants/GL125";
    private const string BundlePath = AssetRoot + "/Meshes/GL125_Lifts.asset";
    private const string RootName = "GL125 Elevators";
    private static readonly Vector3 Up = Vector3.up;
    private static readonly Quaternion Heading = Quaternion.Euler(0, -15, 0);

    [Serializable] private sealed class Part
    {
        public string name, file;
        public float[] origin;
        public int[] materials;
    }
    [Serializable] private sealed class Slot
    {
        public int slot, count;
        public int[] remove;
    }
    [Serializable] private sealed class Edit
    {
        public string name;
        public int vertices;
        public bool collision;
        public Slot[] slots;
    }
    [Serializable] private sealed class Manifest
    {
        public int version;
        public string worldHash, collisionHash;
        public Part[] parts;
        public Edit[] edits;
    }

    public static string Install(string stage)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || BuildPipeline.isBuildingPlayer ||
            SceneManager.GetSceneByPath(ScenePath).isLoaded)
            throw new InvalidOperationException("Close the formal map and keep the Editor in Edit Mode.");
        var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(Path.Combine(stage, "Lifts.json")));
        if (manifest.version != 1 || manifest.worldHash?.Length != 64 || manifest.collisionHash?.Length != 64 ||
            manifest.parts.Length != 47 || manifest.edits.Length == 0)
            throw new InvalidDataException("Incomplete GL125 lift staging manifest.");
        string revision = manifest.worldHash + ":" + manifest.collisionHash;
        if (File.ReadAllText(AssetRoot + "/ImportSource.txt").Trim() != manifest.worldHash)
            throw new InvalidDataException("The lift stage belongs to a different GL125 export.");

        var previous = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        bool createdBundle = false, published = false;
        try
        {
            SceneManager.SetActiveScene(scene);
            var roots = scene.GetRootGameObjects();
            var world = roots.Single(root => root.name == "GL125 World");
            var bundle = AssetDatabase.LoadAssetAtPath<SourceWorldMeshBundle>(BundlePath);
            if (world.transform.Find(RootName))
            {
                if (!bundle || bundle.geometryRevision != revision) throw new InvalidDataException("The installed lift bundle has a different revision.");
                return Verify(world.GetComponentsInChildren<RaidLift>());
            }
            string backup = Path.Combine(stage, "TheophilusGameScene.before-GL125Lifts.unity");
            if (!File.Exists(backup)) File.Copy(ScenePath, backup);

            var meshes = new List<Mesh>();
            var colliders = world.GetComponentsInChildren<MeshCollider>(true);
            var renderers = world.GetComponentsInChildren<MeshFilter>(true);
            foreach (var edit in manifest.edits)
            {
                if (edit.collision)
                {
                    var collider = colliders.Single(c => c.name == edit.name);
                    collider.sharedMesh = Split(collider.sharedMesh, edit);
                    meshes.Add(collider.sharedMesh);
                }
                else
                {
                    var renderer = renderers.Single(r => r.name == edit.name);
                    renderer.sharedMesh = Split(renderer.sharedMesh, edit);
                    meshes.Add(renderer.sharedMesh);
                }
            }
            var parts = manifest.parts.ToDictionary(part => part.name, part => (Source: part, Mesh: ReadPart(stage, part)));
            meshes.AddRange(parts.Values.Select(part => part.Mesh));
            Transform root = Child(world.transform, RootName, Vector3.zero);
            int layer = LayerMask.NameToLayer("Ground");
            if (layer < 0) throw new InvalidOperationException("Missing Ground layer.");

            Transform Draw(Transform parent, string key)
            {
                var part = parts[key];
                var origin = part.Source.origin;
                var model = Child(parent, key, Vector3.zero);
                model.SetPositionAndRotation(new Vector3(origin[0], origin[2], origin[1]), Quaternion.identity);
                model.gameObject.AddComponent<MeshFilter>().sharedMesh = part.Mesh;
                var renderer = model.gameObject.AddComponent<MeshRenderer>();
                renderer.sharedMaterials = part.Source.materials.Select(Material).ToArray();
                renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
                renderer.probeAnchor = parent;
                return model;
            }
            AirlockPanelPose Leaf(Transform parent, string key, Vector3 offset, Vector3 sourceOffset)
            {
                var leaf = Child(parent, key, Vector3.zero);
                var model = Draw(leaf, key);
                var mesh = parts[key].Mesh;
                // Use oriented source vertices to fit the collision to each thin leaf.
                var vertices = mesh.vertices.Select(point => leaf.InverseTransformPoint(model.TransformPoint(point))).ToArray();
                var lo = vertices.Aggregate(Vector3.Min);
                var hi = vertices.Aggregate(Vector3.Max);
                var closed = -sourceOffset;
                Solid(leaf, "Door collision", (lo + hi) * .5f, hi - lo, layer);
                return new AirlockPanelPose { PanelId = key, Motion = leaf, ClosedPosition = closed, OpenPosition = closed + offset };
            }

            RaidLift Build(string id, Vector3 dock, float rise, float travelSeconds, float gateSeconds)
            {
                bool north = id == "UL39";
                var shaft = Child(root, id, dock);
                shaft.rotation = Heading;
                var cabin = Child(shaft, "Cabin", Vector3.zero);
                var owner = shaft.gameObject.AddComponent<RaidLift>();
                Draw(cabin, id + "_Cabin");
                var floor = Solid(cabin, "Cabin deck",
                    north ? new Vector3(0, -.09f, 2) : new Vector3(0, -.025f, 1.975f),
                    north ? new Vector3(4.3f, .18f, 4) : new Vector3(3.6f, .05f, 4.01f), layer);
                Solid(cabin, "Cabin roof", north ? new Vector3(0, 3.4f, 2) : new Vector3(0, 2.58f, 2.15f),
                    north ? new Vector3(4.3f, .12f, 4) : new Vector3(3.6f, .08f, 3.66f), layer);
                foreach (float side in new[] { -1f, 1f })
                    Solid(cabin, "Cabin side", north ? new Vector3(side * 2.08f, 1.62f, 2.12f) : new Vector3(side * 1.74f, 1.31f, 1.915f),
                        north ? new Vector3(.14f, 3.24f, 3.72f) : new Vector3(.12f, 2.62f, 3.89f), layer);
                Solid(cabin, "Cabin rear", north ? new Vector3(0, 1.62f, 3.94f) : new Vector3(0, 1.31f, 3.92f),
                    north ? new Vector3(4.3f, 3.24f, .12f) : new Vector3(3.6f, 2.62f, .12f), layer);
                // The four-leaf UL39 gate slides sideways into the original
                // jambs. N62's five staggered blades telescope above head height.
                AirlockPanelPose[] Doors(Transform parent, string section, bool initiallyOpen)
                {
                    var leaves = new List<AirlockPanelPose>();
                    string[] order = north ? new[] { "L1", "L2", "R1", "R2" } : new[] { "0", "1", "2", "3", "4" };
                    for (int i = 0; i < order.Length; i++)
                    {
                        string key = id + "_" + section + "_" + order[i];
                        var delta = north ? Vector3.right * (i < 2 ? -1 : 1) * (i % 2 == 0 ? 1.3f : .66f) : Up * (2.17f - .43f * i);
                        leaves.Add(Leaf(parent, key, delta, initiallyOpen ? delta : Vector3.zero));
                    }
                    return leaves.ToArray();
                }
                var cabinDoors = Doors(cabin, north ? "CAR_DOOR" : "CAB_GATE", north);
                var lowerDoors = Doors(shaft, "LOWER_GATE", north);
                var upperDoors = Doors(shaft, "UPPER_GATE", false);
                var cableObjects = new List<Transform>();
                if (!north)
                {
                    for (int i = 0; i < 2; i++)
                    {
                        string key = "N62_HOIST_CABLE_" + i;
                        cableObjects.Add(Draw(shaft, key));
                    }
                }
                var sill = north ? -.17f : -.15f;
                var width = north ? 2.5f : 3.2f;
                var height = north ? 2.7f : 2.16f;
                BoxCollider Aperture(float level) => Solid(shaft, "Door safety volume",
                    new Vector3(0, level + height * .5f, sill), new Vector3(width, height, .84f), layer, true);
                owner.Configure(id, cabin, floor, rise, travelSeconds, gateSeconds, cabinDoors, lowerDoors, upperDoors,
                    Aperture(0), Aperture(rise), cableObjects.ToArray());
                AddControl(owner, cabin, -1, north ? new Vector3(1.69f, 1.335f, .515f) : new Vector3(1.60f, 1.31f, .82f),
                    north ? Vector3.forward : Vector3.left, !north);
                for (int stop = 0; stop < 2; stop++)
                    AddControl(owner, shaft, stop,
                        north ? new Vector3(1.64f, 1.30f + stop * rise, -.728f)
                            : new Vector3(stop == 0 ? 1.85f : -1.85f, 1.24f + stop * rise, -.68f),
                        Vector3.back, !north);
                return owner;
            }

            RaidLift BuildTransfer(string id, float elevation)
            {
                const float rise = 1.3f;
                var shaft = Child(root, id, new Vector3(2001.83435f, elevation + .12f, 2163.10034f));
                shaft.rotation = Heading;
                var cabin = Child(shaft, "Cabin", Vector3.zero);
                var owner = shaft.gameObject.AddComponent<RaidLift>();
                Draw(cabin, id + "_Cabin");
                var floor = Solid(cabin, "Platform deck", new Vector3(-.05f, -.0225f, 0), new Vector3(1.2f, .045f, 1.52f), layer);
                foreach (float side in new[] { -.62f, .54f })
                    Solid(cabin, "Platform side guard", new Vector3(side, .54f, 0), new Vector3(.055f, 1.08f, 1.47f), layer);
                // The original open pose clears the 1.1 m fence. Lift it a
                // little farther so the standing player can board.
                AirlockPanelPose[] Gate(Transform parent, string name, bool open) =>
                    new[] { Leaf(parent, id + "_" + name, Up * 1.8f, open ? Up * 1.15f : Vector3.zero) };
                var screws = new[]
                {
                    Child(shaft, "Screw A pivot", new Vector3(.77f, -.12f, -.52f)),
                    Child(shaft, "Screw B pivot", new Vector3(.77f, -.12f, .52f))
                };
                Draw(screws[0], id + "_SCREW_A");
                Draw(screws[1], id + "_SCREW_B");
                var drive = Child(shaft, "Drive pivot", new Vector3(.77f, .07f, 0));
                Draw(drive, id + "_DRIVE_SHAFT");
                var lowerSill = Solid(shaft, "Lower door safety volume", new Vector3(-.05f, .53f, -.77f), new Vector3(1.12f, 1.08f, .46f), layer, true);
                var upperSill = Solid(shaft, "Upper door safety volume", new Vector3(-.05f, rise + .53f, .9f), new Vector3(1.12f, 1.08f, .54f), layer, true);
                owner.Configure(id, cabin, floor, rise, 12, 1, Gate(cabin, "CAR_GATE_IN", true),
                    Gate(shaft, "LOW_GATE", true), Gate(shaft, "HIGH_GATE", false),
                    lowerSill, upperSill, Array.Empty<Transform>(), Gate(cabin, "CAR_GATE_OUT", false), screws, drive);
                AddControl(owner, cabin, -1, new Vector3(-.57f, 1.17f, 0), Vector3.right, true);
                AddControl(owner, shaft, 0, new Vector3(.72f, .92f, -.995f), Vector3.back, false);
                AddControl(owner, shaft, 1, new Vector3(.72f, 2.32f, 1.26f), Vector3.forward, false);
                return owner;
            }

            // Both pivots are the authored lower threshold, with +Z pointing inside.
            var owners = new[]
            {
                Build("UL39", new Vector3(1998.551f, 283.42f, 2166.259f), 51, 10, 1),
                Build("N62", new Vector3(2012.5798f, 233.78f, 2171.4873f), 48.3f, 18, 2.5f),
                BuildTransfer("FT55_LOWER", 282),
                BuildTransfer("FT55_UPPER", 333)
            };
            string checks = Verify(owners);
            if (bundle)
            {
                if (bundle.geometryRevision != revision || bundle.meshes.Length != meshes.Count)
                    throw new InvalidDataException("An incompatible lift bundle already exists.");
                var existing = bundle.meshes.ToDictionary(mesh => mesh.name);
                foreach (var filter in world.GetComponentsInChildren<MeshFilter>(true))
                    if (filter.sharedMesh && meshes.Contains(filter.sharedMesh)) filter.sharedMesh = existing[filter.sharedMesh.name];
                foreach (var collider in colliders)
                    if (collider.sharedMesh && meshes.Contains(collider.sharedMesh)) collider.sharedMesh = existing[collider.sharedMesh.name];
                foreach (var mesh in meshes) Object.DestroyImmediate(mesh);
            }
            else
            {
                bundle = ScriptableObject.CreateInstance<SourceWorldMeshBundle>();
                bundle.geometryRevision = revision;
                AssetDatabase.CreateAsset(bundle, BundlePath);
                createdBundle = true;
                foreach (var mesh in meshes) AssetDatabase.AddObjectToAsset(mesh, bundle);
                bundle.meshes = meshes.ToArray();
                EditorUtility.SetDirty(bundle);
                AssetDatabase.SaveAssetIfDirty(bundle);
            }
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save the formal GL125 scene.");
            published = true;
            File.WriteAllText(Path.Combine(stage, "UnityLifts.txt"), checks);
            return checks;
        }
        finally
        {
            EditorSceneManager.CloseScene(scene, true);
            SceneManager.SetActiveScene(previous);
            if (createdBundle && !published) AssetDatabase.DeleteAsset(BundlePath);
        }
    }

    internal static IReadOnlyDictionary<string, Mesh> CollisionOverrides(Transform world, string collisionManifest)
    {
        const string prefix = "WithoutLifts_";
        if (!world.Find(RootName)) return new Dictionary<string, Mesh>();
        var bundle = AssetDatabase.LoadAssetAtPath<SourceWorldMeshBundle>(BundlePath);
        using var sha = SHA256.Create();
        using var source = File.OpenRead(collisionManifest);
        string collisionHash = BitConverter.ToString(sha.ComputeHash(source)).Replace("-", "").ToLowerInvariant();
        string revision = File.ReadAllText(AssetRoot + "/ImportSource.txt").Trim() + ":" + collisionHash;
        if (!bundle || bundle.geometryRevision != revision)
            throw new InvalidDataException("Regenerate the lift split before replacing a different GL125 collision payload.");
        return bundle.meshes.Where(mesh => mesh.name.StartsWith(prefix + "Structure_", StringComparison.Ordinal))
            .ToDictionary(mesh => mesh.name.Substring(prefix.Length));
    }

    private static Transform Child(Transform parent, string name, Vector3 local)
    {
        var child = new GameObject(name).transform;
        child.SetParent(parent, false);
        child.localPosition = local;
        return child;
    }

    private static BoxCollider Solid(Transform parent, string name, Vector3 center, Vector3 size, int layer, bool trigger = false)
    {
        var part = Child(parent, name, center);
        part.gameObject.layer = layer;
        var box = part.gameObject.AddComponent<BoxCollider>();
        box.size = size;
        box.isTrigger = trigger;
        return box;
    }

    private static void AddControl(RaidLift owner, Transform parent, int stop, Vector3 local, Vector3 facing, bool needsPanel)
    {
        string position = stop < 0 ? "CABIN" : stop == 0 ? "LOWER" : "UPPER";
        var panel = Child(parent, "Elevator control " + position, local);
        panel.localRotation = Quaternion.LookRotation(facing);
        var button = panel.gameObject.AddComponent<RaidLiftButton>();
        panel.GetComponent<BoxCollider>().size = new Vector3(.33f, .42f, .05f);
        button.Configure(owner, owner.LiftId + "." + position, stop);
        if (!needsPanel) return;
        void Face(string name, Vector3 at, Vector3 scale, string material)
        {
            var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            part.name = name;
            part.transform.SetParent(panel, false);
            part.transform.localPosition = at;
            part.transform.localScale = scale;
            Object.DestroyImmediate(part.GetComponent<Collider>());
            part.GetComponent<MeshRenderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(material)
                ?? throw new FileNotFoundException(material);
        }
        Face("Panel housing", Vector3.zero, new Vector3(.33f, .42f, .065f), AssetRoot + "/Airlocks/Pressure door steel.mat");
        Face("Button", new Vector3(0, 0, .047f), new Vector3(.14f, .17f, .04f), AssetRoot + "/Airlocks/Airlock control amber.mat");
    }

    private static Material Material(int id) => AssetDatabase.LoadAssetAtPath<Material>($"{AssetRoot}/Materials/Material_{id:0000}.mat")
        ?? throw new FileNotFoundException("Missing GL125 lift material " + id);

    private static Mesh ReadPart(string stage, Part part)
    {
        using var input = new BinaryReader(File.OpenRead(Path.Combine(stage, part.file)));
        if (input.ReadUInt32() != 0x314d4c47) throw new InvalidDataException(part.file);
        int vertices = input.ReadInt32(), stride = input.ReadInt32(), sections = input.ReadInt32();
        if (stride != 8 || sections != part.materials.Length) throw new InvalidDataException(part.file);
        var pos = new Vector3[vertices];
        var norm = new Vector3[vertices];
        var uv = new Vector2[vertices];
        Vector3 ReadAxes()
        {
            float x = input.ReadSingle(), z = input.ReadSingle(), y = input.ReadSingle();
            return new Vector3(x, y, z);
        }
        for (int i = 0; i < vertices; i++)
        {
            pos[i] = ReadAxes();
            norm[i] = ReadAxes();
            uv[i] = new Vector2(input.ReadSingle(), input.ReadSingle());
        }
        var mesh = new Mesh { name = part.name, indexFormat = vertices > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16,
            vertices = pos, normals = norm, uv = uv, subMeshCount = sections };
        for (int slot = 0; slot < sections; slot++)
        {
            int material = input.ReadInt32(), count = input.ReadInt32();
            if (material != part.materials[slot] || count <= 0 || count % 3 != 0) throw new InvalidDataException(part.file);
            var indices = new int[count];
            for (int i = 0; i < count; i += 3)
            {
                indices[i] = input.ReadInt32();
                indices[i + 2] = input.ReadInt32();
                indices[i + 1] = input.ReadInt32();
            }
            mesh.SetTriangles(indices, slot, false);
        }
        mesh.RecalculateBounds();
        mesh.RecalculateTangents();
        return mesh;
    }

    private static Mesh Split(Mesh source, Edit edit)
    {
        if (!source || source.vertexCount != edit.vertices) throw new InvalidDataException("Wrong static chunk " + edit.name);
        var result = Object.Instantiate(source);
        result.name = "WithoutLifts_" + edit.name;
        foreach (var slot in edit.slots)
        {
            var current = source.GetTriangles(slot.slot);
            if (current.Length != slot.count * 3 || slot.remove.Any(i => i < 0 || i >= slot.count))
                throw new InvalidDataException("Lift removal does not match " + edit.name);
            var removed = slot.remove.ToHashSet();
            var remaining = new List<int>(current.Length - removed.Count * 3);
            for (int i = 0; i < current.Length; i += 3)
            {
                if (removed.Contains(i / 3)) continue;
                remaining.Add(current[i]);
                remaining.Add(current[i + 1]);
                remaining.Add(current[i + 2]);
            }
            result.SetTriangles(remaining, slot.slot, false);
        }
        result.RecalculateBounds();
        return result;
    }

    public static string Verify(RaidLift[] lifts)
    {
        if (lifts.Length != 4) throw new InvalidDataException("Expected UL39, N62 and both FT55 lifts in the formal map.");
        var camera = new GameObject("Lift player aim check").AddComponent<Camera>();
        try
        {
            foreach (var lift in lifts)
            {
                var buttons = lift.GetComponentsInChildren<RaidLiftButton>();
                if (buttons.Length != 3 || buttons.Select(button => button.Stop).Distinct().Count() != 3)
                    throw new InvalidDataException("Expected three controls for " + lift.LiftId);
                try
                {
                    lift.SendMessage("OnEnable");
                    lift.ResetToLower();
                    foreach (var button in buttons) button.SendMessage("OnEnable");
                    var cabin = lift.transform.Find("Cabin");
                    var player = new RaidInteractionSelection();
                    var inside = buttons.Single(button => button.Stop < 0);
                    var bottom = buttons.Single(button => button.Stop == 0);
                    var top = buttons.Single(button => button.Stop == 1);
                    bool transfer = lift.LiftId.StartsWith("FT55_", StringComparison.Ordinal);
                    var floorCenter = transfer ? new Vector3(-.05f, 0, 0) : new Vector3(0, 0, 2);
                    void Aim(RaidLiftButton button)
                    {
                        Physics.SyncTransforms();
                        var panel = button.transform;
                        foreach (float offset in new[] { 0f, .35f, -.35f, .65f, -.65f, .95f, -.95f })
                        foreach (float depth in new[] { .48f, .8f, 1.1f, 1.4f, 1.7f, 2f })
                        {
                            var approach = panel.position + panel.forward * depth + panel.right * offset;
                            if (!Physics.Raycast(approach + Up * .3f, Vector3.down, out var ground, 2.3f, 1 << LayerMask.NameToLayer("Ground"), QueryTriggerInteraction.Ignore)) continue;
                            var feet = ground.point;
                            var eye = feet + Up * 1.52f;
                            camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(button.Center - eye));
                            player.Refresh(camera, feet, true);
                            if (player.Selected.Lift == button) return;
                        }
                        throw new InvalidOperationException("Player aim cannot select " + button.ButtonId);
                    }
                    void Passage(int dock, bool open)
                    {
                        Physics.SyncTransforms();
                        float level = dock * (transfer ? 1.3f : lift.LiftId == "UL39" ? 51f : 48.3f);
                        float front = transfer ? dock == 0 ? -1.34f : 1.62f : -1;
                        var start = lift.transform.TransformPoint(new Vector3(transfer && dock == 0 ? -.24f : floorCenter.x, level, front));
                        var direction = lift.transform.forward * (transfer && dock == 1 ? -1 : 1);
                        bool hit = Physics.CapsuleCast(start + Up * .34f, start + Up * 1.45f, .24f, direction,
                            out var contact, transfer ? 1.35f : 2.1f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                        if (hit == open || hit && !contact.collider.transform.IsChildOf(lift.transform))
                            throw new InvalidOperationException($"{lift.LiftId} level {level} passage expected {(open ? "open" : "closed")}, " +
                                $"got {(hit ? contact.collider.name : "no obstruction")}.");
                    }
                    void Move(int command, byte dock)
                    {
                        if (lift.Request(command) != AirlockFeedback.Accepted) throw new InvalidOperationException("Lift request did not start " + lift.LiftId);
                        float lastY = cabin.position.y;
                        bool rode = false;
                        for (int i = 0; i < 1600 && lift.State.Phase != RaidLiftPhase.Docked; i++)
                        {
                            var rider = cabin.TransformPoint(floorCenter + Up * .025f);
                            lift.TickAuthoritative(1f / 60);
                            if (Mathf.Abs(cabin.position.y - lastY) < .002f) continue;
                            Physics.SyncTransforms();
                            if (Mathf.Abs(rider.y + RaidLift.CarryHeight(rider) - cabin.position.y - .025f) > .009f)
                                throw new InvalidOperationException("The lift failed to carry its passenger " + lift.LiftId);
                            if (i % 20 == 0)
                            {
                                var feet = cabin.TransformPoint(floorCenter);
                                if (!Physics.Raycast(feet + Up * .5f, Vector3.down, out var deck, .54f, ~0, QueryTriggerInteraction.Ignore) ||
                                    !deck.collider.transform.IsChildOf(cabin) ||
                                    Physics.CheckCapsule(feet + Up * .34f, feet + Up * 1.45f, .24f, ~0, QueryTriggerInteraction.Ignore))
                                    throw new InvalidOperationException("Cabin ride has an obstruction or missing support " + lift.LiftId);
                            }
                            lastY = cabin.position.y;
                            rode = true;
                        }
                        if (!rode || lift.State.Dock != dock || lift.State.Gate != 1 || lift.State.Phase != RaidLiftPhase.Docked)
                            throw new InvalidOperationException("Lift did not arrive and open " + lift.LiftId);
                    }
                    Aim(inside);
                    Aim(bottom);
                    Aim(top);
                    Passage(0, true);
                    Passage(1, false);
                    Move(-1, 1);
                    Passage(1, true);
                    Passage(0, false);
                    Aim(inside);
                    Move(0, 0);
                    Passage(0, true);
                }
                finally
                {
                    lift.ResetToLower();
                    foreach (var button in buttons) button.SendMessage("OnDisable");
                    lift.SendMessage("OnDisable");
                    Physics.SyncTransforms();
                }
            }
        }
        finally { Object.DestroyImmediate(camera.gameObject); }
        return "UL39, N62 and both FT55 lifts, 12 selectable controls, both directions, passenger carry and all dock passages passed.";
    }
}
