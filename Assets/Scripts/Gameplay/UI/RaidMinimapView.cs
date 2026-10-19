using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;

namespace Unity.MP_FPS
{
    /// <summary>Client presentation only; consumes the same owned-player and raid snapshot as RaidHUD.</summary>
    public sealed class RaidMinimapView : IDisposable
    {
        public const float HalfRange = 120f;
        private const float CaptureInterval = .2f;
        private static readonly Color SupplyColor = new Color(.91f, .73f, .46f);
        private static readonly Color ExitColor = new Color(.62f, .88f, .73f);
        private readonly VisualElement m_Panel, m_Overlay;
        private readonly Image m_Image;
        private readonly Label m_Distance, m_Signal;
        private readonly UniversalRenderPipeline.SingleCameraRequest m_Request = new UniversalRenderPipeline.SingleCameraRequest();
        private Camera m_Camera;
        private RenderTexture m_Target;
        private MoonRaidMap m_Map;
        private Vector3 m_Position, m_CapturePosition;
        private Vector2 m_Forward = Vector2.up;
        private uint m_TakenMask;
        private float m_NextCapture;
        private bool m_HasCapture, m_RenderUnavailable;

        public RaidMinimapView(VisualElement root)
        {
            m_Panel = root.Q("raidMinimap");
            m_Overlay = root.Q("raidMinimapOverlay");
            m_Image = root.Q<Image>("raidMinimapImage");
            m_Distance = root.Q<Label>("raidMinimapDistance");
            m_Signal = root.Q<Label>("raidMinimapSignal");
            m_Image.scaleMode = ScaleMode.StretchToFill;
            m_Overlay.generateVisualContent += Draw;
        }

        public void Hide()
        {
            m_Panel.style.display = DisplayStyle.None;
            m_NextCapture = 0;
        }

        public void Present(MoonRaidMap map, Vector3 position, Vector3 forward, uint takenMask)
        {
            if (!map || m_Panel.panel == null) { Hide(); return; }
            m_Panel.style.display = DisplayStyle.Flex;
            if (m_Map != map) { m_HasCapture = false; m_NextCapture = 0; }
            m_Map = map;
            m_Position = position;
            m_TakenMask = takenMask;
            var heading = new Vector2(forward.x, forward.z);
            if (heading.sqrMagnitude > .001f) m_Forward = heading.normalized;
            if (Time.unscaledTime >= m_NextCapture)
            {
                m_NextCapture = Time.unscaledTime + CaptureInterval;
                Capture();
                Vector3 delta = map.ExtractionPosition - position;
                MoonkovLocalization.Set(m_Distance, "EXIT / {0:0} m", new Vector2(delta.x, delta.z).magnitude);
                MoonkovLocalization.Set(m_Signal, m_RenderUnavailable ? "SURFACE / NO IMAGE" : "SURFACE / LIVE");
            }
            m_Overlay.MarkDirtyRepaint();
        }

        private void Capture()
        {
            if (m_RenderUnavailable) return;
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                m_RenderUnavailable = true;
                return;
            }
            if (!m_Camera)
            {
                var go = new GameObject("Raid minimap camera") { hideFlags = HideFlags.HideAndDontSave };
                go.transform.SetParent(m_Map.transform, false);
                m_Camera = go.AddComponent<Camera>();
                m_Camera.enabled = false;
                m_Camera.orthographic = true;
                m_Camera.orthographicSize = HalfRange;
                m_Camera.aspect = 1;
                m_Camera.nearClipPlane = .5f;
                m_Camera.farClipPlane = 1600f;
                m_Camera.clearFlags = CameraClearFlags.SolidColor;
                m_Camera.backgroundColor = new Color(.06f, .075f, .09f);
                m_Camera.allowHDR = false;
                m_Camera.allowMSAA = false;
                m_Camera.useOcclusionCulling = false;
                var data = m_Camera.GetUniversalAdditionalCameraData();
                data.renderShadows = false;
                data.renderPostProcessing = false;
                data.volumeLayerMask = 0;
                data.requiresColorOption = CameraOverrideOption.Off;
                data.requiresDepthOption = CameraOverrideOption.Off;
                if (!RenderPipeline.SupportsRenderRequest(m_Camera, m_Request))
                {
                    m_RenderUnavailable = true;
                    return;
                }
                m_Target = new RenderTexture(256, 256, 24, RenderTextureFormat.ARGB32)
                {
                    name = "Raid minimap 256", hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp
                };
                m_Target.Create();
                m_Image.image = m_Target;
                m_Request.destination = m_Target;
            }
            // Surface overview: fixed north-up. UI and first-person overlays never enter this camera.
            m_Camera.cullingMask = ~LayerMask.GetMask("UI", "FirstPersonOverlay", "ServerPlayer", "ClientPlayer");
            m_Camera.transform.SetPositionAndRotation(m_Position + Vector3.up * 650f, Quaternion.Euler(90, 0, 0));
            RenderPipeline.SubmitRenderRequest(m_Camera, m_Request);
            // Project against the captured origin so moving markers stay aligned between terrain refreshes.
            m_CapturePosition = m_Position;
            m_HasCapture = true;
            m_Image.MarkDirtyRepaint();
        }

