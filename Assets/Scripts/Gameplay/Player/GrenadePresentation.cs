using System;
using System.Collections.Generic;
using Unity.MP_FPS.DollSinger;
using UnityEngine;

namespace Unity.MP_FPS
{
    [DefaultExecutionOrder(125)]
    public sealed class GrenadePresentation : MonoBehaviour
    {
        [Serializable] public sealed class Motion { public int frames; public float[] rotations; }
        [Serializable] public sealed class Library
        {
            public string rig, source;
            public string[] bones;
            public Motion hold, prime, overhand, underhand;
            public float release = .55f;
        }
        private static readonly Dictionary<TextAsset, Library> Libraries = new();
        private readonly Dictionary<string, GameObject> props = new();
        private Library library;
        private Transform[] bones;
        private Quaternion[] rest;
        private Transform hand, middle, index, little;
        private Transform rightArm, leftArm, body;
        private DollSingerHaloAim halo;
        private GameObject prop;
        private GrenadePhase phase;
        private uint holdId;
        private string item;
        private Unity.Collections.FixedString64Bytes itemKey;
        private float remaining, receivedAt, blend;
        private float pitch;
        private bool active, prepared, low, owner, sounded;
        private SoundSystem.SoundInfo sound, pinSound;
        public bool OwnsHands => library != null && (active || blend > .001f);
        public bool PropVisible => prop && prop.activeSelf;
        public Vector3 PropPosition => prop ? prop.transform.position : hand ? hand.position : transform.position;

