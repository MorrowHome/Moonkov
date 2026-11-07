using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MP_FPS.Client
{
    public sealed partial class HalosMainRoom
    {
        private Vector2 m_PointerTarget, m_Parallax, m_ParallaxVelocity;

        private void TrackParallax(PointerMoveEvent evt)
        {
            if (IsPreviewOpen || evt.pointerType != UnityEngine.UIElements.PointerType.mouse) return;
            Vector2 point = m_Viewport.WorldToLocal(evt.position);
            if (!m_Viewport.contentRect.Contains(point)) { m_PointerTarget = Vector2.zero; return; }
            m_PointerTarget = new Vector2(Mathf.Clamp(point.x / 960 - 1, -1, 1), Mathf.Clamp(point.y / 540 - 1, -1, 1));
        }

        private void LeaveParallax(PointerLeaveEvent evt)
        {
            if (evt.target == Root) m_PointerTarget = Vector2.zero;
        }

        private void ResetParallax()
        {
            m_PointerTarget = m_Parallax = m_ParallaxVelocity = Vector2.zero;
        }

        private void AdvanceParallax(float dt, bool animate)
        {
            if (!animate) { ResetParallax(); return; }
            var target = IsMoving || IsOpening || m_TabletState > 0 ? Vector2.zero : m_PointerTarget;
            m_Parallax = Vector2.SmoothDamp(m_Parallax, target, ref m_ParallaxVelocity, .24f, Mathf.Infinity, dt);
        }

        private void PaintParallax()
        {
            bool animate = Animate;
            var offset = animate ? m_Parallax : Vector2.zero;
            // Overscan covers the outer movement, including diagonal pointer travel.
            float scale = animate ? 1.04f : 1;
            m_ParallaxLayer.style.scale = new Scale(new Vector3(scale, scale, 1));
            m_ParallaxLayer.style.translate = new Translate(offset.x * 20, offset.y * 12);
            // The window masks the more distant Moon. The foreground actor travels farther.
            m_Moon.style.translate = new Translate(-offset.x * 12, -offset.y * 7);
            m_CharacterHost.style.translate = new Translate(offset.x * 16, offset.y * 10);
        }
    }
}
