// 战术道具的快捷栏图标。把道具类型对应的形状和颜色画出来，数量和能否使用由快捷栏另外更新。
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MP_FPS.Client {
    // 不同小图标用不同轮廓，颜色沿用装备界面的配色。
    public sealed class TacticalItemIcon : VisualElement {
        private readonly string m_Code;

        // 从图标需要的物品代码确定外框和颜色。图标是快捷栏的视觉部分，不决定操作是否可用。
        public TacticalItemIcon(string code) {
            m_Code = code;
            pickingMode = PickingMode.Ignore;
            var texture = PhysicalItemVisuals.Icon(code);
            if (texture) {
                var image = new Image
                    { image = texture, scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
                image.style.position = Position.Absolute;
                image.style.left = image.style.right = image.style.top = image.style.bottom = 0;
                Add(image);
            } else { generateVisualContent += Draw; }
        }

        private void Draw(MeshGenerationContext context) {
            // 先按布局宽高计算绘制比例。不够画时提前结束，等面板正常布局后再重绘。
            float w = contentRect.width, h = contentRect.height;
            if (w < 2 || h < 2) return;
            var p = context.painter2D;
            float s = Mathf.Min(w, h);
            var c = new Vector2(w, h) * .5f;
            bool needle = m_Code.EndsWith("_shot", System.StringComparison.Ordinal);
            bool trap = m_Code.Contains("trip_mine");
            bool box = m_Code == "ammo_box";
            var color = m_Code.Contains("fire") ? new Color(.9f, .47f, .24f) :
                m_Code.Contains("poison") || m_Code.Contains("antidote") ? new Color(.63f, .78f, .42f) :
                m_Code.Contains("choke") ? new Color(.6f, .77f, .8f) :
                m_Code.Contains("vitality") ? new Color(.91f, .52f, .49f) : new Color(.81f, .73f, .51f);
            p.lineWidth = 2;
            p.strokeColor = color;
            p.fillColor = new Color(color.r * .35f, color.g * .35f, color.b * .35f, 1);
            float x = needle ? .13f : box ? .36f : trap ? .33f : .23f, y = needle ? .26f : .29f;
            p.BeginPath();
            p.MoveTo(c + new Vector2(-x, -y) * s);
            p.LineTo(c + new Vector2(x, -y) * s);
            p.LineTo(c + new Vector2(x, y) * s);
            p.LineTo(c + new Vector2(-x, y) * s);
            p.ClosePath();
            p.Fill();
            p.Stroke();
            p.BeginPath();
            p.MoveTo(c + Vector2.up * s * (needle ? -.44f : -.4f));
            p.LineTo(c + Vector2.up * s * -.26f);
            p.Stroke();
            // 用中间的短线、斜线或圆环进一步区分针剂、陷阱和其他道具。显示时可沿用同样尺寸。
            if (needle) {
                p.BeginPath();
                p.MoveTo(c + new Vector2(-.22f, .3f) * s);
                p.LineTo(c + new Vector2(.22f, .3f) * s);
                p.Stroke();
            }
            if (trap) {
                p.BeginPath();
                p.MoveTo(c + new Vector2(-.46f, .36f) * s);
                p.LineTo(c + new Vector2(.46f, -.36f) * s);
                p.Stroke();
            }
            if (box) {
                p.BeginPath();
                p.MoveTo(c + Vector2.left * s * .2f);
                p.LineTo(c + Vector2.right * s * .2f);
                p.Stroke();
            }
            if (!needle && !trap && !box) {
                p.BeginPath();
                p.Arc(c, s * .09f, Angle.Degrees(0), Angle.Degrees(360));
                p.Stroke();
            }
        }
    }
}