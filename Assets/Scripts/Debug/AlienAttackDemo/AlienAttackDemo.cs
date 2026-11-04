using System;
using Unity.Entities;
using Unity.MP_FPS.SpiderLab;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.UIElements;
using Cursor = UnityEngine.Cursor;

namespace Unity.MP_FPS
{
    /// <summary>One local authority world, one visual rig and one target. No connection or player save is opened.</summary>
    public sealed partial class AlienAttackDemo : MonoBehaviour
    {
        [SerializeField] private AlienCreatureProfile[] m_Profiles;
        [SerializeField] private Camera m_Camera;
        [SerializeField] private UIDocument m_Document;
        [SerializeField] private AudioMixer m_Mixer;
        [SerializeField] private Material m_TargetMaterial;
        [SerializeField] private GameObject m_Ceiling;
        private World m_World;
        private Entity m_TimeEntity;
        private ProceduralAlienEnemy m_Actor;
        private GhostGameObject m_Target;
        private Transform m_VisualRoot;
        private AlienCreatureBody m_Visual;
        private AlienAttackPresentation m_Effects;
        private SoundSystem m_Sound;
        private SoundGameObjectPool m_SoundPool;
        private AudioMixer m_MixerInstance;
        private Renderer[] m_TargetRenderers;
        private MaterialPropertyBlock m_TargetBlock;
        private int m_Selected;
        private uint m_Tick;
        private float m_Elapsed, m_Accumulator, m_FinishAge, m_LastHealth = 1000, m_HitFlash;
        private bool m_Started, m_Finished, m_Dodged, m_Ready;
        private CursorLockMode m_PreviousLock;
        private bool m_PreviousCursor;
        public bool Paused { get; private set; }
        public bool CycleAll { get; set; } = true;
        public bool Dodge { get; private set; }
        public bool Liquid { get; private set; }
        public bool Enraged { get; private set; }
        public int Selected => m_Selected;
        public bool Finished => m_Finished;
        public bool Started => m_Started;
        public float Elapsed => m_Elapsed;
        public float Damage => 1000 - m_LastHealth;
        public uint Hits => m_Actor ? m_Actor.DamageCount : 0;
        public Camera Observer => m_Camera;
        public ProceduralAlienEnemy Actor => m_Actor;
        public SoundSystem Audio => m_Sound;
        public ProceduralAlienEnemy.State Snapshot { get; private set; }
        private AttackEntry Entry => m_Attacks[m_Selected];
        private AlienCreatureProfile Profile => m_Profiles[Entry.ProfileIndex];
        private byte Phase => Enraged && Profile.Boss ? (byte)2 : (byte)1;

        public void Configure(AlienCreatureProfile[] profiles, Camera camera, UIDocument document,
            AudioMixer mixer, Material targetMaterial, GameObject ceiling)
        { m_Profiles = profiles; m_Camera = camera; m_Document = document; m_Mixer = mixer; m_TargetMaterial = targetMaterial; m_Ceiling = ceiling; }

        private void Start()
        {
            if (GameManager.Instance) throw new InvalidOperationException("Open AlienAttackDemo by itself, outside a raid.");
            m_PreviousLock = Cursor.lockState; m_PreviousCursor = Cursor.visible;
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
            BuildCatalog();
            m_TargetBlock = new MaterialPropertyBlock();
            ConfigVar.Init();
            m_MixerInstance = Instantiate(m_Mixer);
            m_SoundPool = new SoundGameObjectPool("Attack demo audio pool", 20);
            m_Sound = new SoundSystem();
            m_Sound.Init(m_Camera.transform, 16, m_SoundPool, m_MixerInstance);
            BindControls(); m_Ready = true; Select(0);
        }

