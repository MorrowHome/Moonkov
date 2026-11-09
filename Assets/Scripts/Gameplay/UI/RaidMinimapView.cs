using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MP_FPS
{
    /// <summary>Client presentation only; consumes the same owned-player and raid snapshot as RaidHUD.</summary>
    public sealed class RaidMinimapView : IDisposable
    {
        public const float HalfRange = 120f;
        private const float ReadoutInterval = .2f;
        private static readonly Color SupplyColor = new Color(.91f, .73f, .46f);
        private static readonly Color ExitColor = new Color(.62f, .88f, .73f);
        internal static readonly Color OxygenColor = new Color32(76, 204, 230, 255);
        private readonly VisualElement m_Panel, m_Overlay, m_Backdrop;
        private readonly Label m_Distance, m_Signal;
        private Texture2D m_Surface;
        private Rect m_SurfaceXZ;
        private MoonRaidMap m_Map;
        private Vector3 m_Position;
        private Vector2 m_Forward = Vector2.up;
        private ulong m_TakenMask;
        private float m_NextReadout;
        public bool Visible => m_Panel.style.display == DisplayStyle.Flex;

        public RaidMinimapView(VisualElement root)
        {
            m_Panel = root.Q("raidMinimap");
            m_Overlay = root.Q("raidMinimapOverlay");
            m_Backdrop = root.Q("raidMinimapImage");
            m_Distance = root.Q<Label>("raidMinimapDistance");
            m_Signal = root.Q<Label>("raidMinimapSignal");
            m_Backdrop.generateVisualContent += DrawSurface;
            m_Overlay.generateVisualContent += Draw;
        }

        public void Hide()
        {
            if (Visible) m_Panel.style.display = DisplayStyle.None;
            m_NextReadout = 0;
        }

        public void Present(MoonRaidMap map, BountyClashMapView tacticalMap, Vector3 position, Vector3 forward, ulong takenMask)
        {
            if (!map || m_Panel.panel == null) { Hide(); return; }
            if (!Visible) m_Panel.style.display = DisplayStyle.Flex;
            bool changedMap = m_Map != map;
            if (changedMap)
            {
                m_Map = map;
                tacticalMap.TryGetSatellite(out m_Surface, out m_SurfaceXZ);
                MoonkovLocalization.Set(m_Signal, m_Surface ? "SATELLITE" : "SURFACE / NO IMAGE");
                m_NextReadout = 0;
            }
            bool moved = position.x != m_Position.x || position.z != m_Position.z;
            bool changedLoot = takenMask != m_TakenMask;
            m_Position = position;
            m_TakenMask = takenMask;
            var heading = new Vector2(forward.x, forward.z);
            if (heading.sqrMagnitude <= .001f) heading = m_Forward;
            else heading.Normalize();
            bool turned = heading != m_Forward;
            m_Forward = heading;
            bool refresh = Time.unscaledTime >= m_NextReadout;
            if (refresh)
            {
                m_NextReadout = Time.unscaledTime + ReadoutInterval;
                Vector3 delta = map.NearestExtraction(position) - position;
                MoonkovLocalization.Set(m_Distance, "EXIT / {0:0} m", new Vector2(delta.x, delta.z).magnitude);
            }
            if (changedMap || moved) m_Backdrop.MarkDirtyRepaint();
            if (changedMap || moved || turned || changedLoot || refresh) m_Overlay.MarkDirtyRepaint();
        }

        private void DrawSurface(MeshGenerationContext context)
        {
            var size = m_Backdrop.contentRect.size;
            if (!m_Surface || size.x < 1 || size.y < 1) return;
            var topLeft = Project(new Vector3(m_SurfaceXZ.xMin, 0, m_SurfaceXZ.yMax), m_Position, size);
            var bottomRight = Project(new Vector3(m_SurfaceXZ.xMax, 0, m_SurfaceXZ.yMin), m_Position, size);
            BountyClashMapView.DrawSatellite(context, m_Surface, topLeft, bottomRight);
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
            Vector3 origin = m_Position;
            painter.lineWidth = 1;
            painter.strokeColor = new Color(.8f, .86f, .9f, .13f);
            for (int i = 1; i < 4; i++)
            {
                Line(painter, new Vector2(size.x * i / 4, 0), new Vector2(size.x * i / 4, size.y));
                Line(painter, new Vector2(0, size.y * i / 4), new Vector2(size.x, size.y * i / 4));
            }
            var furniture = RaidFurnitureLoot.Active;
            if (furniture && furniture.gameObject.scene == m_Map.gameObject.scene)
                foreach (var cache in furniture.Drawers)
                {
                    if (cache is not { SupplyCache: true } || !cache.Target || !cache.Target.gameObject.activeInHierarchy) continue;
                    var point = Project(cache.Position, origin, size);
                    if (Inside(point, size, 8)) DrawRouteCacheMarker(painter, point);
                }
            for (int i = 0; i < m_Map.LootPositions.Length && i < RaidLootContainers.FirstDeathBagId; i++)
            {
                Vector2 point = Project(m_Map.LootPositions[i], origin, size);
                if (!Inside(point, size, 8)) continue;
                bool empty = (m_TakenMask & (1UL << i)) != 0;
                painter.strokeColor = empty ? new Color(.56f, .6f, .63f, .65f) : SupplyColor;
                painter.fillColor = new Color(.06f, .07f, .08f, .9f);
                painter.lineWidth = 1.5f;
                Square(painter, point, 3);
                painter.Fill(); painter.Stroke();
                if (empty) Line(painter, point + new Vector2(-3, 3), point + new Vector2(3, -3));
            }
            var stations = OxygenRefillZone.ActiveZones;
            for (int i = 0; i < stations.Count; i++)
            {
                var station = stations[i];
                if (!station || station.gameObject.scene != m_Map.gameObject.scene) continue;
                var point = Project(station.transform.position, origin, size);
                if (Inside(point, size, 8)) DrawOxygenMarker(painter, point);
            }
            int nearest = m_Map.NearestExtractionIndex(m_Position);
            for (int i = 0; i < m_Map.ExtractionCount; i++)
            {
                if (!m_Map.TryExtractionPosition(i, out var point)) continue;
                Vector2 exit = Project(point, origin, size);
                if (!Inside(exit, size, 12))
                {
                    // One edge arrow keeps distant alternatives from crowding the map.
                    if (i != nearest) continue;
                    var delta = exit - size * .5f;
                    float factor = Mathf.Min((size.x * .5f - 12) / Mathf.Max(.001f, Mathf.Abs(delta.x)),
                        (size.y * .5f - 12) / Mathf.Max(.001f, Mathf.Abs(delta.y)));
                    exit = size * .5f + delta * factor;
                    Triangle(painter, exit, delta.normalized, 7, ExitColor);
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
            }
            Vector2 player = Project(m_Position, origin, size);
            Triangle(painter, player, new Vector2(m_Forward.x, -m_Forward.y), 8, new Color(.95f, .96f, .93f));
        }

        private static bool Inside(Vector2 point, Vector2 size, float padding) =>
            point.x >= padding && point.y >= padding && point.x <= size.x - padding && point.y <= size.y - padding;

        private static void Square(Painter2D painter, Vector2 point, float radius)
        {
            painter.BeginPath();
            painter.MoveTo(point + new Vector2(-radius, -radius));
            painter.LineTo(point + new Vector2(radius, -radius));
            painter.LineTo(point + new Vector2(radius, radius));
            painter.LineTo(point + new Vector2(-radius, radius));
            painter.ClosePath();
        }

        internal static void DrawRouteCacheMarker(Painter2D painter, Vector2 point)
        {
            painter.strokeColor = SupplyColor; painter.lineWidth = 1.25f;
            Square(painter, point, 2.75f);
            painter.Stroke();
        }

        internal static void DrawOxygenMarker(Painter2D painter, Vector2 point)
        {
            painter.strokeColor = OxygenColor; painter.fillColor = new Color(.04f, .11f, .13f, .96f);
            painter.lineWidth = 2;
            painter.BeginPath(); painter.Arc(point, 6, Angle.Degrees(0), Angle.Degrees(360));
            painter.Fill(); painter.Stroke();
            painter.fillColor = OxygenColor;
            painter.BeginPath(); painter.Arc(point, 1.8f, Angle.Degrees(0), Angle.Degrees(360)); painter.Fill();
        }

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
            m_Backdrop.generateVisualContent -= DrawSurface;
            m_Surface = null;
        }
    }
}
