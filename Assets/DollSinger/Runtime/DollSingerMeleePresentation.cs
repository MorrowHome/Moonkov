using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Unity.MP_FPS.DollSinger
{
    public struct MeleeViewState
    {
        public MeleeWeaponKind Weapon, AttackWeapon;
        public MeleeAttackKind Attack;
        public MeleePhase Phase;
        public uint AttackSequence, CancelSequence, HitSequence;
        public int ComboIndex, Epoch;
        public float Elapsed;
        public bool Alive, Suppressed, IsOwner, FirstPerson, HitBlocked, Interpolate;
        public Vector3 AttackDirection, HitPoint;
    }

    /// <summary>One real skeleton supplies owner arms and observer views. Never applies gameplay damage.</summary>
    [DefaultExecutionOrder(110)]
    public sealed class DollSingerMeleePresentation : MonoBehaviour
    {
        [Tooltip("Optional exact-rig animation library for this character. Empty retains the original DollSinger library.")]
        public TextAsset gestureBake;
        [Serializable] private sealed class Part
        { public string name; public float[] vertices, normals, color; public int[] triangles; public float emission; }
        [Serializable] private sealed class WeaponArt { public string weapon; public Part[] parts; }
        [Serializable] private sealed class ArtLibrary { public int version; public WeaponArt[] weapons; }
        private sealed class Prop
        {
            public GameObject Root;
            public Renderer[] World, Owner;
            public TrailRenderer WorldTrail, OwnerTrail;
        }
        private struct Visibility { public Camera Camera; public uint World, Owner; }
        private readonly Dictionary<MeleeWeaponKind, Prop> props = new Dictionary<MeleeWeaponKind, Prop>();
        private readonly List<Mesh> ownedMeshes = new List<Mesh>();
        private readonly List<Material> ownedMaterials = new List<Material>();
        private readonly Stack<Visibility> visibility = new Stack<Visibility>(8);
        private DollSingerMeleeBake bake;
        private DollSingerView view;
        private DollSingerHaloAim halo;
        private DollSingerMovement movement;
        private MeleeViewState state;
        private MeleeWeaponKind selected;
        private uint lastAttack, lastCancel, lastHit, cancelledAttack;
        private bool configured, received, wasActive, subscribed;
        private float blend, equipStarted, elapsedReceivedAt, lastElapsed;
        private string previousAction;
        private Quaternion committedFacing = Quaternion.identity;
        private Prop prop;
        private Transform impact;
        private Material impactMaterial;
        private Vector3 impactPoint;
        private float impactStarted = float.NegativeInfinity;
        public bool OwnsHandPose => configured && (WantsPresentation || blend > .001f);
        public bool WantsPresentation => received && state.Alive && !state.Suppressed && state.Weapon != MeleeWeaponKind.None;
        public float PoseBlend => blend;
        public MeleeWeaponKind Weapon => selected;
        public string BakeRevision => bake?.Revision;
        public event Action<Vector3, bool> AuthoritativeImpact;

        public void Configure(Animator animator, DollSingerView playerView, DollSingerHaloAim playerHalo)
        {
            if (configured) return;
            var data = gestureBake ? gestureBake : Resources.Load<TextAsset>("MoonkovMeleeGestures");
            var art = Resources.Load<TextAsset>("MoonkovMeleeWeapons");
            var shader = Resources.Load<Shader>("MoonkovMelee");
            if (!data || !art || !shader) throw new InvalidOperationException("Independent melee bake/art/shader resources have not been imported.");
            // Initializing during an already posed preview must not capture the gun pose as bind state.
            if (playerHalo) playerHalo.PrepareExternalHandPose();
            bake = new DollSingerMeleeBake(animator, data); view = playerView; halo = playerHalo;
            movement = animator.GetComponent<DollSingerMovement>();
            var library = JsonUtility.FromJson<ArtLibrary>(art.text);
            if (library == null || library.version != 1) throw new InvalidOperationException("Unsupported melee art library.");
            foreach (var weapon in library.weapons) BuildProp(weapon, shader);
            configured = true; Subscribe();
        }
        public void SetPresentation(in MeleeViewState value)
        {
            if (received && value.Epoch != state.Epoch) ResetPresentation();
            bool fresh = !received;
            bool changed = fresh || value.Weapon != state.Weapon;
            bool cancel = !fresh && value.CancelSequence != state.CancelSequence;
            bool attack = value.AttackSequence != 0 && (lastAttack == 0 || unchecked((int)(value.AttackSequence - lastAttack)) > 0);
            if (attack && value.AttackDirection.sqrMagnitude > .001f)
                committedFacing = Quaternion.LookRotation(value.AttackDirection, transform.up);
            if (cancel || !value.Alive || value.Suppressed || !fresh && value.Weapon != state.Weapon)
                cancelledAttack = value.AttackSequence;
            else if (attack) cancelledAttack = 0;
            if (changed || cancel || attack) ClearTrails();
            if (changed)
            {
                selected = value.Weapon; equipStarted = Time.time;
                if (prop != null) prop.Root.SetActive(false);
                props.TryGetValue(selected, out prop);
            }
            if (changed || attack || cancel) bake?.BeginTransition(attack ? .035f : .10f);
            if (fresh || value.Elapsed != lastElapsed || value.AttackSequence != state.AttackSequence)
            { lastElapsed = value.Elapsed; elapsedReceivedAt = Time.time; }
            if (fresh) lastHit = value.HitSequence;
            else if (value.HitSequence != 0 && (lastHit == 0 || unchecked((int)(value.HitSequence - lastHit)) > 0))
            {
                lastHit = value.HitSequence;
                if (value.Alive && !value.Suppressed)
                {
                    ShowImpact(value.HitPoint, value.HitBlocked);
                    AuthoritativeImpact?.Invoke(value.HitPoint, value.HitBlocked);
                }
            }
            if (attack) lastAttack = value.AttackSequence;
            lastCancel = value.CancelSequence; state = value; received = true;
            if (!value.Alive || value.Suppressed) blend = 0;
            if (!WantsPresentation) ClearTrails();
            if (halo) halo.ExternalHandPoseActive = OwnsHandPose;
        }
        public void ResetPresentation()
        {
            if (configured) { ClearTrails(); bake.RestoreBeforeAnimation(); }
            foreach (var value in props.Values) value.Root.SetActive(false);
            if (impact) impact.gameObject.SetActive(false);
            visibility.Clear(); received = wasActive = false; blend = 0;
            lastAttack = lastCancel = lastHit = cancelledAttack = 0; previousAction = null;
            selected = MeleeWeaponKind.None; prop = null; state = default;
            committedFacing = Quaternion.identity;
            if (halo) halo.ExternalHandPoseActive = false;
            if (view) view.SetMeleeHandsActive(false, 0, selected);
        }
        private void Update()
        {
            if (!configured) return;
            if (wasActive || OwnsHandPose) bake.RestoreBeforeAnimation();
            blend = Mathf.MoveTowards(blend, WantsPresentation ? 1 : 0, Time.deltaTime / .12f);
            if (halo) halo.ExternalHandPoseActive = OwnsHandPose;
        }
        private void LateUpdate()
        {
            if (!configured) return;
            UpdateImpact();
            wasActive = OwnsHandPose;
            if (!wasActive)
            {
                if (prop != null) prop.Root.SetActive(false);
                if (view) view.SetMeleeHandsActive(false, 0, selected);
                return;
            }
            bool attacking = WantsPresentation && state.AttackWeapon == selected && state.AttackSequence != 0 &&
                state.AttackSequence != cancelledAttack && state.Attack != MeleeAttackKind.None && state.Phase != MeleePhase.Idle;
            float elapsed = Mathf.Max(0, state.Elapsed) + (state.Interpolate ? Mathf.Clamp(Time.time - elapsedReceivedAt, 0, .06f) : 0);
            MeleeTiming timing = default;
            if (MeleeRules.TryDefinition(selected, out var definition)) timing = definition.Timing(state.Attack, state.ComboIndex);
            string action = attacking ? state.Attack == MeleeAttackKind.Heavy ? "heavy" :
                state.ComboIndex == 1 ? "light1" : state.ComboIndex == 2 ? "light2" : "light0" :
                Time.time - equipStarted < .28f && WantsPresentation ? "equip" : "idle";
            if (action == "equip") elapsed = Time.time - equipStarted;
            if (previousAction != action) { bake.BeginTransition(attacking ? .035f : .10f); previousAction = action; }
            Vector3 direction = state.AttackDirection.sqrMagnitude > .001f ? state.AttackDirection : transform.forward;
            if (attacking) direction = Quaternion.RotateTowards(committedFacing,
                Quaternion.LookRotation(direction, transform.up), MeleeRules.MaximumTurnDegrees) * Vector3.forward;
            bake.Apply(selected, action, elapsed, blend, timing, direction,
                movement ? movement.LeanRoll : 0);
            if (prop != null)
            {
                prop.Root.SetActive(WantsPresentation);
                bool trail = attacking && state.Phase == MeleePhase.Active && elapsed >= timing.Windup && elapsed < timing.Windup + timing.Active;
                prop.WorldTrail.emitting = trail; prop.OwnerTrail.emitting = trail;
            }
            if (view) view.SetMeleeHandsActive(state.IsOwner && state.FirstPerson, blend, selected);
        }
        private void UpdateImpact()
        {
            if (impact)
            {
                float age = Time.time - impactStarted;
                impact.gameObject.SetActive(age < .12f && state.Alive && !state.Suppressed);
                impact.position = impactPoint;
                impact.localScale = InverseScale(transform.lossyScale) * Mathf.Max(0, 1 - age / .12f);
            }
        }
        private void ShowImpact(Vector3 point, bool blocked)
        {
            if (!impact)
            {
                impact = new GameObject("Adjudicated melee impact (Runtime)").transform; impact.SetParent(transform, false);
                var mesh = new Mesh { name = "Melee impact spark" };
                mesh.vertices = new[] { new Vector3(-.035f,0,0),new Vector3(0,.008f,0),new Vector3(.035f,0,0),new Vector3(0,-.008f,0) };
                mesh.triangles = new[] { 0,1,2,0,2,3,2,1,0,3,2,0 }; mesh.RecalculateNormals(); mesh.RecalculateBounds(); ownedMeshes.Add(mesh);
                var material = new Material(Resources.Load<Shader>("MoonkovMelee")); material.SetFloat("_Emission",1); ownedMaterials.Add(material); impactMaterial = material;
                for (int i = 0; i < 3; i++)
                {
                    var child = new GameObject("Spark"); child.transform.SetParent(impact,false); child.transform.localRotation = Quaternion.Euler(i*55,i*63,i*48);
                    child.AddComponent<MeshFilter>().sharedMesh = mesh; var renderer = child.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material; renderer.shadowCastingMode = ShadowCastingMode.Off;
                }
            }
            impactPoint = point; impactStarted = Time.time;
            impactMaterial.SetColor("_BaseColor", blocked ? new Color(.72f,.85f,1) : new Color(1,.86f,.48f));
            impact.rotation = Quaternion.LookRotation(state.AttackDirection.sqrMagnitude > .001f ? state.AttackDirection : transform.forward);
        }
        private void BuildProp(WeaponArt art, Shader shader)
        {
            if (!Enum.TryParse<MeleeWeaponKind>(art.weapon, out var kind) || kind == MeleeWeaponKind.None) return;
            var root = new GameObject("Melee " + art.weapon + " (Runtime)"); root.transform.SetParent(bake.RightHand, false);
            // Imported MMD hand transforms carry centimetre scale; weapon geometry is authored in metres.
            Vector3 inverse = InverseScale(bake.RightHand.lossyScale);
            root.transform.localPosition = Vector3.Scale(bake.GripPosition, inverse);
            root.transform.localRotation = bake.GripRotation; root.transform.localScale = inverse;
            var world = new List<Renderer>(); var owner = new List<Renderer>();
            foreach (var part in art.parts)
            {
                var mesh = new Mesh { name = "Melee " + art.weapon + " " + part.name };
                var vertices = new Vector3[part.vertices.Length / 3]; var normals = new Vector3[vertices.Length];
                for (int i = 0; i < vertices.Length; i++)
                { int j = i * 3; vertices[i] = new Vector3(part.vertices[j], part.vertices[j + 1], part.vertices[j + 2]); normals[i] = new Vector3(part.normals[j], part.normals[j + 1], part.normals[j + 2]); }
                mesh.vertices = vertices; mesh.normals = normals; mesh.triangles = part.triangles; mesh.RecalculateBounds(); ownedMeshes.Add(mesh);
                var material = new Material(shader) { name = "Melee " + art.weapon + " " + part.name };
                material.SetColor("_BaseColor", new Color(part.color[0], part.color[1], part.color[2], part.color[3])); material.SetFloat("_Emission", part.emission); ownedMaterials.Add(material);
                for (int copy = 0; copy < 2; copy++)
                {
                    var child = new GameObject(part.name + (copy == 0 ? " World" : " Owner")); child.transform.SetParent(root.transform, false);
                    child.layer = copy == 0 ? gameObject.layer : LayerMask.NameToLayer("FirstPersonOverlay");
                    child.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var renderer = child.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material; renderer.forceRenderingOff = copy != 0;
                    renderer.shadowCastingMode = ShadowCastingMode.Off; (copy == 0 ? world : owner).Add(renderer);
                }
            }
            var result = new Prop { Root = root };
            result.WorldTrail = BuildTrail(root.transform, kind, false); result.OwnerTrail = BuildTrail(root.transform, kind, true);
            world.Add(result.WorldTrail); owner.Add(result.OwnerTrail); result.World = world.ToArray(); result.Owner = owner.ToArray();
            props.Add(kind, result); root.SetActive(false);
        }
        private TrailRenderer BuildTrail(Transform root, MeleeWeaponKind kind, bool owner)
        {
            var child = new GameObject(owner ? "Owner blade trail" : "World blade trail"); child.transform.SetParent(root, false);
            child.layer = owner ? LayerMask.NameToLayer("FirstPersonOverlay") : gameObject.layer;
            child.transform.localPosition = Vector3.up * (kind == MeleeWeaponKind.EnergySword ? .83f : .29f);
            var trail = child.AddComponent<TrailRenderer>(); trail.emitting = false; trail.time = .075f;
            trail.minVertexDistance = .018f; trail.startWidth = kind == MeleeWeaponKind.EnergySword ? .018f : .009f; trail.endWidth = 0;
            trail.numCornerVertices = 1; trail.shadowCastingMode = ShadowCastingMode.Off; trail.forceRenderingOff = owner;
            var material = new Material(Resources.Load<Shader>("MoonkovMeleeTrail"));
            material.SetColor("_BaseColor", kind == MeleeWeaponKind.EnergySword ? new Color(.3f, .9f, 1, .4f) : new Color(.8f, .85f, .9f, .18f));
            trail.sharedMaterial = material; ownedMaterials.Add(material); return trail;
        }
        private void ClearTrails()
        { foreach (var value in props.Values) { value.WorldTrail.emitting = value.OwnerTrail.emitting = false; value.WorldTrail.Clear(); value.OwnerTrail.Clear(); } }
        private static Vector3 InverseScale(Vector3 scale) => new Vector3(
            1 / Mathf.Max(.0001f, Mathf.Abs(scale.x)), 1 / Mathf.Max(.0001f, Mathf.Abs(scale.y)), 1 / Mathf.Max(.0001f, Mathf.Abs(scale.z)));
        private void Subscribe()
        {
            if (subscribed || !isActiveAndEnabled) return;
            RenderPipelineManager.beginCameraRendering += BeforeCamera; RenderPipelineManager.endCameraRendering += AfterCamera; subscribed = true;
        }
        private void BeforeCamera(ScriptableRenderContext context, Camera camera)
        {
            if (prop == null || !view) return;
            uint a = 0, b = 0;
            bool own = state.IsOwner && state.FirstPerson && view.GestureHandsActive;
            for (int i = 0; i < prop.World.Length; i++)
            {
                if (prop.World[i].forceRenderingOff) a |= 1u << i;
                if (prop.Owner[i].forceRenderingOff) b |= 1u << i;
                prop.World[i].forceRenderingOff = own && camera == view.camera;
                prop.Owner[i].forceRenderingOff = !own || camera != view.GestureCamera;
            }
            visibility.Push(new Visibility { Camera = camera, World = a, Owner = b });
        }
        private void AfterCamera(ScriptableRenderContext context, Camera camera)
        {
            if (prop == null || visibility.Count == 0) return;
            var previous = visibility.Pop();
            if (previous.Camera != camera)
            {
                visibility.Clear();
                for (int i = 0; i < prop.World.Length; i++) { prop.World[i].forceRenderingOff = false; prop.Owner[i].forceRenderingOff = true; }
                return;
            }
            for (int i = 0; i < prop.World.Length; i++)
            { prop.World[i].forceRenderingOff = (previous.World & (1u << i)) != 0; prop.Owner[i].forceRenderingOff = (previous.Owner & (1u << i)) != 0; }
        }
        private void OnEnable() { if (configured) Subscribe(); }
        private void OnDisable()
        {
            if (subscribed) { RenderPipelineManager.beginCameraRendering -= BeforeCamera; RenderPipelineManager.endCameraRendering -= AfterCamera; subscribed = false; }
            ResetPresentation();
        }
        private void OnDestroy()
        {
            foreach (var value in props.Values) if (value.Root) Destroy(value.Root);
            foreach (var mesh in ownedMeshes) if (mesh) Destroy(mesh);
            foreach (var material in ownedMaterials) if (material) Destroy(material);
            if (impact) Destroy(impact.gameObject);
        }
    }
}