        public void Select(int index)
        {
            if (!m_Ready) return;
            ClearCase();
            m_Selected = (index % m_Attacks.Count + m_Attacks.Count) % m_Attacks.Count;
            m_Elapsed = m_Accumulator = m_FinishAge = m_HitFlash = 0;
            m_LastHealth = 1000; m_Started = m_Finished = m_Dodged = false;
            m_Ceiling.SetActive(Profile.IsCeilingSpider);
            m_World = new World("Alien attack demonstration", WorldFlags.GameServer);
            m_World.GetOrCreateSystemManaged<GhostGameObjectLifetimeSystem>();
            var manager = m_World.EntityManager;
            m_TimeEntity = manager.CreateEntity(typeof(NetworkTime));
            manager.SetComponentData(m_TimeEntity, new NetworkTime { ServerTick = new NetworkTick(++m_Tick) });

            var actorObject = new GameObject("Demonstration authority"); actorObject.transform.SetParent(transform, false);
            Vector3 position = Vector3.up * SurfaceMotor.Height * Profile.Scale;
            Quaternion rotation = Quaternion.identity;
            if (Profile.IsCeilingSpider) { position = new Vector3(0, 3.8f - SurfaceMotor.Height * Profile.Scale, 0); rotation = Quaternion.Euler(0, 0, 180); }
            actorObject.transform.SetPositionAndRotation(position, rotation);
            var ghost = actorObject.AddComponent<GhostGameObject>();
            m_Actor = actorObject.AddComponent<ProceduralAlienEnemy>(); m_Actor.ConfigureDemonstration(Profile);
            Entity entity = MakeEntity(ghost, position, rotation, -33000, Profile.Health);
            manager.AddComponentData(entity, new ProceduralAlienEnemy.State { Scale = Profile.Scale, BossPhase = Phase,
                AmbushEnabled = Profile.IsCeilingSpider, Action = Profile.IsCeilingSpider ? AlienAction.Ambush : AlienAction.Roam });
            if (Enraged && Profile.Boss)
            { var health = manager.GetComponentData<PredictedPlayerGhost>(entity); health.CurrentHealth = health.MaxHealth * .4f; manager.SetComponentData(entity, health); }
            ghost.LinkGhost(m_World, entity, MultiplayerRole.Server);

            var targetObject = new GameObject("Attack target"); targetObject.transform.SetParent(transform, false);
            targetObject.layer = LayerMask.NameToLayer("ServerPlayer");
            targetObject.transform.position = Vector3.forward * Entry.Distance;
            targetObject.transform.rotation = Quaternion.Euler(0, 180, 0);
            var capsule = targetObject.AddComponent<CapsuleCollider>(); capsule.height = 1.8f; capsule.radius = .3f; capsule.center = Vector3.up * .9f;
            m_Target = targetObject.AddComponent<GhostGameObject>(); targetObject.AddComponent<AlienDemoTarget>();
            Entity target = MakeEntity(m_Target, targetObject.transform.position, targetObject.transform.rotation, 42, 1000);
            m_Target.LinkGhost(m_World, target, MultiplayerRole.Server);
            MakeTargetVisual(targetObject.transform);

            m_VisualRoot = new GameObject(Profile.CreatureName).transform; m_VisualRoot.SetParent(transform, false);
            m_VisualRoot.SetPositionAndRotation(position, rotation);
            m_Visual = new AlienCreatureBody(m_VisualRoot, Profile, Profile.Scale, true, LayerMask.NameToLayer("ClientPlayer"), m_Sound);
            m_Effects = new AlienAttackPresentation(m_VisualRoot, Profile);
            Snapshot = manager.GetComponentData<ProceduralAlienEnemy.State>(entity);
            UnityEngine.Physics.SyncTransforms(); Present(1f / 60);
            UpdateSelection(); SetView(m_ViewMode);
        }

