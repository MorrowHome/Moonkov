// 客户端根据异形已同步的攻击快照画预警、喷吐轨迹和落点效果。
// 攻击时钟跟 AlienCombatPattern 共用，命中结算仍在 ProceduralAlienEnemy 的服务端分支。
using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Unity.MP_FPS {
    /// <summary>客户端按已确认的快照显示实体攻击效果。
    /// 即使中途加入一段正在进行的攻势，选目标和伤害也由服务端处理。</summary>
    public sealed class AlienAttackPresentation : IDisposable {
        private readonly GameObject m_Root;
        private readonly AlienCreatureProfile m_Profile;
        private readonly AlienVoidStyle m_Style;
        private readonly LineRenderer[] m_Streams = new LineRenderer[4];
        private readonly LineRenderer[] m_Warnings = new LineRenderer[10];
        private Material m_WarningMaterial;
        private uint m_WarningSequence;
        private Transform m_Globule;
        private TrailRenderer m_Wake;
        private ParticleSystem m_Droplets;
        private AlienAttackEvents m_Events;
        private uint m_Sequence;
        private int m_LastPulse = -1;
        private bool m_Observed;
        private readonly Transform[] m_Impacts = new Transform[3];
        private readonly MeshFilter[] m_ImpactFilters = new MeshFilter[3];
        private Mesh m_Spike, m_Residue;
        private Material m_ImpactMaterial;

        public AlienAttackPresentation(Transform owner, AlienCreatureProfile profile = null) {
            m_Root = new GameObject("Alien attack effects");
            m_Root.transform.SetParent(owner, false);
            m_Profile = profile;
            m_Style = Resources.Load<AlienVoidStyle>("Moonkov/AlienVoid");
        }

        public void Observe(ProceduralAlienEnemy.State state, AlienCombatPattern pattern, Vector3 socket = default) {
            // 每帧先把旧落点网格收起，再按最新的攻击阶段决定哪些重新出现，攻击结束后不会留一堆假危险区。
            foreach (var impact in m_Impacts)
                if (impact)
                    impact.gameObject.SetActive(false);
            if (m_Style && m_Profile && (m_Profile.VoidLiquid || pattern.Kind == AlienAttackKind.MucusSpit))
                DrawLiquidAttack(state, pattern, socket);
            // 命中事件只消费一次。颗粒根据服务端命中法线喷出，撞墙和击中角色的数量也不同。
            if (m_Events.Impact(state) && m_Droplets) {
                m_Droplets.transform.SetPositionAndRotation(state.ImpactPoint,
                    Quaternion.FromToRotation(Vector3.up, state.ImpactNormal));
                m_Droplets.Play();
                m_Droplets.Emit(state.ImpactKind == AlienImpactKind.World ? 24 : 36);
            }
            DrawWarnings(state, pattern);
            // 命中网格、伤害和音效按同一个攻击节拍开始。
            if (state.Action != AlienAction.Strike || state.AttackSequence == 0 ||
                pattern.PulseAt(state.ActionTime) < 0)
                return;
            if (pattern.Kind != AlienAttackKind.RootSpikes && pattern.Kind != AlienAttackKind.AcidSpit &&
                pattern.Kind != AlienAttackKind.SlimePulse && pattern.Kind != AlienAttackKind.TentacleSlam)
                return;
            // 定位共享攻击节拍的中心。多点技能围绕这个位置放出各块落点，画面时间跟伤害时间对得上。
            var centre = pattern.PulsePoint(state.AttackPoint, state.AttackDirection,
                pattern.PulseAt(state.ActionTime));
            if (pattern.Kind == AlienAttackKind.SlimePulse)
                centre = (Vector3)state.AttackOrigin - Vector3.up * (.56f * state.Scale);
            centre.y += .07f;
            int patches = pattern.Kind == AlienAttackKind.RootSpikes ? 3 : 1;
            var right = Vector3.Cross(Vector3.up, state.AttackDirection).normalized;
            for (int patch = 0; patch < patches; patch++) {
                var point = centre +
                            right * (patch == 0 ? 0 : patch == 1 ? pattern.Radius * 2.7f : -pattern.Radius * 2.7f);
                DrawImpact(patch, point, pattern,
                    state.ActionTime - pattern.PulseTime(pattern.PulseAt(state.ActionTime)));
            }
        }

        // 同一轮攻击使用服务器选定的起点、方向和目标。预警画出来之后不会跟着目标偷偷移位。
        private void DrawWarnings(ProceduralAlienEnemy.State state, AlienCombatPattern pattern) {
            foreach (var line in m_Warnings)
                if (line)
                    line.enabled = false;
            if (!m_Profile || !m_Profile.Boss || state.AttackSequence == 0 ||
                (state.Action != AlienAction.Windup && state.Action != AlienAction.Strike))
                return;
            if (!m_WarningMaterial)
                m_WarningMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"))
                    { hideFlags = HideFlags.DontSave };
            float elapsed = state.Action == AlienAction.Windup
                ? state.ActionTime / Mathf.Max(.01f, pattern.Windup)
                : Mathf.Repeat(state.ActionTime - .08f, pattern.Active / Mathf.Max(1, pattern.Pulses)) /
                  (pattern.Active / Mathf.Max(1, pattern.Pulses));
            m_WarningMaterial.SetColor("_BaseColor",
                Color.Lerp(new Color(.7f, .17f, .025f), new Color(1, .7f, .18f), Mathf.Clamp01(elapsed)));
            bool sweep = pattern.Kind == AlienAttackKind.TentacleSweep || pattern.Kind == AlienAttackKind.SpiderRake;
            if (pattern.Kind == AlienAttackKind.SpiderRush) {
                var lane = Warning(0);
                if (m_WarningSequence != state.AttackSequence) {
                    m_WarningSequence = state.AttackSequence;
                    var start = (Vector3)state.AttackOrigin - Vector3.up * (.56f * state.Scale);
                    Vector3 end = state.AttackPoint;
                    Vector3 heading = state.AttackDirection;
                    // 胶囊形预警线覆盖和伤害扫描相同的半径。
                    lane.positionCount = 27;
                    for (int cap = 0; cap < 2; cap++)
                    for (int i = 0; i <= 12; i++) {
                        float angle = -90 + i * 15 + cap * 180;
                        lane.SetPosition(cap * 13 + i,
                            WarningGround((cap == 0 ? end : start) +
                                          Quaternion.AngleAxis(angle, Vector3.up) * heading * pattern.Radius));
                    }
                    lane.SetPosition(26, lane.GetPosition(0));
                }
                return;
            }
            bool spit = pattern.Kind == AlienAttackKind.MucusSpit;
            var centre = sweep || pattern.Kind == AlienAttackKind.SlimePulse
                ? (Vector3)state.AttackOrigin - Vector3.up * (.56f * state.Scale)
                : (Vector3)state.AttackPoint;
            Vector3 direction = state.AttackDirection;
            if (direction.sqrMagnitude < .01f) direction = Vector3.forward;
            var right = Vector3.Cross(Vector3.up, direction).normalized;
            int patches = pattern.Kind == AlienAttackKind.RootSpikes ? 3 : 1;
            int rows = pattern.Kind == AlienAttackKind.RootSpikes ? pattern.Pulses : 1;
            if (m_WarningSequence == state.AttackSequence && m_Warnings[0]) {
                for (int row = 0; row < rows; row++)
                for (int patch = 0; patch < patches; patch++)
                    m_Warnings[row * patches + patch].enabled = state.Action == AlienAction.Windup ||
                                                                row >= pattern.PulseAt(state.ActionTime);
                if (spit && m_Warnings[9]) m_Warnings[9].enabled = true;
                return;
            }
            m_WarningSequence = state.AttackSequence;
            for (int row = 0; row < rows; row++)
            for (int patch = 0; patch < patches; patch++) {
                var line = Warning(row * patches + patch);
                int segments = sweep ? 40 : 24;
                line.positionCount = sweep ? segments + 3 : segments + 1;
                var point = pattern.PulsePoint(centre, direction, row) +
                            right * (patch == 0 ? 0 : patch == 1 ? pattern.Radius * 2.7f : -pattern.Radius * 2.7f);
                if (sweep) line.SetPosition(0, WarningGround(point));
                for (int i = 0; i <= segments; i++) {
                    float angle = sweep ? Mathf.Lerp(-65, 65, i / (float)segments) : i * 360f / segments;
                    var vertex = point + Quaternion.AngleAxis(angle, Vector3.up) * direction *
                        (spit ? .65f : pattern.Radius);
                    line.SetPosition(i + (sweep ? 1 : 0), spit ? vertex : WarningGround(vertex));
                }
                if (sweep) line.SetPosition(segments + 2, WarningGround(point));
                line.enabled = state.Action == AlienAction.Windup || row >= pattern.PulseAt(state.ActionTime);
            }
            if (spit) {
                var path = Warning(9);
                path.positionCount = 25;
                float duration =
                    AlienMucusTrajectory.FlightTime(state.AttackOrigin, state.AttackPoint, m_Profile.MucusSpeed);
                for (int i = 0; i < 25; i++)
                    path.SetPosition(i,
                        AlienMucusTrajectory.Point(state.AttackOrigin, state.AttackPoint, m_Profile.MucusSpeed,
                            duration * i / 24, state.MucusLowArc));
            }
        }

        // 预警线按号复用，只有第一次用到某条线时才创建。
        private LineRenderer Warning(int index) {
            if (!m_Warnings[index]) {
                var go = new GameObject("Boss committed attack warning");
                go.transform.SetParent(m_Root.transform, false);
                var line = go.AddComponent<LineRenderer>();
                m_Warnings[index] = line;
                line.sharedMaterial = m_WarningMaterial;
                line.useWorldSpace = true;
                line.widthMultiplier = .1f;
                line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                line.receiveShadows = false;
            }
            m_Warnings[index].enabled = true;
            return m_Warnings[index];
        }

        // 将预警轮廓投到脚下地面。候选点高度和场景真实地面有差异时，更容易看清要躲哪里的图形。
        private static Vector3 WarningGround(Vector3 point) {
            return UnityEngine.Physics.Raycast(point + Vector3.up * 1.5f, Vector3.down, out var hit, 3,
                LayerMask.GetMask("Default", "Ground"), QueryTriggerInteraction.Ignore)
                ? hit.point + Vector3.up * .06f
                : point + Vector3.up * .06f;
        }

        // 不同技能按当前节拍显示液体沉积或尖刺等效果。重用相同实例时仍要选择对应的网格。
        private void DrawImpact(int index, Vector3 point, AlienCombatPattern pattern, float age) {
            bool roots = pattern.Kind == AlienAttackKind.RootSpikes;
            if (!m_ImpactMaterial) {
                m_ImpactMaterial = m_Style
                    ? new Material(m_Style.Skin)
                    : new Material(Shader.Find("Universal Render Pipeline/Lit"));
                m_ImpactMaterial.hideFlags = HideFlags.DontSave;
                m_ImpactMaterial.SetFloat("_Smoothness", roots ? .45f : .85f);
            }
            m_ImpactMaterial.SetColor("_BaseColor", new Color(.010f, .012f, .016f));
            if (m_Style) m_ImpactMaterial.SetFloat("_VoidCharge", .4f + .6f * Mathf.Sin(age * 12));
            if (!m_Impacts[index]) {
                var go = new GameObject(roots ? "Erupting root spike" :
                    pattern.Kind == AlienAttackKind.TentacleSlam ? "Impact residue" : "Acid impact pool");
                go.transform.SetParent(m_Root.transform, false);
                m_ImpactFilters[index] = go.AddComponent<MeshFilter>();
                go.AddComponent<MeshRenderer>().sharedMaterial = m_ImpactMaterial;
                m_Impacts[index] = go.transform;
            }
            // 根须 Boss 可以交替使用地刺和环形脉冲。
            // 重用落点网格时更新形状，别留着上一招的外形。
            if (roots && !m_Spike) m_Spike = BuildSpike();
            if (!roots && !m_Residue) m_Residue = BuildResidue();
            var impact = m_Impacts[index];
            m_ImpactFilters[index].sharedMesh = roots ? m_Spike : m_Residue;
            m_ImpactMaterial.SetFloat("_Smoothness", roots ? .45f : .85f);
            impact.gameObject.SetActive(true);
            impact.position = point;
            if (roots) {
                float pulse = 1 - Mathf.Clamp01(age / .4f);
                impact.localScale = new Vector3(pattern.Radius * 1.7f, .15f + pulse * 2, pattern.Radius * 1.7f);
            } else { impact.localScale = new Vector3(pattern.Radius * 2, .04f, pattern.Radius * 2); }
        }

        // 喷吐和液体攻击根据快照算流向，已经在飞的液滴按发射时间和弹道移动。
        private void DrawLiquidAttack(ProceduralAlienEnemy.State state, AlienCombatPattern pattern, Vector3 socket) {
            if (!m_Globule) CreateLiquidEffects();
            bool windup = state.Action == AlienAction.Windup;
            bool strike = state.Action == AlienAction.Strike;
            bool newSequence = state.AttackSequence != m_Sequence;
            if (newSequence) {
                m_Sequence = state.AttackSequence;
                m_LastPulse = -1;
                m_Wake.Clear();
            }
            foreach (var stream in m_Streams) stream.enabled = false;
            m_Globule.gameObject.SetActive(false);
            m_Wake.emitting = strike && (pattern.Kind == AlienAttackKind.RoofPounce ||
                                         pattern.Kind == AlienAttackKind.FaceLeap ||
                                         pattern.Kind == AlienAttackKind.Charge ||
                                         pattern.Kind == AlienAttackKind.Bite ||
                                         pattern.Kind == AlienAttackKind.SpiderRush);
            m_Wake.transform.position = socket;
            if (state.Action == AlienAction.Dead) {
                m_Wake.Clear();
                m_Droplets.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                m_Observed = true;
                return;
            }
            Vector3 direction = state.AttackDirection;
            if (direction.sqrMagnitude < .01f) direction = m_Root.transform.forward;
            var right = Vector3.Cross(Vector3.up, direction).normalized;
            float scale = m_Profile.Scale;
            if (pattern.Kind == AlienAttackKind.MucusSpit && (windup || strike)) {
                float flight =
                    AlienMucusTrajectory.FlightTime(state.AttackOrigin, state.AttackPoint, m_Profile.MucusSpeed);
                m_Globule.gameObject.SetActive(windup || (state.MucusInFlight && state.ActionTime <= flight + .12f));
                m_Globule.position = windup
                    ? socket
                    : AlienMucusTrajectory.Point(state.AttackOrigin, state.AttackPoint, m_Profile.MucusSpeed,
                        Mathf.Min(state.ActionTime, flight + .12f), state.MucusLowArc);
                m_Globule.localScale = Vector3.one *
                                       (windup ? .12f + .1f * Mathf.Clamp01(state.ActionTime / pattern.Windup) : .28f);
            }
            if (pattern.Kind == AlienAttackKind.AcidSpit && windup) {
                float t = Mathf.Clamp01(state.ActionTime / Mathf.Max(.05f, pattern.Windup));
                m_Globule.gameObject.SetActive(true);
                // 蓄力后段会看到一团液体飞过去。
                // 权威伤害区域生效时液团落地。画面不额外叠地面范围线。
                float flight = Mathf.SmoothStep(0, 1, Mathf.Clamp01((t - .65f) / .35f));
                m_Globule.position = Vector3.Lerp(socket, state.AttackPoint, flight) +
                                     Vector3.up * (Mathf.Sin(flight * Mathf.PI) * 1.2f);
                m_Globule.localScale = Vector3.one * (.12f + .18f * t) * Mathf.Max(.6f, scale);
            }
            if (strike && pattern.Kind == AlienAttackKind.LeechDrain) {
                var end = (Vector3)state.AttackPoint + Vector3.up * .75f;
                end = socket + Vector3.ClampMagnitude(end - socket, pattern.Radius + .2f);
                for (int i = 0; i < 3; i++) Stream(i, socket, end, .016f + i * .006f, state.ActionTime, true);
            }
            if (strike && (pattern.Kind == AlienAttackKind.TentacleSweep ||
                           pattern.Kind == AlienAttackKind.StiltRake)) {
                float interval = pattern.Active / Mathf.Max(1, pattern.Pulses);
                int motionPulse = Mathf.Clamp(Mathf.FloorToInt(state.ActionTime / interval), 0,
                    Mathf.Max(0, pattern.Pulses - 1));
                float pulseAge = state.ActionTime - motionPulse * interval;
                float t = Mathf.Clamp01(pulseAge / .08f);
                float strength = 1 - Mathf.Clamp01((pulseAge - .08f) / .16f);
                float angle = Mathf.Lerp(-65, 65, t);
                for (int i = 0; i < 3; i++) {
                    var a = socket + Vector3.up * (i - .7f) * .12f * scale;
                    var tip = a + Quaternion.AngleAxis(angle, Vector3.up) * direction * pattern.Range;
                    Stream(i, a, tip, .045f * scale * strength, state.ActionTime, false);
                }
            }
            if (strike && state.AttackSequence != 0) {
                int pulse = pattern.PulseAt(state.ActionTime);
                bool fresh = (!newSequence && m_Observed) || state.ActionTime < .12f;
                if (pulse >= 0 && pulse != m_LastPulse) {
                    m_LastPulse = pulse;
                    var point =
                        pattern.Kind == AlienAttackKind.AcidSpit || pattern.Kind == AlienAttackKind.RootSpikes ||
                        pattern.Kind == AlienAttackKind.TentacleSlam
                            ? pattern.PulsePoint(state.AttackPoint, direction, pulse) + Vector3.up * .15f
                            : socket;
                    if (fresh) {
                        m_Droplets.transform.position = point;
                        m_Droplets.Play();
                        m_Droplets.Emit(pattern.Kind == AlienAttackKind.TentacleSlam ? 32 : 16);
                    }
                }
            }
            m_Observed = true;
        }

        // 用线段表示从起点到落点的一股液流。宽度和流动方向依当前阶段改变。
        private void Stream(int index, Vector3 from, Vector3 to, float width, float age, bool reflux) {
            var stream = m_Streams[index];
            stream.enabled = true;
            stream.widthMultiplier = Mathf.Max(.002f, width);
            for (int j = 0; j < 16; j++) {
                float t = j / 15f, envelope = Mathf.Sin(t * Mathf.PI);
                var side = Vector3.Cross((to - from).normalized, Vector3.up);
                var wave = side * (Mathf.Sin(t * 17 - age * (reflux ? -14 : 8) + index * 2) * .065f) +
                           Vector3.up * (Mathf.Cos(t * 12 + age * 9 + index) * .04f - .06f);
                stream.SetPosition(j, Vector3.Lerp(from, to, t) + wave * envelope);
            }
        }

        private void CreateLiquidEffects() {
            var globule = new GameObject("Coalesced black liquid spit");
            globule.transform.SetParent(m_Root.transform, false);
            globule.AddComponent<MeshFilter>().sharedMesh = m_Style.Sphere;
            globule.AddComponent<MeshRenderer>().sharedMaterial = m_Style.Droplet;
            m_Globule = globule.transform;
            for (int i = 0; i < m_Streams.Length; i++) {
                var stream = new GameObject("Viscous attack thread " + i).AddComponent<LineRenderer>();
                stream.transform.SetParent(m_Root.transform, false);
                stream.sharedMaterial = m_Style.Filament;
                stream.positionCount = 16;
                stream.useWorldSpace = true;
                stream.startColor = Color.white;
                stream.endColor = new Color(1, 1, 1, .1f);
                stream.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                stream.receiveShadows = false;
                m_Streams[i] = stream;
            }
            m_Wake = new GameObject("Shed liquid wake").AddComponent<TrailRenderer>();
            m_Wake.transform.SetParent(m_Root.transform, false);
            m_Wake.sharedMaterial = m_Style.Filament;
            m_Wake.time = .28f;
            m_Wake.minVertexDistance = .035f;
            m_Wake.startWidth = .22f * Mathf.Max(.4f, m_Profile.Scale);
            m_Wake.endWidth = 0;
            m_Wake.startColor = Color.white;
            m_Wake.endColor = new Color(1, 1, 1, 0);
            m_Wake.emitting = false;
            m_Wake.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            m_Droplets = new GameObject("Shed black droplets").AddComponent<ParticleSystem>();
            m_Droplets.transform.SetParent(m_Root.transform, false);
            m_Droplets.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = m_Droplets.main;
            main.loop = false;
            main.playOnAwake = false;
            main.duration = 1;
            main.maxParticles = 48;
            main.startLifetime = new ParticleSystem.MinMaxCurve(.25f, .65f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(.6f, 2.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(.025f, .11f);
            main.gravityModifier = .5f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var emission = m_Droplets.emission;
            emission.enabled = false;
            var shape = m_Droplets.shape;
            shape.shapeType = ParticleSystemShapeType.Hemisphere;
            shape.radius = .15f;
            var renderer = m_Droplets.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = m_Style.Sphere;
            renderer.sharedMaterial = m_Style.Droplet;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        private static Mesh BuildSpike() {
            // 好几股弯曲并逐渐收细的液流从同一块不规则沉积中冲出。
            // 液流表现的是本次攻击，实际碰撞半径由服务端规则决定。
            const int jets = 5, rings = 6, sides = 8;
            var vertices = new Vector3[jets * rings * sides];
            int[] triangles = new int[jets * (rings - 1) * sides * 6];
            int triangle = 0;
            for (int jet = 0; jet < jets; jet++)
            for (int ring = 0; ring < rings; ring++) {
                float t = ring / (rings - 1f), angle = jet * 2.39996f;
                var axis = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                var centre = axis * (jet == 0 ? .03f : .24f) + Vector3.up * t * (jet == 0 ? 1.15f : .65f + jet * .07f) +
                             axis * (t * t * .28f);
                float radius = Mathf.Lerp(.15f, .003f, Mathf.Pow(t, .75f));
                for (int side = 0; side < sides; side++) {
                    int vertex = jet * rings * sides + ring * sides + side;
                    float a = side * Mathf.PI * 2 / sides;
                    vertices[vertex] = centre + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * radius;
                    if (ring == rings - 1) continue;
                    int next = jet * rings * sides + ring * sides + (side + 1) % sides;
                    triangles[triangle++] = vertex;
                    triangles[triangle++] = vertex + sides;
                    triangles[triangle++] = next;
                    triangles[triangle++] = next;
                    triangles[triangle++] = vertex + sides;
                    triangles[triangle++] = next + sides;
                }
            }
            var mesh = new Mesh { name = "Alien root eruption", hideFlags = HideFlags.DontSave };
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh BuildResidue() {
            // 发光而不平整的沉积属于落点效果，精确判定范围另由规则定义。
            const int sides = 24;
            var vertices = new Vector3[sides + 1];
            int[] triangles = new int[sides * 3];
            vertices[0] = Vector3.up * .25f;
            for (int i = 0; i < sides; i++) {
                float angle = i * Mathf.PI * 2 / sides;
                float radius = .43f + .055f * Mathf.Sin(angle * 5 + .7f) + .025f * Mathf.Cos(angle * 9);
                vertices[i + 1] = new Vector3(Mathf.Cos(angle) * radius, 0, Mathf.Sin(angle) * radius);
                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = (i + 1) % sides + 1;
                triangles[i * 3 + 2] = i + 1;
            }
            var mesh = new Mesh { name = "Irregular alien residue", hideFlags = HideFlags.DontSave };
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        // 连同根对象释放程序生成的材质和网格，不继续占用显存。
        public void Dispose() {
            Destroy(m_Root);
            Destroy(m_ImpactMaterial);
            Destroy(m_WarningMaterial);
            Destroy(m_Spike);
            Destroy(m_Residue);
        }

        private static void Destroy(Object value) {
            if (!value) return;
            if (Application.isPlaying)
                Object.Destroy(value);
            else
                Object.DestroyImmediate(value);
        }
    }
}