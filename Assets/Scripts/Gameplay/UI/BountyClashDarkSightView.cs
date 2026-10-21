using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MP_FPS
{
    /// <summary>Soft areas from short-lived, server-authorized contacts. No hitbox or ghost lookup.</summary>
    public sealed class BountyClashDarkSightView : VisualElement
    {
        private sealed class Cloud : VisualElement
        {
            public Cloud(int index)
            {
                pickingMode = PickingMode.Ignore;
                style.position = Position.Absolute;
                generateVisualContent += context =>
                {
                    var painter = context.painter2D;
                    Vector2 center = contentRect.center;
                    float radius = Mathf.Min(contentRect.width, contentRect.height) * .5f;
                    // Overlapping low-alpha disks soften the whole uncertain region; never a crosshair.
                    for (int layer = 0; layer < 10; layer++)
                    {
                        float fraction = 1f - layer * .075f;
                        painter.fillColor = new Color(1f, .38f + layer * .012f, .045f, .018f + layer * .0035f);
                        for (int lobe = 0; lobe < 3; lobe++)
                        {
                            float angle = (index * 73 + lobe * 120) * Mathf.Deg2Rad;
                            Vector2 offset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius * .16f;
                            painter.BeginPath(); painter.Arc(center + offset, radius * fraction * .85f, Angle.Degrees(0), Angle.Degrees(360)); painter.Fill();
                        }
                    }
                };
            }
        }
        private readonly Cloud[] m_Clouds = new Cloud[BountyClashRules.MaxPlayers - 1];
        private bool m_Presence;
        public BountyClashDarkSightView()
        {
            pickingMode = PickingMode.Ignore;
            style.position = Position.Absolute; style.left = style.right = style.top = style.bottom = 0;
            style.backgroundColor = new Color(.025f, .04f, .055f, .52f);
            for (int i = 0; i < m_Clouds.Length; i++) { m_Clouds[i] = new Cloud(i); Add(m_Clouds[i]); }
            generateVisualContent += context =>
            {
                if (!m_Presence) return;
                var p = context.painter2D; float w = contentRect.width, h = contentRect.height;
                for (int i = 0; i < 12; i++)
                {
                    float inset = i * 3f; p.strokeColor = new Color(1, .45f, .08f, .15f * (1 - i / 12f)); p.lineWidth = 4;
                    p.BeginPath(); p.MoveTo(new Vector2(inset, inset)); p.LineTo(new Vector2(w - inset, inset));
                    p.LineTo(new Vector2(w - inset, h - inset)); p.LineTo(new Vector2(inset, h - inset)); p.ClosePath(); p.Stroke();
                }
            };
        }
        public void Present(BountyClashClientState data, Camera camera, bool view, bool boost, double now)
        {
            style.display = view ? DisplayStyle.Flex : DisplayStyle.None;
            bool presence = view && data.Snapshot.IsCarrier && data.Sense.Presence && now - data.SenseReceivedAt < .7;
            if (presence != m_Presence) { m_Presence = presence; MarkDirtyRepaint(); }
            float width = contentRect.width, height = contentRect.height;
            for (int i = 0; i < m_Clouds.Length; i++)
            {
                var cloud = m_Clouds[i];
                bool visible = view && boost && camera && data.ContactVisible(i, now) && width > 0 && height > 0;
                if (!visible) { cloud.style.display = DisplayStyle.None; continue; }
                var contact = data.Contacts[i]; Vector3 world = contact.Position;
                var point = camera.WorldToViewportPoint(world);
                // Behind-camera contacts have no visible cloud until the player turns toward them.
                if (point.z <= .05f) { cloud.style.display = DisplayStyle.None; continue; }
                var edge = camera.WorldToViewportPoint(world + camera.transform.right * contact.UncertaintyRadius);
                float size = Mathf.Clamp(Mathf.Abs(edge.x - point.x) * width * 2.4f, 52, 220);
                cloud.style.display = DisplayStyle.Flex;
                cloud.style.width = cloud.style.height = size;
                cloud.style.left = point.x * width - size * .5f;
                cloud.style.top = (1 - point.y) * height - size * .5f;
            }
        }
    }
}
