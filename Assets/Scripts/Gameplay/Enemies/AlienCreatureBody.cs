using System;
using System.Collections.Generic;
using Unity.MP_FPS.SpiderLab;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Unity.MP_FPS
{
    /// <summary>Gameplay-owned rig adapter. Server uses solved poses and physical
    /// hitboxes without GPU/renderer allocation; clients reconstruct cosmetic poses
    /// from snapshots. Experimental camera/input components are never instantiated.</summary>
    public sealed class AlienCreatureBody : IDisposable
    {
        private readonly AlienCreatureProfile m_Profile;
        private readonly Transform m_Owner;
        private readonly int m_Mask;
        private readonly bool m_Render;
        private readonly float m_Scale;
        private readonly CreatureChainSettings m_ChainShape;
        private readonly BranchCreatureSettings m_BranchShape;
        private readonly List<CapsuleCollider> m_Capsules = new List<CapsuleCollider>();
        private readonly List<SphereCollider> m_Spheres = new List<SphereCollider>();
        private readonly Transform m_HitRoot;
        private readonly CreatureChain m_Chain;
        private readonly BranchCreatureRig m_Branch;
        private readonly PredictiveFeet m_Feet;
        private readonly Vector3[] m_SpiderJoints = new Vector3[24];
        private readonly bool[] m_SpiderLegSolved = new bool[8];
        private CreatureChainPresentation m_ChainView;
        private ChainFluidPresentation m_ChainFluid;
        private BranchFluidPresentation m_BranchFluid;
        private SpiderPresentation m_SpiderView;
        private float m_Phase, m_Clock;
        private bool m_Folded;
        public SurfaceMotor Motor { get; }
        public Vector3 AimPoint => m_Chain != null ? m_Chain.Centres[CreatureChain.Rings / 2] : m_Branch != null ? m_Branch.Points[0] : m_Feet.BodyPose.Position;
        public float Scale => m_Scale;
        public int HitboxCount => m_Capsules.Count + m_Spheres.Count;
        public SkeletonFluidPresentation Fluid => m_ChainFluid?.Fluid ?? m_BranchFluid?.Fluid ?? m_SpiderView?.Fluid;

        public AlienCreatureBody(Transform owner, AlienCreatureProfile profile, float scale, bool render, int hitLayer)
        {
            m_Owner = owner; m_Profile = profile; m_Scale = Mathf.Clamp(scale, .1f, 4); m_Render = render;
            m_Mask = LayerMask.GetMask("Default", "Ground");
            Motor = new SurfaceMotor(owner.position, owner.rotation, m_Mask, m_Scale);
            if (profile.IsChain)
            {
                m_ChainShape = JsonUtility.FromJson<CreatureChainSettings>(JsonUtility.ToJson(profile.Chain));
                ScaleChain(m_ChainShape, m_Scale);
                m_Chain = new CreatureChain(m_Mask); m_Chain.Reset(Motor.Current, m_ChainShape.length);
                if (render) m_ChainView = new CreatureChainPresentation(owner, m_Mask);
            }
            else if (profile.Species == AlienSpecies.Spider)
            {
                var gait = JsonUtility.FromJson<PredictiveFeet.GaitSettings>(JsonUtility.ToJson(profile.Gait));
                gait.compression *= m_Scale; gait.bodyBob *= m_Scale; gait.strideCycleDistance *= m_Scale;
                m_Feet = new PredictiveFeet(m_Mask, m_Scale) { Settings = gait }; m_Feet.Reset(Motor.Current);
                if (render) m_SpiderView = new SpiderPresentation(owner, m_Mask, m_Scale);
            }
            else
            {
                m_BranchShape = JsonUtility.FromJson<BranchCreatureSettings>(JsonUtility.ToJson(profile.Branch));
                m_BranchShape.bodyRadii *= m_Scale; m_BranchShape.reach *= m_Scale;
                m_BranchShape.rootRadius *= m_Scale; m_BranchShape.tipRadius *= m_Scale;
                m_BranchShape.lift *= m_Scale; m_BranchShape.curl *= m_Scale; m_BranchShape.actionExtension *= m_Scale;
                m_Branch = new BranchCreatureRig(owner, m_Mask, m_BranchShape, profile.BranchSpecies, render);
            }
            m_HitRoot = new GameObject("Alien authoritative body hitboxes").transform;
            m_HitRoot.SetParent(owner, false); m_HitRoot.gameObject.layer = hitLayer;
            UpdateRig(Vector2.zero, 0, 0, .016f, false, 0);
            BuildHitboxes(hitLayer); UpdateHitboxes();
        }

        private static void ScaleChain(CreatureChainSettings s, float scale)
        {
            s.length *= scale; s.radius *= scale; s.headScale *= scale; s.feelerLength *= scale;
            s.lateralAmplitude *= scale; s.wavelength *= scale; s.archHeight *= scale;
            s.headLift *= scale; s.sprintHeadLift *= scale; s.legReach *= scale;
            s.legRadius *= scale; s.footLift *= scale; s.bristleLength *= scale;
        }
        public void Tick(Vector3 intent, float speed, float yaw, float dt, float action, bool liquid)
        {
            var pose = Motor.Current;
            var basis = pose.Rotation * Quaternion.AngleAxis(Motor.Transitioning ? 0 : yaw * dt, Vector3.up);
            Vector2 input = new Vector2(Vector3.Dot(intent, basis * Vector3.right), Vector3.Dot(intent, basis * Vector3.forward));
            float gaitSpeed = speed;
            if (m_Profile.Species == AlienSpecies.Leech)
            {
                if (input.sqrMagnitude > .01f) m_Phase = Mathf.Repeat(m_Phase + dt * m_Profile.Chain.waveFrequency, 1);
                if (m_Phase >= .58f || m_Chain.SuckerSpan >= m_ChainShape.length * 1.25f) gaitSpeed = 0;
                else gaitSpeed *= .8f;
            }
            if (m_Feet != null) gaitSpeed *= m_Feet.SpeedScale;
            Motor.Tick(input, gaitSpeed, yaw, dt);
            m_Owner.SetPositionAndRotation(Motor.Current.Position, Motor.Current.Rotation);
            UpdateRig(input, speed, yaw, dt, liquid, action); UpdateHitboxes();
        }
        public void Observe(SurfaceMotor.Pose pose, Vector3 velocity, bool detached, float phase, float dt, float action, bool liquid)
        {
            Motor.Observe(pose, velocity, detached); m_Phase = phase;
            Vector2 input = new Vector2(Vector3.Dot(velocity.normalized, pose.Right), Vector3.Dot(velocity.normalized, pose.Forward));
            UpdateRig(input, velocity.magnitude, 0, dt, liquid, action); UpdateHitboxes();
        }
        public float Phase => m_Phase;
        private void UpdateRig(Vector2 input, float speed, float yaw, float dt, bool liquid, float action)
        {
            m_Clock += dt;
            float drive = Mathf.Clamp01(Motor.Velocity.magnitude / Mathf.Max(.1f, m_Profile.Speed * m_Scale));
            if (m_Chain != null)
            {
                if (m_Profile.Species == AlienSpecies.Leech) m_Chain.TickSuction(Motor.Current, m_ChainShape, m_Phase, speed > .01f ? 1 : 0, dt, Motor.Detached, Motor.BodyHeight);
                else m_Chain.Tick(Motor.Current, m_ChainShape, m_Profile.IsLarva, drive, dt, action, Motor.Detached, Motor.BodyHeight);
                if (m_Render)
                {
                    m_ChainView.Draw(m_Chain, m_ChainShape, m_Profile.IsLarva, m_Clock, dt, Motor.Detached);
                    m_ChainView.SetVisible(!liquid);
                    if (liquid)
                    {
                        m_ChainFluid ??= new ChainFluidPresentation(m_Owner, m_Mask, m_Profile.Chain, m_Profile.IsLarva, m_Scale);
                        m_ChainFluid.Draw(m_Chain, m_ChainView, m_ChainShape, Motor, m_Profile.Fluid, dt);
                    }
                    else m_ChainFluid?.Fluid.SetVisible(false);
                }
            }
            else if (m_Branch != null)
            {
                m_Branch.Tick(Motor, m_BranchShape, m_Clock, drive, action, dt);
                m_Branch.SetVisible(m_Render && !liquid);
                if (m_Render && liquid)
                {
                    m_BranchFluid ??= new BranchFluidPresentation(m_Owner, m_Mask, m_Branch, m_BranchShape, m_Profile.BranchSpecies, m_Scale, m_Profile.Branch);
                    m_BranchFluid.Draw(Motor, m_Profile.Fluid, dt);
                }
                else m_BranchFluid?.Fluid.SetVisible(false);
            }
            else
            {
                if (Motor.Detached && !m_Folded) { m_Feet.BeginFold(); m_Folded = true; }
                if (!Motor.Detached && m_Folded) { m_Feet.EndFold(Motor.Current); m_Folded = false; }
                if (Motor.Detached) m_Feet.TickFolded(Motor.Current, dt);
                else m_Feet.Tick(Motor, input, speed, yaw, dt);
                for (int leg = 0; leg < 8; leg++)
                {
                    Vector3 root = PredictiveFeet.Hip(m_Feet.BodyPose, leg, m_Scale);
                    m_SpiderLegSolved[leg] = SpiderLegKinematics.Solve(m_Feet.BodyPose, leg, m_Feet[leg].Position, m_Feet[leg].Normal,
                        m_Mask, out var knee, out var toe, m_Scale);
                    m_SpiderJoints[leg * 3] = root; m_SpiderJoints[leg * 3 + 1] = knee; m_SpiderJoints[leg * 3 + 2] = toe;
                }
                if (m_Render)
                {
                    m_SpiderView.SetAppearance(liquid ? SpiderPresentation.Look.Fluid : SpiderPresentation.Look.Carapace, false);
                    if (m_SpiderView.Fluid != null)
                    { m_SpiderView.Fluid.Settings = m_Profile.Fluid; m_SpiderView.Fluid.AttractionEnabled = m_Profile.Fluid.attraction; m_SpiderView.Fluid.Predatory = m_Profile.Fluid.heavy; }
                    m_SpiderView.Draw(Motor, m_Feet, SpiderPresentation.Stage.Umbrella, dt, m_SpiderJoints, m_SpiderLegSolved);
                }
            }
        }

        private void BuildHitboxes(int layer)
        {
            Sphere(layer);
            int count = m_Chain != null ? 12 : m_Branch != null ? m_Branch.Branches * 4 : 16;
            for (int i = 0; i < count; i++)
            {
                var go = new GameObject("Alien tissue segment " + i); go.layer = layer; go.transform.SetParent(m_HitRoot, false);
                var capsule = go.AddComponent<CapsuleCollider>(); capsule.direction = 1; m_Capsules.Add(capsule);
            }
        }
        private void Sphere(int layer)
        {
            var go = new GameObject("Alien mantle hitbox"); go.layer = layer; go.transform.SetParent(m_HitRoot, false);
            m_Spheres.Add(go.AddComponent<SphereCollider>());
        }
        private void UpdateHitboxes()
        {
            if (m_Spheres.Count == 0) return;
            var body = m_Spheres[0]; body.transform.position = AimPoint;
            body.radius = (m_Chain != null ? m_ChainShape.radius : m_Branch != null ? m_BranchShape.bodyRadii.x * .7f : .23f * m_Scale);
            for (int i = 0; i < m_Capsules.Count; i++)
            {
                Vector3 a, b; float radius;
                if (m_Chain != null)
                {
                    int first = i * 8, last = Mathf.Min(first + 8, CreatureChain.Rings - 1);
                    a = m_Chain.Centres[first]; b = m_Chain.Centres[last]; radius = Mathf.Max(.015f, m_Chain.Radii[first] * m_ChainShape.verticalScale);
                }
                else if (m_Branch != null)
                {
                    int arm = i / 4, point = i % 4 * 4;
                    int first = BranchCreatureRig.Index(arm, point), last = BranchCreatureRig.Index(arm, point + 4);
                    a = m_Branch.Points[first]; b = m_Branch.Points[last]; radius = m_Branch.Radii[first];
                }
                else
                {
                    int leg = i / 2, segment = i % 2;
                    a = m_SpiderJoints[leg * 3 + segment]; b = m_SpiderJoints[leg * 3 + segment + 1]; radius = .035f * m_Scale;
                }
                var capsule = m_Capsules[i]; Vector3 delta = b - a;
                capsule.transform.SetPositionAndRotation((a + b) * .5f, delta.sqrMagnitude > .000001f ? Quaternion.FromToRotation(Vector3.up, delta) : Quaternion.identity);
                capsule.radius = radius; capsule.height = Mathf.Max(radius * 2, delta.magnitude + radius * 2);
            }
        }
        public void Escape(Vector3 direction)
        {
            Motor.Detach(direction); m_Chain?.ReleaseContacts(); m_ChainView?.ResetContacts(); m_Branch?.ResetContacts();
        }
        public void Dispose()
        {
            m_ChainFluid?.Release(); m_BranchFluid?.Release(); m_SpiderView?.Release(); m_ChainView?.Release(); m_Branch?.Release();
            if (m_HitRoot) { m_HitRoot.gameObject.SetActive(false); if (Application.isPlaying) Object.Destroy(m_HitRoot.gameObject); else Object.DestroyImmediate(m_HitRoot.gameObject); }
        }
    }
}