        private Entity MakeEntity(GhostGameObject ghost, Vector3 position, Quaternion rotation, int owner, float health)
        {
            var manager = m_World.EntityManager;
            var entity = manager.CreateEntity(typeof(GhostGameObjectGuid), typeof(PredictedPlayerGhost), typeof(GhostOwner), typeof(LocalTransform));
            manager.SetComponentData(entity, new GhostGameObjectGuid { Guid = GhostGameObject.GenerateRandomHash() });
            manager.SetComponentData(entity, new PredictedPlayerGhost { MaxHealth = health, CurrentHealth = health });
            manager.SetComponentData(entity, new GhostOwner { NetworkId = owner });
            manager.SetComponentData(entity, LocalTransform.FromPositionRotation(position, rotation));
            manager.AddComponentObject(entity, new GhostGameObjectLink { LinkedInstance = ghost });
            return entity;
        }

        private void MakeTargetVisual(Transform parent)
        {
            void Part(string name, PrimitiveType type, Vector3 position, Vector3 scale)
            {
                var part = GameObject.CreatePrimitive(type); part.name = name; part.transform.SetParent(parent, false);
                part.transform.localPosition = position; part.transform.localScale = scale;
                part.layer = LayerMask.NameToLayer("Ignore Raycast");
                var collider = part.GetComponent<Collider>(); collider.enabled = false; Destroy(collider);
                part.GetComponent<Renderer>().sharedMaterial = m_TargetMaterial;
            }
            Part("Body", PrimitiveType.Capsule, new Vector3(0, .92f, 0), new Vector3(.5f, .62f, .35f));
            Part("Head", PrimitiveType.Sphere, new Vector3(0, 1.64f, .1f), Vector3.one * .3f);
            for (int side = -1; side <= 1; side += 2)
            {
                Part("Leg", PrimitiveType.Capsule, new Vector3(side * .15f, .34f, 0), new Vector3(.18f, .34f, .18f));
                Part("Arm", PrimitiveType.Capsule, new Vector3(side * .36f, .98f, 0), new Vector3(.16f, .36f, .16f));
            }
            m_TargetRenderers = parent.GetComponentsInChildren<Renderer>();
        }

        private void Update()
        {
            if (!m_Ready) return;
            ReadInput();
            m_Sound.UpdateSoundSystem(Paused);
            if (!Paused)
            {
                m_Accumulator += Mathf.Min(Time.unscaledDeltaTime, .1f);
                while (m_Accumulator >= 1f / 60) { m_Accumulator -= 1f / 60; TickDemonstration(1f / 60); }
            }
            RefreshStatus();
        }

        // Public for bounded scene validation; all hit/movement decisions still run in the real enemy.
        public void TickDemonstration(float dt)
        {
            if (!m_Actor || m_Finished) { if (m_Finished && (m_FinishAge += dt) > 1.6f) Select(CycleAll ? m_Selected + 1 : m_Selected); return; }
            m_Elapsed += dt;
            if (!m_Started && m_Elapsed >= .7f)
            {
                UnityEngine.Physics.SyncTransforms();
                if (!m_Actor.BeginDemonstration(m_Target.LinkedEntity, Entry.Kind, Phase))
                    throw new InvalidOperationException("Attack cannot start: " + Profile.name + " / " + Entry.Kind);
                m_Started = true;
            }
            var manager = m_World.EntityManager;
            if (m_Started)
            {
                if (Dodge && !m_Dodged && Snapshot.Action == AlienAction.Windup && Snapshot.ActionTime >= .25f)
                { m_Target.transform.position += Vector3.right * 7; m_Dodged = true; }
                manager.SetComponentData(m_Target.LinkedEntity, LocalTransform.FromPositionRotation(m_Target.transform.position, m_Target.transform.rotation));
                manager.SetComponentData(m_TimeEntity, new NetworkTime { ServerTick = new NetworkTick(++m_Tick) });
                UnityEngine.Physics.SyncTransforms(); m_Actor.UpdateServer(dt);
                Snapshot = manager.GetComponentData<ProceduralAlienEnemy.State>(m_Actor.GhostGameObject.LinkedEntity);
                var health = manager.GetComponentData<PredictedPlayerGhost>(m_Target.LinkedEntity).CurrentHealth;
                if (health < m_LastHealth) m_HitFlash = .18f;
                m_LastHealth = health;
                var pattern = AlienCombatPattern.For(Profile, Phase, Snapshot.AttackSequence, Snapshot.AttackKind);
                if (Snapshot.Action == AlienAction.Recover && Snapshot.ActionTime >= pattern.Recovery - .02f && !Snapshot.Detached || m_Elapsed > 14)
                    m_Finished = true;
            }
            Present(dt);
            m_HitFlash = Mathf.Max(0, m_HitFlash - dt);
            m_TargetBlock.SetColor("_BaseColor", m_HitFlash > 0 ? new Color(1, .24f, .1f) : new Color(.94f, .75f, .40f));
            foreach (var renderer in m_TargetRenderers) renderer.SetPropertyBlock(m_TargetBlock);
        }

