// 消费弹着点的画面和音效。预测可以让本机先看到弹痕，服务端回执确定实际落点，命中和击杀反馈只在确认后出现。
using Unity.NetCode;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Unity.MP_FPS {
    public struct ClientRifleImpactRpc : IRpcCommand {
        public int OwnerNetworkId;
        public uint ShotTick;
        public uint WeaponId;
        public int PelletIndex;
        public float3 Position, Normal;
        public byte Kind, DamageFlags; // 第 0 位表示命中身体或护盾，第 1 位表示致命伤害。
    }

    // 挂在客户端 World 已有的特效管理器之下。每个槽管理碎片与短暂的表面反馈，
    // 合计最多十六个粒子，狙击闪光也算在内。
    public sealed class RifleImpactFeedback : MonoBehaviour {
        private struct Receipt {
            public int Owner;
            public uint Tick;
            public uint Weapon;
            public int Pellet;
            public float Until;
            public bool Confirmed;
            public Vector3 Position;
            public byte Kind;
        }

        private struct ShotReceipt {
            public int Owner;
            public uint Tick, Weapon;
            public float Until, LastSurfaceSound;
            public byte Confirmation;
        }

        private sealed class ImpactEffect {
            public Transform Root;
            public ParticleSystem Fragments, Surface;
            public ParticleSystemRenderer FragmentRenderer, SurfaceRenderer;
            public MeshRenderer Contact;
            public float ContactUntil;
        }

        private static readonly int ImpactLayerId = Shader.PropertyToID("_ImpactLayer");
        private readonly Receipt[] m_Receipts = new Receipt[256];
        private readonly ShotReceipt[] m_Shots = new ShotReceipt[128];
        private readonly ImpactEffect[] m_Pool = new ImpactEffect[16];
        private MaterialPropertyBlock m_Properties;
        private Mesh m_ContactMesh;
        private static readonly int ContactStartId = Shader.PropertyToID("_ContactStart");
        private static readonly int ContactDurationId = Shader.PropertyToID("_ContactDuration");
        private static readonly int ContactMetalId = Shader.PropertyToID("_ContactMetal");
        private Gradient m_FragmentFade, m_SparkCooling;
        private RifleFeedbackLibrary m_Library;
        private int m_NextReceipt, m_NextEffect, m_NextShot;

        public void Warmup() {
            if (m_Library != null) return;
            m_Library = Resources.Load<RifleFeedbackLibrary>("Moonkov/RifleFeedback");
            if (m_Library == null || m_Library.ImpactMaterial == null) return;
            m_Properties = new MaterialPropertyBlock();
            m_FragmentFade = new Gradient();
            m_FragmentFade.SetKeys(new[] {
                new GradientColorKey(Color.white, 0),
                new GradientColorKey(Color.white, 1)
            }, new[] {
                new GradientAlphaKey(1, 0),
                new GradientAlphaKey(1, .25f), new GradientAlphaKey(0, 1)
            });
            m_SparkCooling = new Gradient();
            m_SparkCooling.SetKeys(new[] {
                new GradientColorKey(Color.white, 0),
                new GradientColorKey(new Color(1, .75f, .32f), .2f),
                new GradientColorKey(new Color(.6f, .19f, .035f), .7f),
                new GradientColorKey(new Color(.2f, .045f, .01f), 1)
            }, new[] {
                new GradientAlphaKey(1, 0), new GradientAlphaKey(1, .15f),
                new GradientAlphaKey(0, 1)
            });
            m_ContactMesh = new Mesh { name = "Pooled impact contact", hideFlags = HideFlags.DontSave };
            m_ContactMesh.vertices = new[] {
                new Vector3(-.5f, -.5f, 0), new Vector3(-.5f, .5f, 0),
                new Vector3(.5f, .5f, 0), new Vector3(.5f, -.5f, 0)
            };
            m_ContactMesh.uv = new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right };
            m_ContactMesh.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            m_ContactMesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            for (int i = 0; i < m_Pool.Length; i++) {
                var child = new GameObject("Rifle impact " + i);
                child.transform.SetParent(transform, false);
                var effect = new ImpactEffect { Root = child.transform };
                effect.Fragments = CreateLayer("Fragments", effect.Root, 12, false, out effect.FragmentRenderer);
                effect.Surface = CreateLayer("Surface response", effect.Root, 4, true, out effect.SurfaceRenderer);
                var contact = new GameObject("Surface contact");
                contact.transform.SetParent(effect.Root, false);
                contact.transform.localPosition = Vector3.back * .02f;
                contact.AddComponent<MeshFilter>().sharedMesh = m_ContactMesh;
                effect.Contact = contact.AddComponent<MeshRenderer>();
                effect.Contact.sharedMaterial = m_Library.ImpactMaterial;
                effect.Contact.shadowCastingMode = ShadowCastingMode.Off;
                effect.Contact.receiveShadows = false;
                effect.Contact.lightProbeUsage = LightProbeUsage.Off;
                effect.Contact.reflectionProbeUsage = ReflectionProbeUsage.Off;
                effect.Contact.enabled = false;
                m_Pool[i] = effect;
            }
        }

        private ParticleSystem CreateLayer(
            string name,
            Transform parent,
            int budget,
            bool surface,
            out ParticleSystemRenderer renderer) {
            var child = new GameObject(name);
            child.transform.SetParent(parent, false);
            var particles = child.AddComponent<ParticleSystem>();
            particles.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = particles.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = .8f;
            main.maxParticles = budget;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 1.62f / Mathf.Max(.001f, Mathf.Abs(UnityEngine.Physics.gravity.y));
            main.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
            var emission = particles.emission;
            emission.enabled = false;
            var shape = particles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = surface ? 65f : 48f;
            shape.radius = surface ? .035f : .012f;
            var size = particles.sizeOverLifetime;
            size.enabled = true;
            // 较大的碎片保留清楚轮廓，小碎屑散开时不加烟雾那样的拖拽。
            size.size = new ParticleSystem.MinMaxCurve(1,
                AnimationCurve.Linear(0, surface ? .65f : 1, 1, surface ? 1.25f : .55f));
            var color = particles.colorOverLifetime;
            color.enabled = true;
            color.color = m_FragmentFade;
            renderer = particles.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = m_Library.ImpactMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return particles;
        }

        private void SetLayer(ParticleSystemRenderer renderer, float layer) {
            m_Properties.Clear();
            m_Properties.SetFloat(ImpactLayerId, layer);
            renderer.SetPropertyBlock(m_Properties);
        }

        // 预测撞击可立即显示表面反馈。伤害和命中仍用服务端确认。
        public void Present(in ClientRifleImpactRpc impact, bool confirmed, bool owned) {
            Warmup();
            var profile = m_Library?.ForWeapon(impact.WeaponId);
            // 把一次扣扳机和它的各颗弹丸分开追踪。每个落点能有特效，HUD 可把同一发的反馈合成一条。
            int shot = FindShot(impact);
            // 落点按发射者、射击 tick、武器和弹丸序号去重。同一发的确认如果改变了表面或位置，再在正确的位置补一个画面效果。
            int receipt = -1;
            for (int i = 0; i < m_Receipts.Length; i++)
                if (m_Receipts[i].Until > Time.unscaledTime && m_Receipts[i].Owner == impact.OwnerNetworkId &&
                    m_Receipts[i].Tick == impact.ShotTick && m_Receipts[i].Weapon == impact.WeaponId &&
                    m_Receipts[i].Pellet == impact.PelletIndex) {
                    receipt = i;
                    break;
                }
            if (receipt < 0) {
                receipt = m_NextReceipt++ % m_Receipts.Length;
                m_Receipts[receipt] = new Receipt {
                    Owner = impact.OwnerNetworkId, Tick = impact.ShotTick,
                    Weapon = impact.WeaponId, Pellet = impact.PelletIndex,
                    Until = Time.unscaledTime + 6f, Position = impact.Position, Kind = impact.Kind
                };
                Emit((RifleImpactKind)impact.Kind, impact.Position, impact.Normal, profile, shot);
            // 在本机先画的落点如果和服务器确认相差明显，就补画正确的地面或身体。这只影响表现，实际受伤已由服务器定。
            } else if (confirmed && !m_Receipts[receipt].Confirmed && (m_Receipts[receipt].Kind != impact.Kind ||
                                                                       Vector3.Distance(m_Receipts[receipt].Position,
                                                                           impact.Position) > .25f)) {
                // 修正后的轨迹可能撞到另一种表面。用服务器给出的实际落点展示效果。
                Emit((RifleImpactKind)impact.Kind, impact.Position, impact.Normal, profile, shot);
            }
            if (!confirmed || m_Receipts[receipt].Confirmed) return;
            m_Receipts[receipt].Confirmed = true;
            // 击中角色的回执才给射手的本机界面响确认。别人造成的伤害和打到环境的特效仍正常展示。
            if (!owned || (impact.DamageFlags & 1) == 0) return;
            bool killed = (impact.DamageFlags & 2) != 0;
            // 霰弹的多颗弹丸共享一次扳机的确认。普通命中可升级为击杀，后续同级或更低的确认不会再响。
            byte confirmation = (byte)(killed ? 2 : 1);
            // 霰弹枪八个弹丸合起来是一次扣扳机。后到的致命弹丸只可将提示升级一次。
            if (confirmation <= m_Shots[shot].Confirmation) return;
            m_Shots[shot].Confirmation = confirmation;
            InGameHUD.ConfirmRifleHit(killed, profile);
            MoonkovAudio.Play(
                killed ? profile?.KillConfirm ?? m_Library?.KillConfirm : profile?.HitConfirm ?? m_Library?.HitConfirm,
                Vector3.zero);
        }

        private int FindShot(in ClientRifleImpactRpc impact) {
            for (int i = 0; i < m_Shots.Length; i++)
                if (m_Shots[i].Until > Time.unscaledTime && m_Shots[i].Owner == impact.OwnerNetworkId &&
                    m_Shots[i].Tick == impact.ShotTick && m_Shots[i].Weapon == impact.WeaponId)
                    return i;
            // 存放确认的环形表大小固定。过期或被覆盖的老枪击退出记录，避免打久后不断累积。
            int next = m_NextShot++ % m_Shots.Length;
            m_Shots[next] = new ShotReceipt {
                Owner = impact.OwnerNetworkId, Tick = impact.ShotTick,
                Weapon = impact.WeaponId, Until = Time.unscaledTime + 6f, LastSurfaceSound = float.NegativeInfinity
            };
            return next;
        }

        private void Emit(
            RifleImpactKind kind,
            Vector3 position,
            Vector3 normal,
            HaloWeaponFeedbackProfile profile,
            int shot) {
            if (m_Library == null) return;
            // 各弹丸仍分别有表面粒子，挨得近且同时发生的声音共用短时限频。
            if (Time.unscaledTime - m_Shots[shot].LastSurfaceSound >= (profile?.SurfaceSoundInterval ?? 0)) {
                MoonkovAudio.Play(
                    kind == RifleImpactKind.Body ? profile?.BodyImpact ?? m_Library.BodyImpact :
                    kind == RifleImpactKind.Metal ? profile?.MetalImpact ?? m_Library.MetalImpact :
                    profile?.RockImpact ?? m_Library.RockImpact, position);
                m_Shots[shot].LastSurfaceSound = Time.unscaledTime;
            }
            var effect = m_Pool[m_NextEffect++ % m_Pool.Length];
            if (effect == null) return;
            var particles = effect.Fragments;
            particles.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            effect.Surface.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            normal = normal.sqrMagnitude > .001f ? normal.normalized : Vector3.up;
            effect.Root.SetPositionAndRotation(position + normal * .025f, Quaternion.LookRotation(normal));
            bool metal = kind == RifleImpactKind.Metal, body = kind == RifleImpactKind.Body;
            effect.Contact.enabled = !body;
            effect.ContactUntil = Time.time + (metal ? .85f : 1.1f);
            float contactSize = (metal ? .09f : .12f) * Mathf.Sqrt(profile?.FragmentScale ?? 1);
            effect.Contact.transform.localScale = Vector3.one * contactSize;
            m_Properties.Clear();
            m_Properties.SetFloat(ImpactLayerId, 5);
            m_Properties.SetFloat(ContactStartId, Time.time);
            m_Properties.SetFloat(ContactDurationId, metal ? .85f : 1.1f);
            m_Properties.SetFloat(ContactMetalId, metal ? 1 : 0);
            effect.Contact.SetPropertyBlock(m_Properties);
            var main = particles.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(metal ? .09f : .18f, metal ? .32f : .52f);
            float speed = profile?.FragmentSpeed ?? 1, scale = profile?.FragmentScale ?? 1;
            main.startSpeed = new ParticleSystem.MinMaxCurve((metal ? 1.4f : .3f) * speed, (metal ? 3f : .8f) * speed);
            main.startSize = new ParticleSystem.MinMaxCurve((metal ? .014f : .025f) * scale,
                (metal ? .032f : .075f) * scale);
            main.startColor = body ? new Color(.9f, .3f, .4f, .7f) :
                metal ? new Color(1f, .95f, .8f) : new Color(.55f, .57f, .61f, .9f);
            main.startRotation =
                metal ? new ParticleSystem.MinMaxCurve(0) : new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
            var color = particles.colorOverLifetime;
            color.color = metal ? m_SparkCooling : m_FragmentFade;
            var shape = particles.shape;
            shape.angle = metal ? 62f : body ? 32f : 48f;
            var renderer = effect.FragmentRenderer;
            SetLayer(renderer, metal ? 1 : body ? 2 : 0);
            renderer.renderMode = metal ? ParticleSystemRenderMode.Stretch : ParticleSystemRenderMode.Billboard;
            renderer.lengthScale = metal ? 2.8f : 1f;
            renderer.velocityScale = metal ? .075f : 0;
            particles.Play(false);
            particles.Emit(Mathf.Clamp(Mathf.RoundToInt((metal ? 10 : body ? 6 : 8) * (profile?.FragmentCount ?? 1)), 1,
                12));

            // 对象复用时重置这一层效果，避免上次撞岩石的灰尘留到下一次金属碰撞上。
            // 月尘和碎片共用低重力抛物线。没有额外湍流或循环。
            var surface = effect.Surface;
            var surfaceMain = surface.main;
            SetLayer(effect.SurfaceRenderer, metal ? 4 : 3);
            if (!metal && !body) {
                surfaceMain.startLifetime = new ParticleSystem.MinMaxCurve(.3f, .62f);
                surfaceMain.startSpeed = new ParticleSystem.MinMaxCurve(.18f * speed, .48f * speed);
                surfaceMain.startSize = new ParticleSystem.MinMaxCurve(.12f * scale, .23f * scale);
                surfaceMain.startColor = new Color(.56f, .59f, .64f, .34f);
                surface.Play(false);
                surface.Emit(Mathf.Clamp(Mathf.RoundToInt(3 * (profile?.FragmentCount ?? 1)), 1, 3));
            }
            if (profile?.ImpactFlash == true && !body) {
                surface.Play(false);
                surface.Emit(new ParticleSystem.EmitParams {
                    startSize = .14f * Mathf.Sqrt(scale), startLifetime = .065f,
                    velocity = Vector3.zero, startColor = new Color(.65f, .9f, 1f, .8f)
                }, 1);
            }
        }

        private void Update() {
            foreach (var effect in m_Pool)
                if (effect != null && effect.Contact.enabled && Time.time >= effect.ContactUntil)
                    effect.Contact.enabled = false;
        }

        private void OnDisable() {
            foreach (var effect in m_Pool)
                if (effect != null) {
                    effect.Contact.enabled = false;
                    effect.Fragments.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                    effect.Surface.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                }
        }

        private void OnDestroy() {
            if (m_ContactMesh) {
                if (Application.isPlaying)
                    Destroy(m_ContactMesh);
                else
                    DestroyImmediate(m_ContactMesh);
            }
        }
    }
}