        public void Configure(Animator animator, DollSingerHaloAim playerHalo, string rig)
        {
            if (library != null) return;
            var asset = Resources.Load<TextAsset>("Moonkov/Grenades/" + rig);
            if (!asset) throw new InvalidOperationException("Missing grenade motion library: " + rig);
#if UNITY_EDITOR
            if (!Application.isPlaying) Libraries.Remove(asset);
#endif
            if (!Libraries.TryGetValue(asset, out library))
            { library = JsonUtility.FromJson<Library>(asset.text); Libraries.Add(asset, library); }
            var hierarchy = new Dictionary<string, Transform>();
            foreach (var bone in animator.GetComponentsInChildren<Transform>(true)) hierarchy[bone.name] = bone;
            bones = new Transform[library.bones.Length]; rest = new Quaternion[bones.Length];
            for (int i = 0; i < bones.Length; i++)
            { bones[i] = hierarchy[library.bones[i]]; rest[i] = bones[i].localRotation; }
            hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            middle = animator.GetBoneTransform(HumanBodyBones.RightMiddleProximal);
            index = animator.GetBoneTransform(HumanBodyBones.RightIndexProximal);
            little = animator.GetBoneTransform(HumanBodyBones.RightLittleProximal);
            rightArm = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            leftArm = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm); body = animator.transform;
            halo = playerHalo; if (halo) halo.PrepareExternalHandPose();
        }
        public void Present(in PredictedPlayerGhost state, bool isOwner)
        {
            owner = isOwner;
            var next = (GrenadePhase)state.GrenadePhase;
            bool alive = state.CurrentHealth > 0 && (!state.ClashEnabled || state.ClashLife == ClashLife.Alive);
            if (!alive) { ResetPresentation(); return; }
            active = next != GrenadePhase.None;
            if (!active) return;
            if (phase != next || holdId != state.GrenadeHoldId)
            { phase = next; holdId = state.GrenadeHoldId; receivedAt = Time.time; sounded = false; }
            if (!Mathf.Approximately(remaining, state.GrenadePhaseRemaining)) receivedAt = Time.time;
            remaining = state.GrenadePhaseRemaining; low = state.GrenadeUnderhand;
            pitch = Mathf.Clamp(state.ControllerState.PitchDegrees, -75, 75);
            if (!itemKey.Equals(state.GrenadeItem))
            { if (prop) prop.SetActive(false); itemKey = state.GrenadeItem; item = itemKey.ToString(); prop = Prop(item); }
        }
        private float Progress => Mathf.Clamp01(1 - (remaining - Mathf.Min(Time.time - receivedAt, .15f)) /
            Mathf.Max(.001f, GrenadeHandling.Duration(phase)));
        private void Update()
        {
            if (library == null) return;
            if (prepared) Restore();
            bool raised = active && (phase != GrenadePhase.Stowing || Progress < .4f);
            blend = Mathf.MoveTowards(blend, raised ? 1 : 0, Time.deltaTime / (raised ? .15f : .18f));
            prepared = OwnsHands;
            if (OwnsHands && halo) halo.ExternalHandPoseActive = true;
        }
        private void LateUpdate() => Apply(Progress);
        private void Apply(float progress)
        {
            if (!OwnsHands)
            { if (prop) prop.SetActive(false); return; }
            var motion = library.hold; float sample = 0;
            switch (phase)
            {
                case GrenadePhase.Priming: motion = library.prime; sample = progress; break;
                case GrenadePhase.Live: motion = library.prime; sample = 1; break;
                case GrenadePhase.Throwing: motion = low ? library.underhand : library.overhand; sample = progress * library.release; break;
                case GrenadePhase.Recovering: motion = low ? library.underhand : library.overhand; sample = Mathf.Lerp(library.release, 1, progress); break;
            }
            Sample(motion, sample, blend);
            rightArm.rotation = Quaternion.AngleAxis(pitch * blend, body.right) * rightArm.rotation;
            float guide = phase == GrenadePhase.Priming ? progress : phase == GrenadePhase.Live || phase == GrenadePhase.Throwing ? 1 :
                phase == GrenadePhase.Recovering ? 1-progress : 0;
            leftArm.rotation = Quaternion.AngleAxis(pitch * guide * blend, body.right) * leftArm.rotation;
            bool visible = phase != GrenadePhase.Recovering && !(phase == GrenadePhase.Throwing && progress >= 1);
            if (prop)
            {
                prop.SetActive(visible);
                Vector3 forward = (middle.position - hand.position).normalized;
                Vector3 across = (index.position - little.position).normalized;
                Vector3 normal = -Vector3.Cross(across, forward).normalized;
                prop.transform.SetPositionAndRotation(Vector3.Lerp(hand.position, middle.position, .75f) + normal * .043f,
                    Quaternion.LookRotation(-normal, across));
            }
            if (halo) halo.ExternalHandPoseActive = true;
            if (active && !sounded && (phase == GrenadePhase.Drawing || phase == GrenadePhase.Priming && progress >= .8f ||
                phase == GrenadePhase.Throwing || phase == GrenadePhase.Stowing))
            {
                sounded = true;
                var audio = MoonkovAudio.Library;
                var cue = phase == GrenadePhase.Priming ? audio?.GrenadePin : phase == GrenadePhase.Throwing ? audio?.GrenadeThrow :
                    phase == GrenadePhase.Stowing ? audio?.WeaponStow : audio?.GrenadeDraw;
                // A queued throw can begin immediately after the pin clears. Let its
                // short metallic tail finish; both handles are still owned and stopped.
                if (phase == GrenadePhase.Priming)
                {
                    GameManager.Instance?.SoundSystem?.Stop(pinSound, .025f);
                    pinSound = MoonkovAudio.PlayWeaponLayer(cue, hand.position, 1, owner ? 0 : 1, follow: hand);
                }
                else
                {
                    GameManager.Instance?.SoundSystem?.Stop(sound, .025f);
                    sound = MoonkovAudio.PlayWeaponLayer(cue, hand.position, 1, owner ? 0 : 1, follow: hand);
                }
            }
        }
        private void Sample(Motion motion, float phaseTime, float weight)
        {
            float frame = Mathf.Clamp01(phaseTime) * (motion.frames - 1);
            int a = Mathf.FloorToInt(frame), b = Mathf.Min(a + 1, motion.frames - 1);
            for (int i = 0; i < bones.Length; i++)
            {
                int p = (a * bones.Length + i) * 4, q = (b * bones.Length + i) * 4;
                var values = motion.rotations;
                var rotation = Quaternion.Slerp(new Quaternion(values[p], values[p+1], values[p+2], values[p+3]),
                    new Quaternion(values[q], values[q+1], values[q+2], values[q+3]), frame-a);
                bones[i].localRotation = Quaternion.Slerp(bones[i].localRotation, rotation, weight);
            }
        }
        private GameObject Prop(string code)
        {
            if (props.TryGetValue(code, out var existing)) return existing;
            var root = new GameObject("Held " + code); root.transform.SetParent(transform, false);
            var scale = transform.lossyScale;
            root.transform.localScale = new Vector3(1/scale.x, 1/scale.y, 1/scale.z);
            if (!PhysicalItemVisuals.Create(code, root.transform, true))
            { DestroyObject(root); return null; }
            foreach (var collider in root.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            foreach (var body in root.GetComponentsInChildren<Rigidbody>(true)) body.isKinematic = true;
            root.SetActive(false); props.Add(code, root); return root;
        }
        private void Restore() { for (int i = 0; i < bones.Length; i++) bones[i].localRotation = rest[i]; }
        public void SamplePreview(float progress)
        { if (library == null) return; blend = 1; prepared = true; Apply(Mathf.Clamp01(progress)); }
        public void ResetPresentation()
        {
            if (prepared && library != null) Restore();
            if (OwnsHands && halo) halo.ExternalHandPoseActive = false;
            active = prepared = false; blend = 0; phase = GrenadePhase.None; holdId = 0;
            if (prop) prop.SetActive(false);
            GameManager.Instance?.SoundSystem?.Stop(sound, .03f); sound = null;
            GameManager.Instance?.SoundSystem?.Stop(pinSound, .03f); pinSound = null;
        }
        private void OnDisable() => ResetPresentation();
        private void OnDestroy() { ResetPresentation(); foreach (var value in props.Values) if (value) DestroyObject(value); props.Clear(); }
        private static void DestroyObject(UnityEngine.Object value) { if (Application.isPlaying) Destroy(value); else DestroyImmediate(value); }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] private static void ResetCache() => Libraries.Clear();
    }
}