        /// <summary>North (+Z) is up; the same square projection is used for terrain and all markers.</summary>
        public static Vector2 Project(Vector3 world, Vector3 origin, Vector2 size)
        {
            return new Vector2((.5f + (world.x - origin.x) / (HalfRange * 2)) * size.x,
                (.5f - (world.z - origin.z) / (HalfRange * 2)) * size.y);
        }

        private void Draw(MeshGenerationContext context)
        {
            Vector2 size = m_Overlay.contentRect.size;
            if (!m_Map || size.x < 1 || size.y < 1) return;
            var painter = context.painter2D;
            Vector3 origin = m_HasCapture ? m_CapturePosition : m_Position;
            painter.lineWidth = 1;
            painter.strokeColor = new Color(.8f, .86f, .9f, .13f);
            for (int i = 1; i < 4; i++)
            {
                Line(painter, new Vector2(size.x * i / 4, 0), new Vector2(size.x * i / 4, size.y));
                Line(painter, new Vector2(0, size.y * i / 4), new Vector2(size.x, size.y * i / 4));
            }
            for (int i = 0; i < m_Map.LootPositions.Length && i < 32; i++)
            {
                Vector2 point = Project(m_Map.LootPositions[i], origin, size);
                if (!Inside(point, size, 8)) continue;
                bool empty = (m_TakenMask & (1u << i)) != 0;
                painter.strokeColor = empty ? new Color(.56f, .6f, .63f, .65f) : SupplyColor;
                painter.fillColor = new Color(.06f, .07f, .08f, .9f);
                painter.lineWidth = 1.5f;
                painter.BeginPath();
                painter.MoveTo(point + new Vector2(-3, -3));
                painter.LineTo(point + new Vector2(3, -3));
                painter.LineTo(point + new Vector2(3, 3));
                painter.LineTo(point + new Vector2(-3, 3));
                painter.ClosePath(); painter.Fill(); painter.Stroke();
                if (empty) Line(painter, point + new Vector2(-3, 3), point + new Vector2(3, -3));
            }
            Vector2 exit = Project(m_Map.ExtractionPosition, origin, size);
            bool outside = !Inside(exit, size, 12);
            if (outside)
            {
                var delta = exit - size * .5f;
                float factor = Mathf.Min((size.x * .5f - 12) / Mathf.Max(.001f, Mathf.Abs(delta.x)),
                    (size.y * .5f - 12) / Mathf.Max(.001f, Mathf.Abs(delta.y)));
                exit = size * .5f + delta * factor;
                Vector2 direction = delta.normalized;
                Triangle(painter, exit, direction, 7, ExitColor);
            }
            else
            {
                painter.strokeColor = ExitColor; painter.fillColor = new Color(.06f, .12f, .09f, .95f);
                painter.lineWidth = 2;
                painter.BeginPath(); painter.MoveTo(exit + Vector2.up * 6);
                painter.LineTo(exit + Vector2.right * 6); painter.LineTo(exit + Vector2.down * 6);
                painter.LineTo(exit + Vector2.left * 6); painter.ClosePath(); painter.Fill(); painter.Stroke();
                Vector2 zone = size * (m_Map.ExtractionRadius / (HalfRange * 2));
                painter.strokeColor = new Color(ExitColor.r, ExitColor.g, ExitColor.b, .45f);
                painter.BeginPath(); painter.Arc(exit, Mathf.Max(8, zone.x), Angle.Degrees(0), Angle.Degrees(360)); painter.Stroke();
            }
            Vector2 player = Project(m_Position, origin, size);
            Triangle(painter, player, new Vector2(m_Forward.x, -m_Forward.y), 8, new Color(.95f, .96f, .93f));
        }

        private static bool Inside(Vector2 point, Vector2 size, float padding) =>
            point.x >= padding && point.y >= padding && point.x <= size.x - padding && point.y <= size.y - padding;

        private static void Line(Painter2D painter, Vector2 a, Vector2 b)
        {
            painter.BeginPath(); painter.MoveTo(a); painter.LineTo(b); painter.Stroke();
        }

        private static void Triangle(Painter2D painter, Vector2 center, Vector2 direction, float radius, Color color)
        {
            Vector2 right = new Vector2(-direction.y, direction.x);
            painter.fillColor = color; painter.strokeColor = new Color(.035f, .045f, .06f); painter.lineWidth = 2;
            painter.BeginPath(); painter.MoveTo(center + direction * radius);
            painter.LineTo(center - direction * radius * .65f + right * radius * .6f);
            painter.LineTo(center - direction * radius * .3f);
            painter.LineTo(center - direction * radius * .65f - right * radius * .6f);
            painter.ClosePath(); painter.Fill(); painter.Stroke();
        }

        public void Dispose()
        {
            m_Overlay.generateVisualContent -= Draw;
            m_Image.image = null;
            if (m_Camera) Destroy(m_Camera.gameObject);
            if (m_Target) { m_Target.Release(); Destroy(m_Target); }
            m_Camera = null; m_Target = null;
        }

        private static void Destroy(UnityEngine.Object value)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(value);
            else UnityEngine.Object.DestroyImmediate(value);
        }
    }
}
