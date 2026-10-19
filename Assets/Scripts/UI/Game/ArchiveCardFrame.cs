using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MP_FPS.Client
{
    // Static vector edges. Native Buttons retain their rectangular hit area and input behavior.
    [UxmlElement]
    public partial class ArchiveCardFrame : VisualElement
    {
        [UxmlAttribute] public bool Orbital { get; set; }
        private Button m_Button;
        private bool m_Hover, m_Focus, m_Press;

        public ArchiveCardFrame()
        {
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
            RegisterCallback<AttachToPanelEvent>(Attach);
            RegisterCallback<DetachFromPanelEvent>(Detach);
        }
        private void Attach(AttachToPanelEvent evt)
        {
            m_Button = parent as Button;
            if (m_Button == null) return;
            m_Button.RegisterCallback<PointerEnterEvent>(Enter);
            m_Button.RegisterCallback<PointerLeaveEvent>(Leave);
            m_Button.RegisterCallback<FocusInEvent>(FocusIn);
            m_Button.RegisterCallback<FocusOutEvent>(FocusOut);
            m_Button.RegisterCallback<PointerDownEvent>(Press);
            m_Button.RegisterCallback<PointerUpEvent>(Release);
            m_Button.RegisterCallback<PointerCaptureOutEvent>(CaptureLost);
        }
        private void Detach(DetachFromPanelEvent evt)
        {
            if (m_Button == null) return;
            m_Button.UnregisterCallback<PointerEnterEvent>(Enter);
            m_Button.UnregisterCallback<PointerLeaveEvent>(Leave);
            m_Button.UnregisterCallback<FocusInEvent>(FocusIn);
            m_Button.UnregisterCallback<FocusOutEvent>(FocusOut);
            m_Button.UnregisterCallback<PointerDownEvent>(Press);
            m_Button.UnregisterCallback<PointerUpEvent>(Release);
            m_Button.UnregisterCallback<PointerCaptureOutEvent>(CaptureLost);
            m_Button = null; m_Hover = m_Focus = m_Press = false;
        }
        private void Enter(PointerEnterEvent evt) { m_Hover = true; MarkDirtyRepaint(); }
        private void Leave(PointerLeaveEvent evt) { m_Hover = m_Press = false; MarkDirtyRepaint(); }
        private void FocusIn(FocusInEvent evt) { m_Focus = true; MarkDirtyRepaint(); }
        private void FocusOut(FocusOutEvent evt) { m_Focus = m_Press = false; MarkDirtyRepaint(); }
        private void Press(PointerDownEvent evt) { if (evt.button == 0) { m_Press = true; MarkDirtyRepaint(); } }
        private void Release(PointerUpEvent evt) { m_Press = false; MarkDirtyRepaint(); }
        private void CaptureLost(PointerCaptureOutEvent evt) { m_Press = false; MarkDirtyRepaint(); }
        private void Draw(MeshGenerationContext ctx)
        {
            float w = contentRect.width - 1, h = contentRect.height - 1;
            if (w < 2 || h < 2) return;
            bool selected = parent != null && (parent.ClassListContains("terminal-tab-selected") || parent.ClassListContains("terminal-node-selected"));
            bool lit = selected || m_Hover || m_Focus;
            var p = ctx.painter2D;
            Color blue = new Color(.086f, .294f, 1f);
            if (Orbital)
            {
                if (!lit) return;
                p.strokeColor = selected ? blue : Color.white; p.lineWidth = 2;
                float n = Mathf.Min(w, h) * .12f;
                p.BeginPath(); p.MoveTo(new Vector2(1, n)); p.LineTo(new Vector2(1, 1)); p.LineTo(new Vector2(n, 1)); p.Stroke();
                p.BeginPath(); p.MoveTo(new Vector2(w - n, h)); p.LineTo(new Vector2(w, h)); p.LineTo(new Vector2(w, h - n)); p.Stroke();
                return;
            }
            float cut = Mathf.Min(14, h * .2f);
            p.BeginPath(); p.MoveTo(new Vector2(1, 1)); p.LineTo(new Vector2(w - cut, 1));
            p.LineTo(new Vector2(w, cut)); p.LineTo(new Vector2(w, h)); p.LineTo(new Vector2(cut, h)); p.LineTo(new Vector2(1, h - cut)); p.ClosePath();
            p.fillColor = m_Press ? blue : selected ? new Color(.045f, .1f, .24f) : new Color(.028f, .033f, .045f, .96f); p.Fill();
            p.lineWidth = lit ? 2 : 1; p.strokeColor = selected ? blue : m_Hover || m_Focus ? Color.white : new Color(.46f, .49f, .54f); p.Stroke();
            p.strokeColor = lit ? blue : new Color(.75f, .78f, .82f); p.lineWidth = lit ? 3 : 1;
            p.BeginPath(); p.MoveTo(new Vector2(10, 5)); p.LineTo(new Vector2(lit ? 54 : 30, 5)); p.Stroke();
            if (lit)
            {
                p.fillColor = blue; p.BeginPath(); p.MoveTo(new Vector2(w - 20, h)); p.LineTo(new Vector2(w, h - 20)); p.LineTo(new Vector2(w, h)); p.ClosePath(); p.Fill();
            }
        }
    }

    [UxmlElement]
    public partial class ArchiveOrbitGuides : VisualElement
    {
        public ArchiveOrbitGuides() { pickingMode = PickingMode.Ignore; generateVisualContent += Draw; }
        private void Draw(MeshGenerationContext ctx)
        {
            var rect = contentRect;
            if (rect.width < 1 || rect.height < 1) return;
            var center = new Vector2(rect.width * .49f, rect.height * .49f);
            float radius = Mathf.Min(rect.width * .37f, rect.height * .46f);
            var p = ctx.painter2D; p.lineWidth = 1; p.strokeColor = new Color(.8f, .84f, .9f, .38f);
            foreach (float scale in new[] { .64f, .85f, 1f })
            {
                p.BeginPath();
                for (int i = 0; i <= 120; i++)
                {
                    float angle = i * Mathf.PI * 2 / 120;
                    var point = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius * scale;
                    if (i == 0) p.MoveTo(point); else p.LineTo(point);
                }
                p.Stroke();
            }
            for (int i = 0; i < 12; i++)
            {
                float angle = i * Mathf.PI / 6;
                var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                p.BeginPath(); p.MoveTo(center + direction * radius * .96f); p.LineTo(center + direction * radius * 1.035f); p.Stroke();
            }
        }
    }
}