        private void Present(float dt)
        {
            m_VisualRoot.SetPositionAndRotation(m_Actor.transform.position, m_Actor.transform.rotation);
            var pattern = AlienCombatPattern.For(Profile, Phase, Snapshot.AttackSequence, m_Started ? Snapshot.AttackKind : Entry.Kind);
            m_Visual.ObserveAttackAudio(Snapshot, pattern);
            m_Visual.SetCombatPresentation(Snapshot.Action, Snapshot.ActionTime, Snapshot.AttackSequence,
                Profile.Health * (Phase > 1 ? .4f : 1), pattern.Windup, pattern.Recovery, pattern.Active, pattern.Kind);
            float pulse = Snapshot.Action == AlienAction.Windup ? Mathf.Clamp01(Snapshot.ActionTime / pattern.Windup) :
                Snapshot.Action == AlienAction.Strike || Snapshot.Action == AlienAction.Attached ? 1 :
                Snapshot.Action == AlienAction.Recover ? Mathf.Clamp01(1 - Snapshot.ActionTime / pattern.Recovery) : 0;
            m_Visual.Observe(new SurfaceMotor.Pose { Position = m_Actor.transform.position, Rotation = m_Actor.transform.rotation },
                Snapshot.Velocity, Snapshot.Detached, Snapshot.Phase, dt, pulse, Liquid);
            m_Effects.Observe(Snapshot, pattern, m_Visual.AttackSocket);
        }

        public void SetPaused(bool paused) { Paused = paused; UpdatePauseButton(); }
        public void SetDodge(bool dodge) { Dodge = dodge; Select(m_Selected); }
        public void SetLiquid(bool liquid) { Liquid = liquid && SystemInfo.supportsComputeShaders; Select(m_Selected); }
        public void SetEnraged(bool enraged) { Enraged = enraged; Select(m_Selected); }

        private void ClearCase()
        {
            m_Effects?.Dispose(); m_Effects = null; m_Visual?.Dispose(); m_Visual = null;
            m_Sound?.KillAll();
            if (m_Actor) { m_Actor.GhostGameObject.OnGhostPreDestroy(); m_Actor.gameObject.SetActive(false); Destroy(m_Actor.gameObject); }
            if (m_Target) { m_Target.OnGhostPreDestroy(); m_Target.gameObject.SetActive(false); Destroy(m_Target.gameObject); }
            if (m_VisualRoot) { m_VisualRoot.gameObject.SetActive(false); Destroy(m_VisualRoot.gameObject); }
            m_Actor = null; m_Target = null; m_VisualRoot = null;
            if (m_World != null && m_World.IsCreated) m_World.Dispose();
            m_World = null;
        }

        private void OnDestroy()
        {
            ClearCase(); m_SoundPool?.Dispose();
            if (m_ViewTexture) { m_Camera.targetTexture = null; m_ViewTexture.Release(); Destroy(m_ViewTexture); }
            if (m_MixerInstance) Destroy(m_MixerInstance);
            if (m_Ready) { Cursor.lockState = m_PreviousLock; Cursor.visible = m_PreviousCursor; }
        }
    }
}
