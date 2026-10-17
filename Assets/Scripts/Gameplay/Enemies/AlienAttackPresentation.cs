using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Unity.MP_FPS
{
    /// <summary>Client-only warnings from committed snapshots. No damage or target
    /// selection occurs here, including when joining an attack already in progress.</summary>
    public sealed class AlienAttackPresentation : IDisposable
    {
        private readonly GameObject m_Root;
        private readonly LineRenderer[] m_Lines = new LineRenderer[3];
        private readonly Material m_Material;
        private readonly Transform[] m_Impacts = new Transform[3];
        private Mesh m_Spike;
        private Material m_ImpactMaterial;
        private readonly Vector3[] m_Points = new Vector3[49];
        public AlienAttackPresentation(Transform owner)
        {
            m_Root = new GameObject("Alien attack warning"); m_Root.transform.SetParent(owner, false);
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (!shader) shader = Shader.Find("Unlit/Color");
            m_Material = new Material(shader) { hideFlags = HideFlags.DontSave };
            for (int i = 0; i < m_Lines.Length; i++)
            {
                var go = new GameObject("Warning boundary " + i); go.transform.SetParent(m_Root.transform, false);
                var line = go.AddComponent<LineRenderer>(); line.sharedMaterial = m_Material;
                line.useWorldSpace = true; line.loop = false; line.widthMultiplier = .045f;
                line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; line.receiveShadows = false;
                m_Lines[i] = line; line.enabled = false;
            }
        }
        public void Observe(ProceduralAlienEnemy.State state, AlienCombatPattern pattern, bool boss)
        {
            bool visible = (state.Action == AlienAction.Windup || state.Action == AlienAction.Strike) && state.AttackSequence > 0;
            for (int i = 0; i < m_Lines.Length; i++) m_Lines[i].enabled = false;
            foreach (var impact in m_Impacts) if (impact) impact.gameObject.SetActive(false);
            if (!visible || pattern.Kind == AlienAttackKind.FaceLeap || pattern.Kind == AlienAttackKind.RoofPounce) return;
            Color color = pattern.Kind == AlienAttackKind.AcidSpit || pattern.Kind == AlienAttackKind.SlimePulse ?
                new Color(.4f, .9f, .32f) : boss ? new Color(.95f, .25f, .16f) : new Color(.9f, .65f, .2f);
            m_Material.SetColor("_BaseColor", color); m_Material.SetColor("_Color", color);
            Vector3 centre = state.AttackPoint;
            if (pattern.Kind == AlienAttackKind.SlimePulse || pattern.Kind == AlienAttackKind.TentacleSweep) centre = state.AttackOrigin;
            centre.y += .07f;
            if (pattern.Kind == AlienAttackKind.Charge || pattern.Kind == AlienAttackKind.Bite)
            {
                Vector3 end = (Vector3)state.AttackOrigin + (Vector3)state.AttackDirection * pattern.Speed * pattern.Active;
                var side = Vector3.Cross(Vector3.up, state.AttackDirection).normalized * pattern.Radius;
                m_Points[0] = (Vector3)state.AttackOrigin + side; m_Points[1] = end + side;
                m_Points[2] = end - side; m_Points[3] = (Vector3)state.AttackOrigin - side; m_Points[4] = m_Points[0];
                Draw(0, 5); return;
            }
            if (pattern.Kind == AlienAttackKind.TentacleSweep)
            {
                Vector3 facing = state.AttackDirection;
                int count = state.BossPhase > 1 ? 2 : 1;
                for (int side = 0; side < count; side++)
                {
                    m_Points[0] = centre;
                    for (int i = 0; i <= 32; i++) m_Points[i + 1] = centre + Quaternion.AngleAxis(-65 + i * 130f / 32 + side * 180, Vector3.up) * facing * pattern.Radius;
                    m_Points[34] = centre; Draw(side, 35);
                }
                return;
            }
            int patches = pattern.Kind == AlienAttackKind.RootSpikes ? 3 : 1;
            Vector3 right = Vector3.Cross(Vector3.up, state.AttackDirection).normalized;
            for (int patch = 0; patch < patches; patch++)
            {
                Vector3 point = centre + right * (patch == 0 ? 0 : patch == 1 ? pattern.Radius * 2.7f : -pattern.Radius * 2.7f);
                for (int i = 0; i < m_Points.Length; i++)
                {
                    float angle = i * Mathf.PI * 2 / (m_Points.Length - 1);
                    m_Points[i] = point + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * pattern.Radius;
                }
                Draw(patch, m_Points.Length);
                if (state.Action == AlienAction.Strike && (pattern.Kind == AlienAttackKind.RootSpikes ||
                    pattern.Kind == AlienAttackKind.AcidSpit || pattern.Kind == AlienAttackKind.SlimePulse || pattern.Kind == AlienAttackKind.TentacleSlam))
                    DrawImpact(patch, point, pattern, state.ActionTime);
            }
        }
        private void DrawImpact(int index, Vector3 point, AlienCombatPattern pattern, float age)
        {
            bool roots = pattern.Kind == AlienAttackKind.RootSpikes;
            if (!m_ImpactMaterial)
            {
                m_ImpactMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit")) { hideFlags = HideFlags.DontSave };
                m_ImpactMaterial.SetFloat("_Smoothness", roots ? .45f : .85f);
            }
            m_ImpactMaterial.SetColor("_BaseColor", roots ? new Color(.15f, .035f, .08f) :
                pattern.Kind == AlienAttackKind.TentacleSlam ? new Color(.22f, .045f, .06f) : new Color(.12f, .27f, .055f));
            if (!m_Impacts[index])
            {
                var go = new GameObject(roots ? "Erupting root spike" : "Acid impact pool"); go.transform.SetParent(m_Root.transform, false);
                var filter = go.AddComponent<MeshFilter>(); go.AddComponent<MeshRenderer>().sharedMaterial = m_ImpactMaterial;
                if (roots)
                {
                    if (!m_Spike) m_Spike = BuildSpike();
                    filter.sharedMesh = m_Spike;
                }
                else
                {
                    // Reuse Unity's sphere mesh as a shallow liquid patch, without a collider.
                    var sample = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    filter.sharedMesh = sample.GetComponent<MeshFilter>().sharedMesh; Destroy(sample);
                }
                m_Impacts[index] = go.transform;
            }
            var impact = m_Impacts[index]; impact.gameObject.SetActive(true); impact.position = point;
            if (roots)
            {
                float pulse = Mathf.Sin(Mathf.Clamp01((age % (pattern.Active / pattern.Pulses)) / .4f) * Mathf.PI);
                impact.localScale = new Vector3(pattern.Radius * .65f, .15f + pulse * 2, pattern.Radius * .65f);
            }
            else impact.localScale = new Vector3(pattern.Radius * 2, .04f, pattern.Radius * 2);
        }
        private static Mesh BuildSpike()
        {
            var vertices = new Vector3[24]; var triangles = new int[24];
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI / 4, b = (i + 1) * Mathf.PI / 4;
                vertices[i * 3] = new Vector3(Mathf.Cos(a) * .4f, 0, Mathf.Sin(a) * .4f);
                vertices[i * 3 + 1] = new Vector3(.15f, 1, .08f);
                vertices[i * 3 + 2] = new Vector3(Mathf.Cos(b) * .4f, 0, Mathf.Sin(b) * .4f);
                triangles[i * 3] = i * 3; triangles[i * 3 + 1] = i * 3 + 1; triangles[i * 3 + 2] = i * 3 + 2;
            }
            var mesh = new Mesh { name = "Alien root eruption", hideFlags = HideFlags.DontSave };
            mesh.vertices = vertices; mesh.triangles = triangles; mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }
        private void Draw(int index, int count)
        {
            var line = m_Lines[index]; line.enabled = true; line.positionCount = count;
            for (int i = 0; i < count; i++) line.SetPosition(i, m_Points[i]);
        }
        public void Dispose() { Destroy(m_Root); Destroy(m_Material); Destroy(m_ImpactMaterial); Destroy(m_Spike); }
        private static void Destroy(Object value) { if (!value) return; if (Application.isPlaying) Object.Destroy(value); else Object.DestroyImmediate(value); }
    }
}
