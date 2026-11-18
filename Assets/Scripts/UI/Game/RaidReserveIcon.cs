// 监测仪里的分格资源图标和武器图案。调用方给出点亮状态和颜色，图标只更新视觉表现。
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MP_FPS.Client {
    // 小号矢量图标在缺少 Emoji 字体的系统上也能正常显示。
    public sealed class RaidReserveIcon : VisualElement {
        private readonly int m_Kind;
        private readonly Image m_Image;

        private static readonly string[] HaloNames = {
            "Rifle", "Revolver", "Shotgun", "Sniper", "Cicada", "Trine", "Aegis", "Vesper", "Thorn", "Sundown", "Rondo",
            "Tempest", "Nocturne"
        };

        private static readonly Texture2D[] HaloTextures = new Texture2D[13];
        public bool Filled { get; private set; }
        public Color Tint { get; private set; }
        public Texture2D Artwork => m_Image?.image as Texture2D;

        public RaidReserveIcon(int kind) {
            m_Kind = kind;
            pickingMode = PickingMode.Ignore;
            AddToClassList("vitals-cell");
            // 光环一栏使用实际武器贴图。其他生命资源按自己的图形模板绘制，每一格表示当前值下应不应该点亮。
            if (kind == 4) {
                m_Image = new Image { scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
                m_Image.AddToClassList("vitals-halo-art");
                Add(m_Image);
            } else { generateVisualContent += Draw; }
        }

        // 按武器编号从资源取对应图标。第一次加载后缓存，武器能量变化不需要再读图片。
        public static Texture2D HaloArtwork(uint weapon) {
            int index = Mathf.Clamp((int)weapon - (int)DollSingerWeapons.Halo, 0, HaloNames.Length - 1);
            return HaloTextures[index]
                ? HaloTextures[index]
                : HaloTextures[index] = Resources.Load<Texture2D>("HaloIcons/" + HaloNames[index]);
        }

        public void SetArtwork(Texture2D artwork) {
            if (m_Image != null) m_Image.image = artwork;
        }

        public void Present(bool filled, Color tint, bool ready) {
            Filled = filled;
            Tint = tint;
            // 武器轮廓用透明度区分点亮、空置与尚未准备。程序画出的其他图形使用同一套状态。
            if (m_Image != null)
                m_Image.tintColor = filled ? Color.white : new Color(.65f, .65f, .65f, ready ? .3f : .15f);
            else
                MarkDirtyRepaint();
        }

        private void Draw(MeshGenerationContext context) {
            float size = Mathf.Min(contentRect.width, contentRect.height);
            if (size < 1) return;
            float scale = size / 22;
            var origin = new Vector2((contentRect.width - size) / 2 + scale, (contentRect.height - size) / 2 + scale);
            Vector2 V(float x, float y) { return origin + new Vector2(x, y) * scale; }
            var p = context.painter2D;
            p.fillColor = Filled ? Tint : new Color(Tint.r, Tint.g, Tint.b, .22f);
            p.BeginPath();

            void Circle(float x, float y, float r) {
                float c = r * .552285f;
                p.MoveTo(V(x + r, y));
                p.BezierCurveTo(V(x + r, y + c), V(x + c, y + r), V(x, y + r));
                p.BezierCurveTo(V(x - c, y + r), V(x - r, y + c), V(x - r, y));
                p.BezierCurveTo(V(x - r, y - c), V(x - c, y - r), V(x, y - r));
                p.BezierCurveTo(V(x + c, y - r), V(x + r, y - c), V(x + r, y));
                p.ClosePath();
            }

            switch (m_Kind) {
                case 0:
                    Circle(7, 12, 5);
                    Circle(15, 5, 3);
                    break;
                case 1:
                    p.MoveTo(V(10, 1));
                    p.BezierCurveTo(V(12, 7), V(18, 8), V(17, 13));
                    p.BezierCurveTo(V(16, 21), V(3, 21), V(3, 12));
                    p.BezierCurveTo(V(3, 8), V(7, 5), V(7, 5));
                    p.BezierCurveTo(V(6, 11), V(11, 9), V(10, 1));
                    p.ClosePath();
                    break;
                case 2:
                    p.MoveTo(V(10, 1));
                    p.BezierCurveTo(V(8, 5), V(3, 9), V(3, 13));
                    p.BezierCurveTo(V(3, 21), V(17, 21), V(17, 13));
                    p.BezierCurveTo(V(17, 9), V(12, 5), V(10, 1));
                    p.ClosePath();
                    break;
                case 3:
                    p.MoveTo(V(7, 9));
                    p.BezierCurveTo(V(6, 5), V(9, 1), V(13, 2));
                    p.BezierCurveTo(V(20, 2), V(20, 7), V(17, 11));
                    p.BezierCurveTo(V(15, 14), V(12, 14), V(10, 12));
                    p.LineTo(V(6, 16));
                    p.BezierCurveTo(V(7, 20), V(3, 20), V(3, 17));
                    p.BezierCurveTo(V(0, 17), V(0, 13), V(3, 14));
                    p.LineTo(V(7, 10));
                    p.ClosePath();
                    break;
                default:
                    p.MoveTo(V(11, 1));
                    p.LineTo(V(4, 11));
                    p.LineTo(V(9, 11));
                    p.LineTo(V(7, 19));
                    p.LineTo(V(17, 8));
                    p.LineTo(V(12, 8));
                    p.LineTo(V(14, 1));
                    p.ClosePath();
                    break;
            }
            p.Fill();
        }
    }
}