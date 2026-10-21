using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Unity.MP_FPS
{
    /// <summary>Client-only physical attack effects from committed snapshots. No damage or target
    /// selection occurs here, including when joining an attack already in progress.</summary>
    public sealed class AlienAttackPresentation : IDisposable
    {
        private readonly GameObject m_Root;
        private readonly AlienCreatureProfile m_Profile;
        private readonly AlienVoidStyle m_Style;
        private readonly LineRenderer[] m_Streams = new LineRenderer[4];
        private readonly LineRenderer[] m_Warnings = new LineRenderer[4];
        private Material m_WarningMaterial;
        private uint m_WarningSequence;
        private Transform m_Globule;
        private TrailRenderer m_Wake;
        private ParticleSystem m_Droplets;
        private uint m_Sequence;
        private int m_LastPulse = -1;
        private bool m_Observed;
        private readonly Transform[] m_Impacts = new Transform[3];
        private Mesh m_Spike, m_Residue;
        private Material m_ImpactMaterial;
        public AlienAttackPresentation(Transform owner, AlienCreatureProfile profile = null)
        {
            m_Root = new GameObject("Alien attack effects"); m_Root.transform.SetParent(owner, false);
            m_Profile = profile;
            m_Style = Resources.Load<AlienVoidStyle>("Moonkov/AlienVoid");
        }
        public void Observe(ProceduralAlienEnemy.State state, AlienCombatPattern pattern, Vector3 socket = default)
        {
            foreach (var impact in m_Impacts) if (impact) impact.gameObject.SetActive(false);
            if (m_Style && m_Profile && (m_Profile.VoidLiquid || pattern.Kind == AlienAttackKind.MucusSpit)) DrawLiquidAttack(state, pattern, socket);
            DrawWarnings(state, pattern);
            // Impact meshes start on the same pulse clock as damage and audio.
            if (state.Action != AlienAction.Strike || state.AttackSequence == 0 || pattern.PulseAt(state.ActionTime) < 0) return;
            if (pattern.Kind != AlienAttackKind.RootSpikes && pattern.Kind != AlienAttackKind.AcidSpit &&
                pattern.Kind != AlienAttackKind.SlimePulse && pattern.Kind != AlienAttackKind.TentacleSlam) return;
            Vector3 centre = state.AttackPoint;
            if (pattern.Kind == AlienAttackKind.SlimePulse) centre = state.AttackOrigin;
            centre.y += .07f;
            int patches = pattern.Kind == AlienAttackKind.RootSpikes ? 3 : 1;
            Vector3 right = Vector3.Cross(Vector3.up, state.AttackDirection).normalized;
            for (int patch = 0; patch < patches; patch++)
            {
                Vector3 point = centre + right * (patch == 0 ? 0 : patch == 1 ? pattern.Radius * 2.7f : -pattern.Radius * 2.7f);
                DrawImpact(patch, point, pattern, state.ActionTime - pattern.PulseTime(pattern.PulseAt(state.ActionTime)));
            }
        }
        private void DrawWarnings(ProceduralAlienEnemy.State state, AlienCombatPattern pattern)
        {
            foreach (var line in m_Warnings) if (line) line.enabled = false;
            if (!m_Profile || !m_Profile.Boss || state.AttackSequence == 0 ||
                state.Action != AlienAction.Windup && state.Action != AlienAction.Strike) return;
            if (!m_WarningMaterial)
            {
                m_WarningMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { hideFlags = HideFlags.DontSave };
            }
            float elapsed = state.Action == AlienAction.Windup ? state.ActionTime / Mathf.Max(.01f, pattern.Windup) :
                Mathf.Repeat(state.ActionTime - .08f, pattern.Active / Mathf.Max(1, pattern.Pulses)) / (pattern.Active / Mathf.Max(1, pattern.Pulses));
            m_WarningMaterial.SetColor("_BaseColor", Color.Lerp(new Color(.7f, .17f, .025f), new Color(1, .7f, .18f), Mathf.Clamp01(elapsed)));
            bool sweep = pattern.Kind == AlienAttackKind.TentacleSweep;
            bool spit = pattern.Kind == AlienAttackKind.MucusSpit;
            Vector3 centre = sweep || pattern.Kind == AlienAttackKind.SlimePulse ?
                (Vector3)state.AttackOrigin - Vector3.up * (.56f * state.Scale) : (Vector3)state.AttackPoint;
            Vector3 direction = state.AttackDirection;
            if (direction.sqrMagnitude < .01f) direction = Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, direction).normalized;
            int patches = pattern.Kind == AlienAttackKind.RootSpikes ? 3 : 1;
            if (m_WarningSequence == state.AttackSequence && m_Warnings[0])
            {
                for (int i = 0; i < patches; i++) m_Warnings[i].enabled = true;
                if (spit && m_Warnings[3]) m_Warnings[3].enabled = true;
                return;
            }
            m_WarningSequence = state.AttackSequence;
            for (int patch = 0; patch < patches; patch++)
            {
                var line = Warning(patch);
                const int segments = 40;
                line.positionCount = sweep ? segments + 3 : segments + 1;
                Vector3 point = centre + right * (patch == 0 ? 0 : patch == 1 ? pattern.Radius * 2.7f : -pattern.Radius * 2.7f);
                if (sweep) line.SetPosition(0, WarningGround(point));
                for (int i = 0; i <= segments; i++)
                {
                    float angle = sweep ? Mathf.Lerp(-65, 65, i / (float)segments) : i * 360f / segments;
                    Vector3 vertex = point + Quaternion.AngleAxis(angle, Vector3.up) * direction * (spit ? .65f : pattern.Radius);
                    line.SetPosition(i + (sweep ? 1 : 0), spit ? vertex : WarningGround(vertex));
                }
                if (sweep) line.SetPosition(segments + 2, WarningGround(point));
            }
            if (spit)
            {
                var path = Warning(3); path.positionCount = 25;
                float duration = AlienMucusTrajectory.FlightTime(state.AttackOrigin, state.AttackPoint, m_Profile.MucusSpeed);
                for (int i = 0; i < 25; i++) path.SetPosition(i,
                    AlienMucusTrajectory.Point(state.AttackOrigin, state.AttackPoint, m_Profile.MucusSpeed, duration * i / 24));
            }
        }
        private LineRenderer Warning(int index)
        {
            if (!m_Warnings[index])
            {
                var go = new GameObject("Boss committed attack warning"); go.transform.SetParent(m_Root.transform, false);
                var line = go.AddComponent<LineRenderer>(); m_Warnings[index] = line;
                line.sharedMaterial = m_WarningMaterial; line.useWorldSpace = true;
                line.widthMultiplier = .1f; line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                line.receiveShadows = false;
            }
            m_Warnings[index].enabled = true;
            return m_Warnings[index];
        }
        private static Vector3 WarningGround(Vector3 point)
        {
            return UnityEngine.Physics.Raycast(point + Vector3.up * 1.5f, Vector3.down, out var hit, 3,
                LayerMask.GetMask("Default", "Ground"), QueryTriggerInteraction.Ignore) ? hit.point + Vector3.up * .06f : point + Vector3.up * .06f;
        }
        private void DrawImpact(int index, Vector3 point, AlienCombatPattern pattern, float age)
        {
            bool roots = pattern.Kind == AlienAttackKind.RootSpikes;
            if (!m_ImpactMaterial)
            {
                m_ImpactMaterial = m_Style ? new Material(m_Style.Skin) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
                m_ImpactMaterial.hideFlags = HideFlags.DontSave;
                m_ImpactMaterial.SetFloat("_Smoothness", roots ? .45f : .85f);
            }
            m_ImpactMaterial.SetColor("_BaseColor", new Color(.010f,.012f,.016f));
            if (m_Style) m_ImpactMaterial.SetFloat("_VoidCharge", .4f + .6f * Mathf.Sin(age * 12));
            if (!m_Impacts[index])
            {
                var go = new GameObject(roots ? "Erupting root spike" : pattern.Kind == AlienAttackKind.TentacleSlam ? "Impact residue" : "Acid impact pool"); go.transform.SetParent(m_Root.transform, false);
                var filter = go.AddComponent<MeshFilter>(); go.AddComponent<MeshRenderer>().sharedMaterial = m_ImpactMaterial;
                if (roots)
                {
                    if (!m_Spike) m_Spike = BuildSpike();
                    filter.sharedMesh = m_Spike;
                }
                else
                {
                    if (!m_Residue) m_Residue = BuildResidue();
                    filter.sharedMesh = m_Residue;
                }
                m_Impacts[index] = go.transform;
            }
            var impact = m_Impacts[index]; impact.gameObject.SetActive(true); impact.position = point;
            if (roots)
            {
                float pulse = 1 - Mathf.Clamp01(age / .4f);
                impact.localScale = new Vector3(pattern.Radius * 1.7f, .15f + pulse * 2, pattern.Radius * 1.7f);
            }
            else impact.localScale = new Vector3(pattern.Radius * 2, .04f, pattern.Radius * 2);
        }
        private void DrawLiquidAttack(ProceduralAlienEnemy.State state, AlienCombatPattern pattern, Vector3 socket)
        {
            if (!m_Globule) CreateLiquidEffects();
            bool windup = state.Action == AlienAction.Windup;
            bool strike = state.Action == AlienAction.Strike;
            bool newSequence = state.AttackSequence != m_Sequence;
            if (newSequence) { m_Sequence = state.AttackSequence; m_LastPulse = -1; m_Wake.Clear(); }
            foreach (var stream in m_Streams) stream.enabled = false;
            m_Globule.gameObject.SetActive(false);
            m_Wake.emitting = strike && (pattern.Kind == AlienAttackKind.RoofPounce || pattern.Kind == AlienAttackKind.FaceLeap ||
                pattern.Kind == AlienAttackKind.Charge || pattern.Kind == AlienAttackKind.Bite);
            m_Wake.transform.position = socket;
            if (state.Action == AlienAction.Dead)
            { m_Wake.Clear(); m_Droplets.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear); m_Observed = true; return; }
            Vector3 direction = state.AttackDirection;
            if (direction.sqrMagnitude < .01f) direction = m_Root.transform.forward;
            Vector3 right = Vector3.Cross(Vector3.up,direction).normalized;
            float scale = m_Profile.Scale;
            if (pattern.Kind == AlienAttackKind.MucusSpit && (windup || strike))
            {
                float flight=AlienMucusTrajectory.FlightTime(state.AttackOrigin,state.AttackPoint,m_Profile.MucusSpeed);
                m_Globule.gameObject.SetActive(windup || state.MucusInFlight && state.ActionTime<=flight+.12f);
                m_Globule.position=windup ? socket : AlienMucusTrajectory.Point(state.AttackOrigin,state.AttackPoint,m_Profile.MucusSpeed,Mathf.Min(state.ActionTime,flight+.12f));
                m_Globule.localScale=Vector3.one*(windup?.12f+.1f*Mathf.Clamp01(state.ActionTime/pattern.Windup):.28f);
            }
            if (pattern.Kind == AlienAttackKind.AcidSpit && windup)
            {
                float t = Mathf.Clamp01(state.ActionTime / Mathf.Max(.05f,pattern.Windup));
                m_Globule.gameObject.SetActive(true);
                // The last part of the windup shows a physical blob in flight; it lands
                // when the authoritative pool activates. There is no ground range overlay.
                float flight = Mathf.SmoothStep(0,1,Mathf.Clamp01((t-.65f)/.35f));
                m_Globule.position = Vector3.Lerp(socket,state.AttackPoint,flight) + Vector3.up*(Mathf.Sin(flight*Mathf.PI)*1.2f);
                m_Globule.localScale = Vector3.one * (.12f + .18f * t) * Mathf.Max(.6f,scale);
            }
            if (strike && pattern.Kind == AlienAttackKind.LeechDrain)
            {
                Vector3 end = (Vector3)state.AttackPoint + Vector3.up*.75f;
                end = socket + Vector3.ClampMagnitude(end-socket,pattern.Radius+.2f);
                for(int i=0;i<3;i++) Stream(i,socket,end,.016f + i*.006f,state.ActionTime, true);
            }
            if (strike && (pattern.Kind == AlienAttackKind.TentacleSweep || pattern.Kind == AlienAttackKind.StiltRake))
            {
                float interval = pattern.Active / Mathf.Max(1, pattern.Pulses);
                int motionPulse = Mathf.Clamp(Mathf.FloorToInt(state.ActionTime / interval), 0, Mathf.Max(0, pattern.Pulses - 1));
                float pulseAge = state.ActionTime - motionPulse * interval;
                float t = Mathf.Clamp01(pulseAge / .08f);
                float strength = 1 - Mathf.Clamp01((pulseAge - .08f) / .16f);
                float angle = Mathf.Lerp(-65,65,t);
                for(int i=0;i<3;i++)
                {
                    Vector3 a = socket + Vector3.up * (i-.7f)*.12f*scale;
                    Vector3 tip = a + Quaternion.AngleAxis(angle,Vector3.up)*direction*pattern.Range;
                    Stream(i,a,tip,.045f*scale*strength,state.ActionTime,false);
                }
            }
            if (strike && state.AttackSequence != 0)
            {
                int pulse = pattern.PulseAt(state.ActionTime);
                bool fresh = (!newSequence && m_Observed) || state.ActionTime < .12f;
                if (pulse >= 0 && pulse != m_LastPulse)
                {
                    m_LastPulse = pulse;
                    Vector3 point = pattern.Kind == AlienAttackKind.AcidSpit || pattern.Kind == AlienAttackKind.RootSpikes || pattern.Kind == AlienAttackKind.TentacleSlam ?
                        (Vector3)state.AttackPoint + Vector3.up*.15f : socket;
                    if (fresh) { m_Droplets.transform.position=point; m_Droplets.Play(); m_Droplets.Emit(pattern.Kind==AlienAttackKind.TentacleSlam ? 32 : 16); }
                }
            }
            m_Observed = true;
        }
        private void Stream(int index, Vector3 from, Vector3 to, float width, float age, bool reflux)
        {
            var stream=m_Streams[index]; stream.enabled=true; stream.widthMultiplier=Mathf.Max(.002f,width);
            for(int j=0;j<16;j++)
            {
                float t=j/15f, envelope=Mathf.Sin(t*Mathf.PI);
                Vector3 side=Vector3.Cross((to-from).normalized,Vector3.up);
                Vector3 wave=side*(Mathf.Sin(t*17-age*(reflux ? -14 : 8)+index*2)*.065f)+Vector3.up*(Mathf.Cos(t*12+age*9+index)*.04f-.06f);
                stream.SetPosition(j,Vector3.Lerp(from,to,t)+wave*envelope);
            }
        }
        private void CreateLiquidEffects()
        {
            var globule=new GameObject("Coalesced black liquid spit"); globule.transform.SetParent(m_Root.transform,false);
            globule.AddComponent<MeshFilter>().sharedMesh=m_Style.Sphere; globule.AddComponent<MeshRenderer>().sharedMaterial=m_Style.Droplet;
            m_Globule=globule.transform;
            for(int i=0;i<m_Streams.Length;i++)
            {
                var stream=new GameObject("Viscous attack thread "+i).AddComponent<LineRenderer>(); stream.transform.SetParent(m_Root.transform,false);
                stream.sharedMaterial=m_Style.Filament; stream.positionCount=16; stream.useWorldSpace=true;
                stream.startColor=Color.white;stream.endColor=new Color(1,1,1,.1f);
                stream.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;stream.receiveShadows=false; m_Streams[i]=stream;
            }
            m_Wake=new GameObject("Shed liquid wake").AddComponent<TrailRenderer>();m_Wake.transform.SetParent(m_Root.transform,false);
            m_Wake.sharedMaterial=m_Style.Filament;m_Wake.time=.28f;m_Wake.minVertexDistance=.035f;
            m_Wake.startWidth=.22f*Mathf.Max(.4f,m_Profile.Scale);m_Wake.endWidth=0;
            m_Wake.startColor=Color.white;m_Wake.endColor=new Color(1,1,1,0);m_Wake.emitting=false;
            m_Wake.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
            m_Droplets=new GameObject("Shed black droplets").AddComponent<ParticleSystem>();m_Droplets.transform.SetParent(m_Root.transform,false);
            m_Droplets.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            var main=m_Droplets.main;main.loop=false;main.playOnAwake=false;main.duration=1;main.maxParticles=48;
            main.startLifetime=new ParticleSystem.MinMaxCurve(.25f,.65f);main.startSpeed=new ParticleSystem.MinMaxCurve(.6f,2.5f);
            main.startSize=new ParticleSystem.MinMaxCurve(.025f,.11f);main.gravityModifier=.5f;main.simulationSpace=ParticleSystemSimulationSpace.World;
            var emission=m_Droplets.emission;emission.enabled=false;var shape=m_Droplets.shape;shape.shapeType=ParticleSystemShapeType.Hemisphere;shape.radius=.15f;
            var renderer=m_Droplets.GetComponent<ParticleSystemRenderer>();renderer.renderMode=ParticleSystemRenderMode.Mesh;
            renderer.mesh=m_Style.Sphere;renderer.sharedMaterial=m_Style.Droplet;renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        private static Mesh BuildSpike()
        {
            // Several bent, tapering liquid jets erupt from one irregular deposit.
            // These are the visible strike, not a preview of its collision radius.
            const int jets=5, rings=6, sides=8;
            var vertices=new Vector3[jets*rings*sides];var triangles=new int[jets*(rings-1)*sides*6];
            int triangle=0;
            for(int jet=0;jet<jets;jet++)
            for(int ring=0;ring<rings;ring++)
            {
                float t=ring/(rings-1f),angle=jet*2.39996f;
                Vector3 axis=new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle));
                Vector3 centre=axis*(jet==0 ? .03f : .24f)+Vector3.up*t*(jet==0 ? 1.15f : .65f+jet*.07f)+axis*(t*t*.28f);
                float radius=Mathf.Lerp(.15f,.003f,Mathf.Pow(t,.75f));
                for(int side=0;side<sides;side++)
                {
                    int vertex=jet*rings*sides+ring*sides+side;
                    float a=side*Mathf.PI*2/sides;
                    vertices[vertex]=centre+new Vector3(Mathf.Cos(a),0,Mathf.Sin(a))*radius;
                    if(ring==rings-1)continue;
                    int next=jet*rings*sides+ring*sides+(side+1)%sides;
                    triangles[triangle++]=vertex;triangles[triangle++]=vertex+sides;triangles[triangle++]=next;
                    triangles[triangle++]=next;triangles[triangle++]=vertex+sides;triangles[triangle++]=next+sides;
                }
            }
            var mesh = new Mesh { name = "Alien root eruption", hideFlags = HideFlags.DontSave };
            mesh.vertices = vertices; mesh.triangles = triangles; mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }
        private static Mesh BuildResidue()
        {
            // An uneven, lit deposit is part of the impact, not an exact range marker.
            const int sides = 24;
            var vertices = new Vector3[sides + 1]; var triangles = new int[sides * 3];
            vertices[0] = Vector3.up * .25f;
            for (int i = 0; i < sides; i++)
            {
                float angle = i * Mathf.PI * 2 / sides;
                float radius = .43f + .055f * Mathf.Sin(angle * 5 + .7f) + .025f * Mathf.Cos(angle * 9);
                vertices[i + 1] = new Vector3(Mathf.Cos(angle) * radius, 0, Mathf.Sin(angle) * radius);
                triangles[i * 3] = 0; triangles[i * 3 + 1] = (i + 1) % sides + 1; triangles[i * 3 + 2] = i + 1;
            }
            var mesh = new Mesh { name = "Irregular alien residue", hideFlags = HideFlags.DontSave };
            mesh.vertices = vertices; mesh.triangles = triangles; mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }
        public void Dispose() { Destroy(m_Root); Destroy(m_ImpactMaterial); Destroy(m_WarningMaterial); Destroy(m_Spike); Destroy(m_Residue); }
        private static void Destroy(Object value) { if (!value) return; if (Application.isPlaying) Object.Destroy(value); else Object.DestroyImmediate(value); }
    }
}
