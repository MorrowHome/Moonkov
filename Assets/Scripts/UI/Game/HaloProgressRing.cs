// 把正式光环贴图按进度画成读条。它读取 HaloProgressArtwork，按当前填充比例裁剪几何并保留对应 UV，供局内不同动作复用。
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MP_FPS.Client {
    // 复用 RifleHaloVisual 的四张贴图和静止布局。转子保持静止。
    public sealed class HaloProgressRing : VisualElement {
        private struct Point {
            public Vector2 Position, UV;

            public Point(Vector2 position, Vector2 uv) {
                Position = position;
                UV = uv;
            }
        }

        private sealed class Group {
            public Texture Texture;
            public readonly List<Point[]> Pieces = new();
        }

        private readonly Group[] m_Groups = new Group[4];
        private readonly Point[] m_ClipA = new Point[12], m_ClipB = new Point[12];
        private readonly List<Vertex> m_Vertices = new(2048);
        private readonly List<ushort> m_Indices = new(4096);
        private readonly Color m_Color;
        private float m_Radius, m_Progress;
        public float Progress => m_Progress;

        public HaloProgressRing() {
            name = "haloProgressRing";
            pickingMode = PickingMode.Ignore;
            var art = Resources.Load<HaloProgressArtwork>("Moonkov/UI/HaloProgressArtwork");
            if (!art || !art.Petal || !art.OuterArc || !art.InnerArc || !art.Cell)
                throw new System.InvalidOperationException("Missing formal halo progress artwork");
            m_Color = art.Color;
            Build(0, art.Petal, 6, .171f, 1);
            Build(1, art.OuterArc, 6, .135f, 1);
            Build(2, art.InnerArc, 3, .09f, 1);
            Build(3, art.Cell, 30, .114f, .9f);
            generateVisualContent += Draw;
        }

        public void SetProgress(float value) {
            // 进度输入必须是 0 到 1。数据尚未同步导致无效数值时先画空环，避免生成坏顶点。
            value = float.IsFinite(value) ? Mathf.Clamp01(value) : 0;
            if (m_Progress == value) return;
            m_Progress = value;
            MarkDirtyRepaint();
        }

        private void Build(int index, Sprite sprite, int count, float radius, float scale) {
            var group = new Group { Texture = sprite.texture };
            m_Groups[index] = group;
            var rect = sprite.textureRect;
            // 紧密打包的 Sprite 会裁掉透明边。画回去时保留原轴心和像素比例。
            // 图集会裁掉透明边。用裁切偏移和原始锚点恢复图案应在的位置，圆环才不会挤到一处。
            var low = (sprite.textureRectOffset - sprite.pivot) / sprite.pixelsPerUnit;
            var high = low + rect.size / sprite.pixelsPerUnit;
            var corners = new[] {
                new Vector2(low.x, low.y), new Vector2(high.x, low.y),
                new Vector2(high.x, high.y), new Vector2(low.x, high.y)
            };
            var uv = new[] {
                new Vector2(rect.xMin, rect.yMin), new Vector2(rect.xMax, rect.yMin),
                new Vector2(rect.xMax, rect.yMax), new Vector2(rect.xMin, rect.yMax)
            };
            for (int i = 0; i < count; i++) {
                float angle = i * Mathf.PI * 2 / count;
                float c = Mathf.Cos(angle), s = Mathf.Sin(angle);
                var piece = new Point[4];
                for (int j = 0; j < 4; j++) {
                    var p = corners[j] * scale + new Vector2(radius, 0);
                    var position = new Vector2(c * p.x - s * p.y, -s * p.x - c * p.y);
                    piece[j] = new Point(position,
                        new Vector2(uv[j].x / sprite.texture.width, uv[j].y / sprite.texture.height));
                    m_Radius = Mathf.Max(m_Radius, position.magnitude);
                }
                group.Pieces.Add(piece);
            }
        }

        private static float Cross(Vector2 a, Vector2 b) { return a.x * b.y - a.y * b.x; }

        private static int Clip(Point[] input, int count, Point[] output, Vector2 boundary, bool positive) {
            if (count == 0) return 0;
            int written = 0;
            var previous = input[count - 1];
            float previousSide = Cross(boundary, previous.Position) * (positive ? 1 : -1);
            for (int i = 0; i < count; i++) {
                var current = input[i];
                float side = Cross(boundary, current.Position) * (positive ? 1 : -1);
                // 轮廓边跨过要填充的边界时补一个交点。贴图坐标按同样比例插值，截取的图案不会拉花。
                if (side >= 0 != previousSide >= 0) {
                    float t = previousSide / (previousSide - side);
                    output[written++] = new Point(Vector2.LerpUnclamped(previous.Position, current.Position, t),
                        Vector2.LerpUnclamped(previous.UV, current.UV, t));
                }
                if (side >= 0) output[written++] = current;
                previous = current;
                previousSide = side;
            }
            return written;
        }

        private void Append(Point[] points, int count, Color color, Vector2 center, float scale) {
            if (count < 3) return;
            ushort start = (ushort)m_Vertices.Count;
            for (int i = 0; i < count; i++) {
                var p = center + points[i].Position * scale;
                m_Vertices.Add(new Vertex
                    { position = new Vector3(p.x, p.y, Vertex.nearZ), tint = (Color32)color, uv = points[i].UV });
            }
            // 源图的四边形在 Unity 纵轴向上的坐标中为顺时针，UI 纵轴则朝下。
            // 将裁出的多边形绕第一个顶点拆成多个三角形。UI 的纵轴方向和 sprite 坐标相反，索引顺序也随之反过来。
            for (int i = 1; i < count - 1; i++) {
                m_Indices.Add(start);
                m_Indices.Add((ushort)(start + i + 1));
                m_Indices.Add((ushort)(start + i));
            }
        }

        private void Draw(MeshGenerationContext context) {
            float diameter = Mathf.Min(contentRect.width, contentRect.height);
            if (diameter <= 0 || m_Radius <= 0) return;
            float scale = diameter * .5f / m_Radius;
            var center = contentRect.center;
            foreach (var group in m_Groups) {
                m_Vertices.Clear();
                m_Indices.Clear();
                foreach (var piece in group.Pieces) {
                    // 先画完整灰底，彩色部分再叠到已完成的角度。这样低进度时仍能看清整圈形状。
                    Append(piece, 4, new Color(.23f, .25f, .28f, .85f), center, scale);
                    if (m_Progress >= 1) {
                        Append(piece, 4, m_Color, center, scale);
                        continue;
                    }
                    float end = m_Progress * 360;
                    // 按固定的四分之一圆区域裁剪。这样超过半圈时仍能正确填色。
                    // 每九十度分一道边界进行裁剪。大于半圈的进度也能用多个相交区域正确填上。
                    for (float from = 0; from < end; from += 90) {
                        float a = (from - 90) * Mathf.Deg2Rad, b = (Mathf.Min(end, from + 90) - 90) * Mathf.Deg2Rad;
                        int count = Clip(piece, 4, m_ClipA, new Vector2(Mathf.Cos(a), Mathf.Sin(a)), true);
                        count = Clip(m_ClipA, count, m_ClipB, new Vector2(Mathf.Cos(b), Mathf.Sin(b)), false);
                        Append(m_ClipB, count, m_Color, center, scale);
                    }
                }
                if (m_Indices.Count == 0) continue;
                var mesh = context.Allocate(m_Vertices.Count, m_Indices.Count, group.Texture);
                foreach (var vertex in m_Vertices) mesh.SetNextVertex(vertex);
                foreach (ushort index in m_Indices) mesh.SetNextIndex(index);
            }
        }
    }
}