using System;
using System.Collections.Generic;
using Unity.MP_FPS.DollSinger;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Unity.MP_FPS
{
    /// <summary>One authored arm pose and the actual inventory prop, driven by server action time.</summary>
    [DefaultExecutionOrder(120)]
    public sealed class RaidActionPresentation : MonoBehaviour
    {
        private sealed class Prop
        {
            public GameObject Root;
            public Renderer[] World, Owner;
            public Bounds Bounds;
            public RaidActionProp Model, OwnerModel;
        }
        private readonly Dictionary<string, Prop> props = new Dictionary<string, Prop>();
        private readonly RaidActionFoley foley = new RaidActionFoley();
        private readonly RaycastHit[] obstructionHits = new RaycastHit[32];
        private static int? ignoredObstructionLayers;
        private DollSingerActionAnimation animation;
        private RaidActionContact contact;
        private Animator animator;
        private DollSingerView view;
        private DollSingerHaloAim halo;
        private Vector3 searchTarget;
        private HandAction action;
        private string code;
        private Prop prop;
        private uint sequence;
        private float receivedAt, remaining, duration, blend, searchStartedAt, releaseStartedAt, releasePhase;
        private float previewPhase = -1f;
        private bool owner, firstPerson, active, prepared, subscribed, finishing;
        private Transform middle, index, little;
        public bool OwnsHands => animation != null && (active || blend > .001f);
        public HandAction Action => action;
        public bool WorldDepthFallbackActive { get; private set; }
        public float SampledPhase { get; private set; }
        public float ContactError => prop?.Model && prop.Model.Contact ? Vector3.Distance(prop.Model.Contact.position,
            action == HandAction.Inject ? animation.InjectionSite(code,SampledPhase) : contact.Mouth) : 0;
        private bool HasUseAndStow => action == HandAction.Eat || action == HandAction.Drink || action == HandAction.Inject;
        private float UseEnd => Mathf.Clamp(animation.GetMotion(action, code)?.contactOut ?? .7f, .5f, .8f);
        private float ReleaseProgress => Mathf.Clamp01((Time.time - releaseStartedAt) / .42f);
        public float Phase
        {
            get
            {
                if (!active) return finishing ? Mathf.Lerp(releasePhase, 1f, ReleaseProgress) : releasePhase;
                if (action == HandAction.Search) return Mathf.Repeat((Time.time-searchStartedAt) / animation.Seconds(action,code), 1);
                return Mathf.Clamp01(Elapsed / Mathf.Max(.01f, duration)) * (HasUseAndStow ? UseEnd : 1f);
            }
        }
        public float Elapsed => Mathf.Max(0, duration - remaining + Mathf.Min(Time.time - receivedAt, .15f));

        public void Configure(Animator target, DollSingerView playerView, DollSingerHaloAim playerHalo, string rig)
        {
            if (animation != null) return;
            animator = target; view = playerView; halo = playerHalo;
            if (view) view.ActionInjectionPitch = rig == "HatsuneMiku" ? 75 : rig == "Shian" ? 35 : 25;
            if (halo) halo.PrepareExternalHandPose();
            var data = Resources.Load<TextAsset>("Moonkov/Actions/" + rig);
            if (!data) throw new InvalidOperationException("Missing authored action library: " + rig);
            animation = new DollSingerActionAnimation(animator, data);
            contact = new RaidActionContact(animator);
            middle = animator.GetBoneTransform(HumanBodyBones.RightMiddleProximal);
            index = animator.GetBoneTransform(HumanBodyBones.RightIndexProximal);
            little = animator.GetBoneTransform(HumanBodyBones.RightLittleProximal);
            Subscribe();
        }
        public void Present(in PredictedPlayerGhost value, bool isOwner, bool isFirstPerson, bool suppressed)
        {
            searchTarget = value.HandActionTarget; owner = isOwner; firstPerson = isFirstPerson;
            var next = (HandAction)value.HandAction;
            bool alive = value.CurrentHealth > 0 && (!value.ClashEnabled || value.ClashLife == ClashLife.Alive);
            bool playing = alive && !suppressed && next != HandAction.None && animation != null && animation.Has(next);
            if (!playing)
            {
                if (active)
                {
                    // Keep using until the server ends the action, then play only
                    // its authored put-away segment. This never grants an effect.
                    releasePhase = Phase; releaseStartedAt = Time.time;
                    finishing = HasUseAndStow && Elapsed >= duration - .15f;
                }
                active = false;
                if (!alive || suppressed) ResetPresentation();
                return;
            }
            if (value.HandActionSequence != sequence || next != action)
            {
                sequence = value.HandActionSequence; action = next;
                searchStartedAt=Time.time;
                string item = value.HandActionItem.ToString();
                if (prop != null) prop.Root.SetActive(false);
                if (halo) halo.ExternalHandPoseActive = false;
                code = item; prop = GetProp(item);
                blend = 0; finishing = false;
            }
            if (!active || !Mathf.Approximately(remaining, value.HandActionRemaining)) receivedAt = Time.time;
            remaining = value.HandActionRemaining; duration = value.HandActionDuration;
            active = true;
        }
        private void Update()
        {
            if (animation == null) return;
            if (prepared) animation.RestoreBeforeAnimation();
            bool holding = active || finishing && ReleaseProgress < .65f;
            blend = Mathf.MoveTowards(blend, holding ? 1 : 0, Time.deltaTime / (holding ? .12f : .18f));
            prepared = OwnsHands;
            if (OwnsHands && halo) halo.ExternalHandPoseActive = true;
        }
        private void LateUpdate()
        {
            if (animation == null) return;
            if (!OwnsHands)
            {
                finishing = false;
                WorldDepthFallbackActive = false;
                foley.Stop();
                if (prop != null) prop.Root.SetActive(false);
                if (halo) halo.ExternalHandPoseActive = false;
                if (view) view.SetActionHandsActive(false, 0);
                return;
            }
            float phase = SampledPhase = previewPhase >= 0f ? previewPhase : Phase;
            animation.Sample(action, phase, blend, code);
            if (action == HandAction.Search) contact.Search(searchTarget, phase, blend);
            if (prop != null)
            {
                // Keep the held object attached while interrupted arms lower.
                // Hiding it on the first inactive snapshot made it vanish mid-air.
                prop.Root.SetActive(true);
                var motion=animation.GetMotion(action,code);
                bool authored=motion!=null&&motion.root?.positions?.Length==motion.frames*3;
                if(!authored){prop.Model?.Present(phase, action == HandAction.Inject);prop.OwnerModel?.Present(phase, action == HandAction.Inject);}
                PlaceProp(prop,phase);
                if (prop.Model && prop.Model.Contact && (action == HandAction.Eat || action == HandAction.Drink))
                {
                    contact.ToMouth(prop.Model.Contact.position, animation.ContactWeight(action,code,phase) * blend);
                    PlaceProp(prop,phase);
                }
                if(authored){PresentParts(prop.Model,motion,phase);PresentParts(prop.OwnerModel,motion,phase);}
            }
            var soundPosition = prop?.Model && prop.Model.Grip ? prop.Model.Grip.position : animation.RightHand.position;
            foley.Observe(action, sequence, phase, animation.Contact(action, code), code, soundPosition, owner, active || finishing,animation.GetMotion(action,code)?.openMarker??.08f);
            // Searching has no viewmodel prop. Keep the ordinary first-person
            // body projection so looking down never exposes detached shoulders.
            // Overlay cameras clear depth. When a wall intervenes, use the same
            // bones/prop in the ordinary world pass so real geometry occludes them.
            // This avoids a second environment render or changing the authored pose.
            WorldDepthFallbackActive = owner && firstPerson && action != HandAction.Search && view && view.camera &&
                (Obstructed(animation.RightHand.position, .045f) || Obstructed(animation.LeftHand.position, .045f) ||
                 prop?.Model && prop.Model.Contact && Obstructed(prop.Model.Contact.position, .025f));
            if (view) view.SetActionHandsActive(owner && firstPerson && action != HandAction.Search && !WorldDepthFallbackActive, blend, action == HandAction.Inject);
        }
        private bool Obstructed(Vector3 point, float radius)
        {
            Vector3 origin = view.camera.transform.position, delta = point - origin;
            float distance = delta.magnitude;
            if (distance < .001f) return false;
            int ignoredLayers = ignoredObstructionLayers ??= LayerMask.GetMask("ServerPlayer", "ClientPlayer", "FirstPersonOverlay");
            int count = gameObject.scene.GetPhysicsScene().SphereCast(origin, radius, delta / distance,
                obstructionHits, distance, view.cameraObstacleLayers & ~ignoredLayers, QueryTriggerInteraction.Ignore);
            if (count == obstructionHits.Length) return true;
            for (int i = 0; i < count; i++)
                if (obstructionHits[i].collider && !obstructionHits[i].collider.transform.IsChildOf(animator.transform.root)) return true;
            return false;
        }
        public void SamplePreview(float phase)
        {
            if (animation == null) return;
            animation.RestoreBeforeAnimation(); blend = active ? 1 : 0;
            if(action==HandAction.Search)searchStartedAt=Time.time-phase*animation.Seconds(action,code);
            // The preview API addresses the authored source, independent of the
            // server's use/put-away timing split.
            remaining = duration * (1 - Mathf.Clamp01(phase)); receivedAt = Time.time;
            previewPhase = Mathf.Clamp01(phase);
            try { LateUpdate(); }
            finally { previewPhase = -1f; }
        }
        private Prop GetProp(string item)
        {
            if (string.IsNullOrEmpty(item)) return null;
            if (props.TryGetValue(item, out var cached)) return cached;
            var root = new GameObject("Using " + item + " (Runtime)");
            root.transform.SetParent(transform, false);
            Vector3 scale = transform.lossyScale;
            root.transform.localScale = new Vector3(1 / scale.x, 1 / scale.y, 1 / scale.z);
            var variant = Resources.Load<GameObject>("Moonkov/Actions/Props/" + item);
            var world = variant ? Instantiate(variant, root.transform, false) : PhysicalItemVisuals.Create(item, root.transform, true);
            if (!world) { DestroyObject(root); return null; }
            var worldRenderers = world.GetComponentsInChildren<Renderer>(true);
            var bounds = worldRenderers[0].bounds;
            foreach (var renderer in worldRenderers) bounds.Encapsulate(renderer.bounds);
            if (!world.GetComponent<RaidActionProp>())
            {
                // Unwrapped food keeps its actual inventory mesh. Its bite point is
                // measured once in metre space, like the authored packet variants.
                var model = world.AddComponent<RaidActionProp>();
                Transform Anchor(string name, Vector3 point)
                {
                    var node = new GameObject(name).transform; node.SetParent(world.transform, false);
                    node.position = point; return node;
                }
                model.Grip = Anchor("Grip", bounds.center);
                model.Contact = Anchor("Bite contact", bounds.center + Vector3.up * (bounds.extents.y - .005f));
            }
            // The source prefab already converts its FBX centimetres and Z-up axis.
            // Preserve it and move a metre-scale parent, never rescale skinned bones.
            var ownerCopy = Instantiate(world, root.transform, false);
            int layer = LayerMask.NameToLayer("FirstPersonOverlay");
            foreach (var child in ownerCopy.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = layer;
            var result = new Prop { Root = root, World = worldRenderers, Owner = ownerCopy.GetComponentsInChildren<Renderer>(true), Bounds = bounds,
                Model = world.GetComponent<RaidActionProp>(), OwnerModel = ownerCopy.GetComponent<RaidActionProp>() };
            foreach (var renderer in result.Owner) { renderer.forceRenderingOff = true; renderer.shadowCastingMode = ShadowCastingMode.Off; }
            foreach (var renderer in result.World) renderer.shadowCastingMode = ShadowCastingMode.Off;
            root.SetActive(false); props.Add(item, result); return result;
        }
        private void PlaceProp(Prop value,float phase)
        {
            var motion=animation.GetMotion(action,code);
            if(motion!=null&&motion.root?.positions?.Length==motion.frames*3)
            {
                motion.root.Sample(phase,motion.frames,out var p,out var q);
                value.Root.transform.SetPositionAndRotation(animation.RightHand.position+animation.RightHand.rotation*p,animation.RightHand.rotation*q);
                Vector3 s=transform.lossyScale;value.Root.transform.localScale=new Vector3(1/s.x,1/s.y,1/s.z);
                return;
            }
            Vector3 forward = (middle.position - animation.RightHand.position).normalized;
            Vector3 across = (index.position - little.position).normalized;
            Vector3 normal = Vector3.Cross(across, forward).normalized;
            var rotation = Quaternion.LookRotation(normal, across) * animation.PropRotation(action, code);
            float radius = action == HandAction.Drink && code == "water" ? .033f : .014f;
            Vector3 position = Vector3.Lerp(animation.RightHand.position, middle.position, .65f) + normal * radius;
            value.Root.transform.SetPositionAndRotation(position, rotation);
            Vector3 scale = transform.lossyScale;
            value.Root.transform.localScale = new Vector3(1 / scale.x, 1 / scale.y, 1 / scale.z);
            if (value.Model && value.Model.Grip) value.Root.transform.position += position - value.Model.Grip.position;
        }
        private void PresentParts(RaidActionProp model,DollSingerActionAnimation.Motion motion,float phase)
        {
            if(!model||motion.parts==null)return;
            foreach(var track in motion.parts)
            {
                var node=model.FindPart(track.name);if(!node)continue;
                int sample=Mathf.Clamp(Mathf.FloorToInt(phase*(motion.frames-1)),0,motion.frames-1);
                bool visible=track.visible==null||track.visible.Length==0||track.visible[sample];node.gameObject.SetActive(visible);
                if(!visible)continue;
                track.Sample(phase,motion.frames,out var p,out var q);
                var anchor=track.anchor=="L"?animation.LeftHand:track.anchor=="Body"?animator.transform:animation.RightHand;
                node.SetPositionAndRotation(anchor.position+anchor.rotation*p,anchor.rotation*q);
            }
        }
        private void BeforeCamera(ScriptableRenderContext context, Camera camera)
        {
            if (prop == null || !prop.Root || !prop.Root.activeInHierarchy) return;
            bool useOverlay = owner && firstPerson && view && view.GestureHandsActive;
            bool overlay = useOverlay && camera == view.GestureCamera;
            bool hideWorld = useOverlay && (camera == view.camera || overlay);
            foreach (var renderer in prop.World) renderer.forceRenderingOff = hideWorld;
            foreach (var renderer in prop.Owner) renderer.forceRenderingOff = !overlay;
        }
        private void AfterCamera(ScriptableRenderContext context, Camera camera)
        {
            if (prop == null) return;
            foreach (var renderer in prop.World) renderer.forceRenderingOff = false;
            foreach (var renderer in prop.Owner) renderer.forceRenderingOff = true;
        }
        public void ResetPresentation()
        {
            if (animation != null && prepared) animation.RestoreBeforeAnimation();
            foley.Stop();
            if (prop != null) prop.Root.SetActive(false);
            active = prepared = finishing = false; blend = 0; sequence = 0; action = HandAction.None;
            WorldDepthFallbackActive = false;
            releasePhase = SampledPhase = 0;
            if (halo) halo.ExternalHandPoseActive = false;
            if (view) view.SetActionHandsActive(false, 0);
        }
        private void Subscribe()
        {
            if (subscribed) return; subscribed = true;
            RenderPipelineManager.beginCameraRendering += BeforeCamera;
            RenderPipelineManager.endCameraRendering += AfterCamera;
        }
        private void OnEnable() { if (animation != null) Subscribe(); }
        public void DisposePresentation()
        {
            ResetPresentation();
            if (!subscribed) return; subscribed = false;
            RenderPipelineManager.beginCameraRendering -= BeforeCamera;
            RenderPipelineManager.endCameraRendering -= AfterCamera;
        }
        private void OnDisable() => DisposePresentation();
        private void OnDestroy() { DisposePresentation(); foreach (var value in props.Values) if (value.Root) DestroyObject(value.Root); props.Clear(); }
        private static void DestroyObject(UnityEngine.Object value)
        { if (Application.isPlaying) Destroy(value); else DestroyImmediate(value); }
    }
}
