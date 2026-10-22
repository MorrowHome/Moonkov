using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MP_FPS
{
    // North-up schematic. Every marker consumes approved snapshots or the owned player;
    // this view never queries entities, cameras or scene geometry.
    public sealed class BountyClashMapView : VisualElement
    {
        public const float MinimumZoom = 1, MaximumZoom = 8;
        public float Zoom { get; private set; } = MinimumZoom;
        private ClashSnapshotRpc m_Snapshot;
        private Vector3 m_Local;
        private Vector2 m_Forward = Vector2.up, m_Center, m_BoundsCenter;
        private float m_Radius = 120;
        private bool m_HasLocal, m_HasBounds, m_Expedition, m_Dragging;
        private int m_Pointer;
        private Vector2 m_LastPointer;
        private MoonRaidMap m_ExpeditionSource;
        private Vector3[] m_Supplies;
        private Vector3 m_Exit;
        private Vector2 m_SupplyMin, m_SupplyMax;
        private uint m_TakenMask;
        private readonly List<ClashParticipantRpc> m_Friends = new List<ClashParticipantRpc>(3);
        private readonly Label m_ExitA, m_ExitB, m_You;

        public BountyClashMapView()
        {
            pickingMode = PickingMode.Position; focusable = true;
            AddToClassList("raid-map-canvas"); generateVisualContent += Draw;
            var north = MapLabel("N"); north.style.left = 10; north.style.top = 8; Add(north);
            m_ExitA = MapLabel("A"); m_ExitB = MapLabel("B"); m_You = MapLabel("YOU");
            Add(m_ExitA); Add(m_ExitB); Add(m_You);
            RegisterCallback<GeometryChangedEvent>(_ => Changed());
            RegisterCallback<WheelEvent>(Wheel);
            RegisterCallback<PointerDownEvent>(Down);
            RegisterCallback<PointerMoveEvent>(Move);
            RegisterCallback<PointerUpEvent>(Up);
            RegisterCallback<PointerCaptureOutEvent>(_ => m_Dragging = false);
            RegisterCallback<DetachFromPanelEvent>(_ => CancelDrag());
        }
        private static Label MapLabel(string text)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.AddToClassList("raid-map-marker-label"); return label;
        }
        private static Vector2 XZ(Vector3 point) => new Vector2(point.x, point.z);
        private float Scale => Mathf.Max(1, Mathf.Min(contentRect.width, contentRect.height) - 48) / (2 * m_Radius) * Zoom;
        private Vector2 Project(Vector3 point) => ProjectXZ(XZ(point));
        private Vector2 ProjectXZ(Vector2 point)
        { var d = point - m_Center; return contentRect.center + new Vector2(d.x, -d.y) * Scale; }
        private Vector2 Unproject(Vector2 point)
        { var d = (point - contentRect.center) / Scale; return m_Center + new Vector2(d.x, -d.y); }
        private void Bounds(Vector2 center, float radius, bool reset)
        {
            m_BoundsCenter = center; m_Radius = Mathf.Max(1, radius);
            if (!m_HasBounds || reset) { m_Center = center; Zoom = MinimumZoom; }
            m_HasBounds = true;
        }
        public void Present(ClashSnapshotRpc value, IEnumerable<ClashParticipantRpc> rows, Vector3 local, bool hasLocal, Vector3 forward = default)
        {
            bool reset = m_Expedition || !m_Snapshot.MatchId.Equals(value.MatchId) || m_Snapshot.Epoch != value.Epoch;
            m_Expedition = false; m_ExpeditionSource = null; m_Supplies = null;
            m_Snapshot = value; m_Local = local; m_HasLocal = hasLocal; Heading(forward); m_Friends.Clear();
            Bounds(XZ(value.ArenaCenter), value.ArenaRadius, reset);
            foreach (var row in rows)
                if (row.OwnTeam && row.TeamId == value.LocalTeamId && !row.IsLocal && (row.Life == ClashLife.Alive || row.Life == ClashLife.Downed)) m_Friends.Add(row);
            Changed();
        }
        public void PresentExpedition(MoonRaidMap map, Vector3 local, Vector3 forward, uint takenMask)
        {
            bool reset = !m_Expedition || m_ExpeditionSource != map;
            m_Expedition = true; m_ExpeditionSource = map; m_Snapshot = default; m_Friends.Clear();
            m_Local = local; m_HasLocal = true; Heading(forward); m_TakenMask = takenMask;
            m_Supplies = map.LootPositions; m_Exit = map.ExtractionPosition;
            if (reset)
            {
                m_SupplyMin = m_SupplyMax = XZ(m_Exit);
                foreach (var point in m_Supplies) { m_SupplyMin = Vector2.Min(m_SupplyMin, XZ(point)); m_SupplyMax = Vector2.Max(m_SupplyMax, XZ(point)); }
            }
            // Full-map bounds also include the current owner, even after walking far before opening M.
            var min = Vector2.Min(m_SupplyMin, XZ(local)); var max = Vector2.Max(m_SupplyMax, XZ(local));
            Bounds((min + max) * .5f, Mathf.Max(120, (max - min).magnitude * .55f), reset);
            Changed();
        }
        private void Heading(Vector3 forward)
        { var heading = XZ(forward); if (heading.sqrMagnitude > .001f) m_Forward = heading.normalized; }
        public void ResetData()
        {
            CancelDrag(); m_Snapshot = default; m_Friends.Clear(); m_Supplies = null; m_ExpeditionSource = null;
            m_HasLocal = m_HasBounds = false; Zoom = MinimumZoom; Changed();
        }
        public void FullMap() { Zoom = MinimumZoom; m_Center = m_BoundsCenter; Changed(); }
        public void CenterOnPlayer() { if (m_HasLocal) { m_Center = XZ(m_Local); Changed(); } }
        public void ZoomBy(float factor) => ZoomAt(factor, contentRect.center);
        private void ZoomAt(float factor, Vector2 point)
        {
            if (!m_HasBounds || !float.IsFinite(factor) || factor <= 0) return;
            var anchor = Unproject(point); Zoom = Mathf.Clamp(Zoom * factor, MinimumZoom, MaximumZoom);
            m_Center += anchor - Unproject(point); ClampPan(); Changed();
        }
        private void ClampPan()
        {
            var min = m_BoundsCenter - Vector2.one * m_Radius * 1.5f;
            var max = m_BoundsCenter + Vector2.one * m_Radius * 1.5f;
            if (m_HasLocal) { min = Vector2.Min(min, XZ(m_Local)); max = Vector2.Max(max, XZ(m_Local)); }
            m_Center = new Vector2(Mathf.Clamp(m_Center.x, min.x, max.x), Mathf.Clamp(m_Center.y, min.y, max.y));
        }
        private void Wheel(WheelEvent evt)
        {
            ZoomAt(Mathf.Pow(1.15f, -Mathf.Clamp(evt.delta.y, -4, 4)), this.WorldToLocal(evt.mousePosition));
            evt.StopPropagation();
        }
        private void Down(PointerDownEvent evt)
        {
            if (evt.button != 0) return;
            m_Dragging = true; m_Pointer = evt.pointerId; m_LastPointer = evt.localPosition;
            this.CapturePointer(m_Pointer); Focus(); evt.StopPropagation();
        }
        private void Move(PointerMoveEvent evt)
        {
            if (!m_Dragging || evt.pointerId != m_Pointer) return;
            var current = (Vector2)evt.localPosition; var delta = current - m_LastPointer; m_LastPointer = current;
            m_Center += new Vector2(-delta.x, delta.y) / Scale; ClampPan(); Changed(); evt.StopPropagation();
        }
        private void Up(PointerUpEvent evt)
        { if (m_Dragging && evt.pointerId == m_Pointer) { CancelDrag(); evt.StopPropagation(); } }
        public void CancelDrag()
        { bool captured = m_Dragging; m_Dragging = false; if (captured && this.HasPointerCapture(m_Pointer)) this.ReleasePointer(m_Pointer); }
        private bool Inside(Vector2 point) => point.x >= 12 && point.y >= 12 && point.x <= contentRect.width - 12 && point.y <= contentRect.height - 12;
        private void Place(Label label, Vector3 world, bool show)
        {
            var point = Project(world); label.style.display = show && m_HasBounds && Inside(point) ? DisplayStyle.Flex : DisplayStyle.None;
            label.style.left = point.x + 8; label.style.top = point.y - 8;
        }
        internal void Relocalize() => MoonkovLocalization.Set(m_You, "YOU");
        private void Changed()
        {
            Relocalize();
            Place(m_ExitA, m_Expedition ? m_Exit : (Vector3)m_Snapshot.ExitPositionA, true);
            Place(m_ExitB, m_Snapshot.ExitPositionB, !m_Expedition);
            Place(m_You, m_Local, m_HasLocal); MarkDirtyRepaint();
        }
        private static void Line(Painter2D p, Vector2 a, Vector2 b)
        { p.BeginPath(); p.MoveTo(a); p.LineTo(b); p.Stroke(); }
        private static void Dot(Painter2D p, Vector2 at, float radius, Color color)
        { p.fillColor = color; p.BeginPath(); p.Arc(at, radius, Angle.Degrees(0), Angle.Degrees(360)); p.Fill(); }
        private static void Diamond(Painter2D p, Vector2 at, float radius, Color color)
        {
            p.strokeColor = color; p.lineWidth = 2; p.BeginPath(); p.MoveTo(at + Vector2.up * radius);
            p.LineTo(at + Vector2.right * radius); p.LineTo(at + Vector2.down * radius); p.LineTo(at + Vector2.left * radius); p.ClosePath(); p.Stroke();
        }
        private void Draw(MeshGenerationContext context)
        {
            if (!m_HasBounds || contentRect.width < 2 || contentRect.height < 2) return;
            var p = context.painter2D; var blue = new Color(.25f, .48f, 1); var ink = new Color(.94f, .95f, .93f);
            var min = Unproject(new Vector2(0, contentRect.height)); var max = Unproject(new Vector2(contentRect.width, 0));
            float step = Mathf.Max(25, Mathf.Ceil(Mathf.Max(max.x - min.x, max.y - min.y) / 20 / 25) * 25);
            p.lineWidth = 1; p.strokeColor = new Color(.5f, .56f, .65f, .15f);
            for (float x = Mathf.Ceil(min.x / step) * step; x <= max.x; x += step) Line(p, ProjectXZ(new Vector2(x, min.y)), ProjectXZ(new Vector2(x, max.y)));
            for (float z = Mathf.Ceil(min.y / step) * step; z <= max.y; z += step) Line(p, ProjectXZ(new Vector2(min.x, z)), ProjectXZ(new Vector2(max.x, z)));
            p.strokeColor = new Color(.6f, .65f, .72f, .65f); p.BeginPath(); p.Arc(ProjectXZ(m_BoundsCenter), m_Radius * Scale, Angle.Degrees(0), Angle.Degrees(360)); p.Stroke();
            if (m_Expedition)
            {
                for (int i = 0; i < m_Supplies.Length && i < 32; i++)
                { var point = Project(m_Supplies[i]); if (Inside(point)) Diamond(p, point, 4, (m_TakenMask & (1u << i)) == 0 ? ink : new Color(.4f, .44f, .5f)); }
                if (Inside(Project(m_Exit))) Dot(p, Project(m_Exit), 6, blue);
            }
            else
            {
                if (m_Snapshot.Bounty == ClashBountyState.Unbanished || m_Snapshot.Bounty == ClashBountyState.Banishing)
                    Diamond(p, Project(m_Snapshot.TargetPosition), 8, ink);
                Dot(p, Project(m_Snapshot.ExitPositionA), 6, blue); Dot(p, Project(m_Snapshot.ExitPositionB), 6, blue);
                foreach (var friend in m_Friends)
                {
                    var point = Project(friend.Position); if (!Inside(point)) continue;
                    if (friend.Life == ClashLife.Downed)
                    { p.strokeColor = new Color(1, .65f, .55f); p.lineWidth = 2; Line(p, point + Vector2.left * 5, point + Vector2.right * 5); Line(p, point + Vector2.up * 5, point + Vector2.down * 5); }
                    else Dot(p, point, 4, new Color(.63f, .73f, .9f));
                }
                if (m_Snapshot.Bounty == ClashBountyState.Ground || m_Snapshot.Bounty == ClashBountyState.Carried && (m_Snapshot.IsCarrier || m_Snapshot.TeamHasBounty))
                    Diamond(p, Project(m_Snapshot.BountyPosition), 9, blue); // Already-authorized exact position: never round it.
                else if (m_Snapshot.Bounty == ClashBountyState.Carried)
                {
                    // The server supplies a 25m coarse position. Draw uncertainty, never a precise carrier icon.
                    p.fillColor = new Color(.09f, .29f, 1, .12f); p.strokeColor = blue; p.lineWidth = 2;
                    p.BeginPath(); p.Arc(Project(m_Snapshot.BountyPosition), 25 * Scale, Angle.Degrees(0), Angle.Degrees(360)); p.Fill(); p.Stroke();
                }
            }
            if (m_HasLocal)
            {
                var at = Project(m_Local); if (!Inside(at)) return;
                var forward = new Vector2(m_Forward.x, -m_Forward.y); var right = new Vector2(-forward.y, forward.x);
                p.fillColor = ink; p.strokeColor = new Color(.04f, .05f, .07f); p.lineWidth = 2;
                p.BeginPath(); p.MoveTo(at + forward * 10); p.LineTo(at - forward * 7 + right * 6);
                p.LineTo(at - forward * 3); p.LineTo(at - forward * 7 - right * 6); p.ClosePath(); p.Fill(); p.Stroke();
            }
        }
    }
}
