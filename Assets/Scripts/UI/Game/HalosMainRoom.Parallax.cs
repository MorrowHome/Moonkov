// 船舱背景随鼠标做轻微位移。这里跟随一个平滑的目标偏移，并给各图层不同位移量，形成前后层次。
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MP_FPS.Client {
    public sealed partial class HalosMainRoom {
        private Vector2 m_PointerTarget, m_Parallax, m_ParallaxVelocity;

        private void TrackParallax(PointerMoveEvent evt) {
            if (IsPreviewOpen || evt.pointerType != UnityEngine.UIElements.PointerType.mouse) return;
            var point = m_Viewport.WorldToLocal(evt.position);
            if (!m_Viewport.contentRect.Contains(point)) {
                m_PointerTarget = Vector2.zero;
                return;
            }
            // 把鼠标移到屏幕左、中、右时分别近似映射成 -1、0、1。这样后面各图层只需乘自己的位移系数。
            m_PointerTarget = new Vector2(Mathf.Clamp(point.x / 960 - 1, -1, 1), Mathf.Clamp(point.y / 540 - 1, -1, 1));
        }

        private void LeaveParallax(PointerLeaveEvent evt) {
            if (evt.target == Root) m_PointerTarget = Vector2.zero;
        }

        private void ResetParallax() { m_PointerTarget = m_Parallax = m_ParallaxVelocity = Vector2.zero; }

        private void AdvanceParallax(float dt, bool animate) {
            if (!animate) {
                ResetParallax();
                return;
            }
            // 切舱或打开平板期间把视差目标拉回中心。平时用 SmoothDamp 追向指针，松开输入也能自然回来。
            var target = IsMoving || IsOpening || m_TabletState > 0 ? Vector2.zero : m_PointerTarget;
            m_Parallax = Vector2.SmoothDamp(m_Parallax, target, ref m_ParallaxVelocity, .24f, Mathf.Infinity, dt);
        }

        private void PaintParallax() {
            bool animate = Animate;
            var offset = animate ? m_Parallax : Vector2.zero;
            // 把背景略微放大，为横向和斜向的指针位移留下空间。
            // 底图略放大给边缘留出空间。近处的角色和窗外月球用不同方向与幅度，移动才有前后层次。
            float scale = animate ? 1.04f : 1;
            m_ParallaxLayer.style.scale = new Scale(new Vector3(scale, scale, 1));
            m_ParallaxLayer.style.translate = new Translate(offset.x * 20, offset.y * 12);
            // 窗框挡住远处一些月球画面。近景人物的移动幅度更大。
            m_Moon.style.translate = new Translate(-offset.x * 12, -offset.y * 7);
            m_CharacterHost.style.translate = new Translate(offset.x * 16, offset.y * 10);
        }
    }
}