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
        private readonly Transform[] m_Impacts = new Transform[3];
        private Mesh m_Spike, m_Residue;
        private Material m_ImpactMaterial;
        public AlienAttackPresentation(Transform owner)
        {
            m_Root = new GameObject("Alien attack effects"); m_Root.transform.SetParent(owner, false);
        }
        public void Observe(ProceduralAlienEnemy.State state, AlienCombatPattern pattern)
        {
            foreach (var impact in m_Impacts) if (impact) impact.gameObject.SetActive(false);
            // Windup is communicated by the creature's pose and sound. Never draw
            // circles, sectors or charge lanes over the world to expose hit geometry.
            if (state.Action != AlienAction.Strike || state.AttackSequence == 0) return;
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
                pattern.Kind == AlienAttackKind.TentacleSlam ? new Color(.16f, .12f, .09f) : new Color(.12f, .27f, .055f));
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
        public void Dispose() { Destroy(m_Root); Destroy(m_ImpactMaterial); Destroy(m_Spike); Destroy(m_Residue); }
        private static void Destroy(Object value) { if (!value) return; if (Application.isPlaying) Object.Destroy(value); else Object.DestroyImmediate(value); }
    }
}
