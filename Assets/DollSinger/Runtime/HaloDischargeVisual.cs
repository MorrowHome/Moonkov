using UnityEngine;
using UnityEngine.Rendering;

namespace Unity.MP_FPS.DollSinger
{
    /// <summary>A short, planar pressure release outside the sight window. One reusable mesh per halo.</summary>
    public sealed class HaloDischargeVisual : MonoBehaviour
    {
        private const int Sectors = 6, Segments = 5, VertexCount = Sectors * (Segments + 1) * 4;
        private readonly Vector3[] vertices = new Vector3[VertexCount];
        private readonly Color[] colors = new Color[VertexCount];
        private Mesh mesh;
        private MeshRenderer meshRenderer;
        private float age = 1, duration = .09f, radius = .24f, width = .005f;
        private Color color;
        private uint lastTick;
        public float Flash => age < duration ? Mathf.Pow(1 - age / duration, 2) : 0;
        public int VertexBudget => VertexCount;

        public void Configure(Material material)
        {
            if (mesh) return;
            mesh = new Mesh { name = "Halo discharge (runtime)", hideFlags = HideFlags.DontSave };
            mesh.MarkDynamic();
            var indices = new int[VertexCount / 4 * 6];
            for (int q = 0; q < VertexCount / 4; q++)
            {
                int v = q * 4, t = q * 6;
                indices[t] = v; indices[t + 1] = v + 1; indices[t + 2] = v + 2;
                indices[t + 3] = v; indices[t + 4] = v + 2; indices[t + 5] = v + 3;
            }
            mesh.vertices = vertices; mesh.colors = colors; mesh.triangles = indices;
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1.6f);
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            meshRenderer = gameObject.AddComponent<MeshRenderer>(); meshRenderer.sharedMaterial = material;
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off; meshRenderer.receiveShadows = false;
            meshRenderer.lightProbeUsage = LightProbeUsage.Off; meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            meshRenderer.enabled = false;
        }

        public bool Play(DollSingerHaloAim.HaloWeapon weapon, Color tint, uint tick = 0)
        {
            if (tick != 0 && lastTick != 0 && (int)(tick - lastTick) <= 0) return false;
            if (tick != 0) lastTick = tick;
            color = Color.Lerp(tint, Color.white, .55f); age = 0;
            radius = weapon == DollSingerHaloAim.HaloWeapon.Revolver ? .145f :
                weapon == DollSingerHaloAim.HaloWeapon.Sniper ? .29f : .25f;
            duration = weapon == DollSingerHaloAim.HaloWeapon.Rifle ? .075f :
                weapon == DollSingerHaloAim.HaloWeapon.Shotgun ? .12f : .095f;
            width = weapon == DollSingerHaloAim.HaloWeapon.Shotgun ? .009f : .005f;
            Layout(); return true;
        }

        public void Step(float deltaTime) { age += Mathf.Max(0, deltaTime); Layout(); }
        public void Clear() { age = duration; if (meshRenderer) meshRenderer.enabled = false; }
        private static Vector3 Polar(float r, float angle)
        { float a = angle * Mathf.Deg2Rad; return new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, -.004f); }
        private void Layout()
        {
            if (!meshRenderer) return;
            meshRenderer.enabled = Flash > .001f;
            if (!meshRenderer.enabled) return;
            float progress = Mathf.Clamp01(age / duration), r = radius + .055f * Mathf.Sqrt(progress);
            Color tint = color; tint.a = Flash * .7f;
            int v = 0;
            for (int sector = 0; sector < Sectors; sector++)
            {
                float start = sector * 60 + 10 + progress * 8;
                for (int segment = 0; segment < Segments; segment++)
                {
                    float a = start + segment * 5, b = a + 5;
                    vertices[v] = Polar(r, a); vertices[v + 1] = Polar(r + width, a);
                    vertices[v + 2] = Polar(r + width, b); vertices[v + 3] = Polar(r, b);
                    for (int i = 0; i < 4; i++) colors[v + i] = tint;
                    v += 4;
                }
                float angle = start + 12;
                vertices[v] = Polar(r + .018f, angle - .6f);
                vertices[v + 1] = Polar(r + .065f, angle - .12f);
                vertices[v + 2] = Polar(r + .065f, angle + .12f);
                vertices[v + 3] = Polar(r + .018f, angle + .6f);
                for (int i = 0; i < 4; i++) colors[v + i] = tint;
                v += 4;
            }
            mesh.vertices = vertices; mesh.colors = colors;
        }
        private void OnDisable() => Clear();
        private void OnDestroy()
        { if (mesh) { if (Application.isPlaying) Destroy(mesh); else DestroyImmediate(mesh); } }
    }
}
