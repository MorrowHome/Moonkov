using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MP_FPS.Client
{
    // Distinct small vector silhouettes share the existing equipment palette.
    public sealed class TacticalItemIcon : VisualElement
    {
        private readonly string m_Code;
        public TacticalItemIcon(string code)
        { m_Code = code; pickingMode = PickingMode.Ignore; generateVisualContent += Draw; }
        private void Draw(MeshGenerationContext context)
        {
            float w = contentRect.width, h = contentRect.height; if (w < 2 || h < 2) return;
            var p = context.painter2D; float s = Mathf.Min(w, h); Vector2 c = new Vector2(w, h) * .5f;
            bool needle = m_Code.EndsWith("_shot", System.StringComparison.Ordinal);
            bool trap = m_Code.Contains("trip_mine"); bool box = m_Code == "ammo_box";
            Color color = m_Code.Contains("fire") ? new Color(.9f, .47f, .24f) : m_Code.Contains("poison") || m_Code.Contains("antidote") ?
                new Color(.63f, .78f, .42f) : m_Code.Contains("choke") ? new Color(.6f, .77f, .8f) :
                m_Code.Contains("vitality") ? new Color(.91f, .52f, .49f) : new Color(.81f, .73f, .51f);
            p.lineWidth = 2; p.strokeColor = color; p.fillColor = new Color(color.r * .35f, color.g * .35f, color.b * .35f, 1);
            float x = needle ? .13f : box ? .36f : trap ? .33f : .23f, y = needle ? .26f : .29f;
            p.BeginPath(); p.MoveTo(c + new Vector2(-x, -y) * s); p.LineTo(c + new Vector2(x, -y) * s);
            p.LineTo(c + new Vector2(x, y) * s); p.LineTo(c + new Vector2(-x, y) * s); p.ClosePath(); p.Fill(); p.Stroke();
            p.BeginPath(); p.MoveTo(c + Vector2.up * s * (needle ? -.44f : -.4f)); p.LineTo(c + Vector2.up * s * -.26f); p.Stroke();
            if (needle) { p.BeginPath(); p.MoveTo(c + new Vector2(-.22f, .3f) * s); p.LineTo(c + new Vector2(.22f, .3f) * s); p.Stroke(); }
            if (trap) { p.BeginPath(); p.MoveTo(c + new Vector2(-.46f, .36f) * s); p.LineTo(c + new Vector2(.46f, -.36f) * s); p.Stroke(); }
            if (box) { p.BeginPath(); p.MoveTo(c + Vector2.left * s * .2f); p.LineTo(c + Vector2.right * s * .2f); p.Stroke(); }
            if (!needle && !trap && !box) { p.BeginPath(); p.Arc(c, s * .09f, Angle.Degrees(0), Angle.Degrees(360)); p.Stroke(); }
        }
    }
}
