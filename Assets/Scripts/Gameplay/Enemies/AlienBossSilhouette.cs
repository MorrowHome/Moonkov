// AlienCreatureBody 给部分 Boss 加上的轮廓表现。程序画出冠部和甲壳，从攻击蓄力、威吓和死亡状态更新发光或收缩。
using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Unity.MP_FPS {
    /// <summary>每个 Boss 有一个显示用网格，比如带锯齿的甲片或有骨脊的根冠。
    /// 它复用现有甲壳材质，无需碰撞、额外光源和 GPU 求解器。</summary>
    public sealed class AlienBossSilhouette : IDisposable {
        private readonly GameObject m_Root;
        private readonly Mesh m_Mesh;
        private readonly Renderer m_Renderer;
        private readonly MaterialPropertyBlock m_Properties = new();

        public AlienBossSilhouette(Transform owner, AlienCreatureProfile profile, float scale) {
            m_Root = new GameObject("Boss serrated carapace");
            m_Root.layer = 2;
            m_Root.transform.SetParent(owner, false);
            m_Mesh = new Mesh { name = "Boss crown and hanging mantle plates" };
            // 按周向造甲片和下垂轮廓，把 Boss 和普通异形从远处区分开。
            var vertices = new List<Vector3>();
            var indices = new List<int>();
            var r = profile.Branch.bodyRadii * scale;
            bool roots = profile.Species == AlienSpecies.Roots;
            for (int i = 0; i < 12; i++) {
                float angle = i * Mathf.PI / 6;
                var radial = new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle));
                var side = Vector3.Cross(Vector3.up, radial);
                var centre = Vector3.Scale(radial, r) * .82f + Vector3.up * r.y * .1f;
                var tip = centre + radial * (roots ? .22f : .42f) * scale - Vector3.up * r.y * .85f;
                Wedge(vertices, indices, centre, side * scale * .11f, Vector3.up * r.y * .7f, tip);
                var crown = Vector3.Scale(radial, r) * .35f + Vector3.up * r.y * .83f;
                tip = crown + Vector3.up * (r.y * .17f + scale * (.18f + i % 3 * .04f)) + radial * scale * .13f;
                Wedge(vertices, indices, crown, side * scale * .085f, radial * scale * .15f, tip);
            }
            m_Mesh.SetVertices(vertices);
            m_Mesh.SetTriangles(indices, 0);
            m_Mesh.RecalculateNormals();
            m_Mesh.RecalculateBounds();
            m_Root.AddComponent<MeshFilter>().sharedMesh = m_Mesh;
            m_Renderer = m_Root.AddComponent<MeshRenderer>();
            var style = Resources.Load<AlienVoidStyle>("Moonkov/AlienVoid");
            m_Renderer.sharedMaterial = style ? style.Plate : null;
        }

        // 每片甲壳用独立的三角面。内外两侧分开记录顶点，法线不会在转角被平滑成同一个方向。
        private static void Wedge(List<Vector3> v, List<int> t, Vector3 c, Vector3 width, Vector3 length, Vector3 tip) {
            Vector3[] basePoints = {
                c - width - length * .5f, c + width - length * .5f,
                c + width + length * .5f, c - width + length * .5f
            };
            for (int side = 0; side < 4; side++) {
                int start = v.Count;
                v.Add(basePoints[side]);
                v.Add(basePoints[(side + 1) % 4]);
                v.Add(tip);
                t.Add(start);
                t.Add(start + 1);
                t.Add(start + 2);
                // 内面使用独立的顶点，保留与外面朝向相反的法线。
                start = v.Count;
                v.Add(tip);
                v.Add(basePoints[(side + 1) % 4]);
                v.Add(basePoints[side]);
                t.Add(start);
                t.Add(start + 1);
                t.Add(start + 2);
            }
        }

        public void Observe(Vector3 centre, Quaternion rotation, AlienAction action, float age) {
            m_Root.transform.SetPositionAndRotation(centre, rotation);
            // 蓄力和威吓阶段用较亮的发光参数，死亡时随时间收到较小尺寸。
            float charge = action == AlienAction.Windup || action == AlienAction.Dominance ? .45f : .05f;
            m_Properties.SetFloat("_VoidCharge", charge);
            m_Renderer.SetPropertyBlock(m_Properties);
            if (action == AlienAction.Dead)
                m_Root.transform.localScale = Vector3.one * Mathf.Lerp(1, .8f, Mathf.Clamp01(age));
        }

        public void Dispose() {
            if (Application.isPlaying) {
                Object.Destroy(m_Root);
                Object.Destroy(m_Mesh);
            } else {
                Object.DestroyImmediate(m_Root);
                Object.DestroyImmediate(m_Mesh);
            }
        }
    }
}