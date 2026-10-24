using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.SceneManagement;

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
        private ulong m_TakenMask;
        private readonly List<ClashParticipantRpc> m_Friends = new List<ClashParticipantRpc>(3);
        private readonly Label m_ExitA, m_ExitB, m_You, m_Scale, m_NearestLabel, m_PinLabel, m_CacheLabel;
        private Vector2 m_Pin;
        private int m_Nearest = -1;
        public bool HasPin { get; private set; }
        public bool ShowSupplies { get; private set; }
        public void ToggleSupplies() { ShowSupplies = !ShowSupplies; m_CacheLabel.style.display = DisplayStyle.None; Changed(); }
        private static readonly Color[] SupplyColors = { new Color(.94f, .95f, .93f), new Color(.45f, .85f, .65f), new Color(1, .72f, .35f), new Color(.48f, .67f, 1) };
        private static readonly string[] SupplyNames = { "SUPPLIES", "MEDICAL", "INDUSTRIAL", "EQUIPMENT" };
        private readonly int[] m_AvailableByType = new int[4];
        [System.Serializable] private sealed class SatelliteImageData
        {
            public string scene = "";
            public float centerX = 0, centerZ = 0, span = 0;
            public MapLandmark[] landmarks = System.Array.Empty<MapLandmark>();
        }
        [System.Serializable] private sealed class MapLandmark { public string name = ""; public float x = 0, z = 0; }
        private readonly List<Label> m_Landmarks = new List<Label>();
        private Texture2D m_Surface;
        private SatelliteImageData m_SurfaceData;
        private string m_SurfaceScene;
        public bool HasSurface => m_Surface != null;

        private void LoadSurface(string scene)
        {
            if (m_SurfaceScene == scene) return;
            m_SurfaceScene = scene; m_Surface = null; m_SurfaceData = null;
            foreach (var label in m_Landmarks) label.RemoveFromHierarchy();
            m_Landmarks.Clear();
            var data = Resources.Load<TextAsset>("Moonkov/Maps/TheophilusSatelliteBounds");
            if (data == null) return;
            var info = JsonUtility.FromJson<SatelliteImageData>(data.text);
            if (info.scene != scene || !float.IsFinite(info.span) || info.span <= 0) return;
            m_SurfaceData = info; m_Surface = Resources.Load<Texture2D>("Moonkov/Maps/TheophilusSatellite");
            if (HasSurface && info.landmarks != null)
                foreach (var landmark in info.landmarks)
                {
                    var label = MapLabel(landmark.name); label.AddToClassList("raid-map-landmark");
                    MoonkovLocalization.Set(label, landmark.name); Insert(0, label); m_Landmarks.Add(label);
                }
        }

        public BountyClashMapView()
        {
            pickingMode = PickingMode.Position; focusable = true;
            AddToClassList("raid-map-canvas"); generateVisualContent += Draw;
            var north = MapLabel("N"); north.style.right = 18; north.style.top = 54; Add(north);
            m_ExitA = MapLabel("A"); m_ExitB = MapLabel("B"); m_You = MapLabel("YOU");
            Add(m_ExitA); Add(m_ExitB); Add(m_You);
            m_NearestLabel = MapLabel(""); m_PinLabel = MapLabel("PIN");
            Add(m_NearestLabel); Add(m_PinLabel);
            m_CacheLabel = MapLabel(""); Add(m_CacheLabel);
            m_Scale = MapLabel(""); m_Scale.RemoveFromClassList("raid-map-marker-label"); m_Scale.AddToClassList("raid-map-scale"); Add(m_Scale);
            RegisterCallback<GeometryChangedEvent>(_ => Changed());
            RegisterCallback<WheelEvent>(Wheel);
            RegisterCallback<PointerDownEvent>(Down);
            RegisterCallback<PointerMoveEvent>(Move);
            RegisterCallback<PointerLeaveEvent>(_ => m_CacheLabel.style.display = DisplayStyle.None);
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
            if (reset) ClearPin();
            if (reset) LoadSurface(SceneManager.GetActiveScene().name);
            m_Expedition = false; m_ExpeditionSource = null; m_Supplies = null;
            m_Snapshot = value; m_Local = local; m_HasLocal = hasLocal; Heading(forward); m_Friends.Clear();
            Bounds(XZ(value.ArenaCenter), value.ArenaRadius, reset);
            foreach (var row in rows)
                if (row.OwnTeam && row.TeamId == value.LocalTeamId && !row.IsLocal && (row.Life == ClashLife.Alive || row.Life == ClashLife.Downed)) m_Friends.Add(row);
            Changed();
        }
        public void PresentExpedition(MoonRaidMap map, Vector3 local, Vector3 forward, ulong takenMask)
        {
            bool reset = !m_Expedition || m_ExpeditionSource != map;
            if (reset) { ClearPin(); m_Nearest = -1; }
            if (reset) LoadSurface(map.gameObject.scene.name);
            m_Expedition = true; m_ExpeditionSource = map; m_Snapshot = default; m_Friends.Clear();
            m_Local = local; m_HasLocal = true; Heading(forward); m_TakenMask = takenMask;
            m_Supplies = map.LootPositions; m_Exit = map.ExtractionPosition;
            if (reset)
            {
                m_SupplyMin = m_SupplyMax = XZ(m_Exit);
                foreach (var point in m_Supplies) { m_SupplyMin = Vector2.Min(m_SupplyMin, XZ(point)); m_SupplyMax = Vector2.Max(m_SupplyMax, XZ(point)); }
                if (HasSurface)
                {
                    var center = new Vector2(m_SurfaceData.centerX, m_SurfaceData.centerZ);
                    m_SupplyMin = Vector2.Min(m_SupplyMin, center - Vector2.one * m_SurfaceData.span * .5f);
                    m_SupplyMax = Vector2.Max(m_SupplyMax, center + Vector2.one * m_SurfaceData.span * .5f);
                }
            }
            // Full-map bounds also include the current owner, even after walking far before opening M.
            var min = Vector2.Min(m_SupplyMin, XZ(local)); var max = Vector2.Max(m_SupplyMax, XZ(local));
            Bounds((min + max) * .5f, Mathf.Max(120, Mathf.Max(max.x - min.x, max.y - min.y) * .52f), reset);
            Changed();
        }
        private void Heading(Vector3 forward)
        { var heading = XZ(forward); if (heading.sqrMagnitude > .001f) m_Forward = heading.normalized; }
        public void ResetData()
        {
            CancelDrag(); m_Snapshot = default; m_Friends.Clear(); m_Supplies = null; m_ExpeditionSource = null;
            HasPin = ShowSupplies = false; m_Nearest = -1;
            m_Surface = null; m_SurfaceData = null; m_SurfaceScene = null;
            foreach (var label in m_Landmarks) label.RemoveFromHierarchy();
            m_Landmarks.Clear();
            m_HasLocal = m_HasBounds = false; Zoom = MinimumZoom; Changed();
        }
        public void FullMap() { Zoom = MinimumZoom; m_Center = m_BoundsCenter; Changed(); }
        public void CenterOnPlayer() { if (m_HasLocal) { m_Center = XZ(m_Local); Changed(); } }
        public void ZoomBy(float factor) => ZoomAt(factor, contentRect.center);
        public void ClearPin() { HasPin = false; Changed(); }
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
            if (evt.button == 1 && m_HasBounds && !m_Dragging)
            { m_Pin = Unproject(evt.localPosition); HasPin = true; Changed(); Focus(); evt.StopPropagation(); return; }
            if (evt.button != 0) return;
            m_Dragging = true; m_Pointer = evt.pointerId; m_LastPointer = evt.localPosition;
            this.CapturePointer(m_Pointer); Focus(); evt.StopPropagation();
        }
        private void Move(PointerMoveEvent evt)
        {
            if (!m_Dragging) { InspectCache(evt.localPosition); return; }
            if (evt.pointerId != m_Pointer) return;
            m_CacheLabel.style.display = DisplayStyle.None;
            var current = (Vector2)evt.localPosition; var delta = current - m_LastPointer; m_LastPointer = current;
            m_Center += new Vector2(-delta.x, delta.y) / Scale; ClampPan(); Changed(); evt.StopPropagation();
        }
        private void Up(PointerUpEvent evt)
        { if (m_Dragging && evt.pointerId == m_Pointer) { CancelDrag(); evt.StopPropagation(); } }
        public void CancelDrag()
        { bool captured = m_Dragging; m_Dragging = false; m_CacheLabel.style.display = DisplayStyle.None; if (captured && this.HasPointerCapture(m_Pointer)) this.ReleasePointer(m_Pointer); }
        private void InspectCache(Vector2 pointer)
        {
            int selected = -1; float best = 14 * 14;
            if (m_Expedition && ShowSupplies && m_Supplies != null)
                for (int i = 0; i < m_Supplies.Length && i < RaidLootContainers.FirstDeathBagId; i++)
                {
                    Vector2 point = Project(m_Supplies[i]); float distance = (point - pointer).sqrMagnitude;
                    if (Inside(point) && distance < best) { best = distance; selected = i; }
                }
            m_CacheLabel.style.display = selected >= 0 ? DisplayStyle.Flex : DisplayStyle.None;
            if (selected < 0) return;
            MoonkovLocalization.Set(m_CacheLabel, "#{0:00} {1} / {2}\n{3:0} m · {4:000}°", selected + 1, SupplyNames[SupplyType(selected)],
                Available(selected) ? "AVAILABLE" : "EMPTY", Distance(m_Supplies[selected]), Degrees(XZ(m_Supplies[selected]) - XZ(m_Local)));
            m_CacheLabel.style.left = Mathf.Clamp(pointer.x + 14, 0, Mathf.Max(0, contentRect.width - 220));
            m_CacheLabel.style.top = Mathf.Clamp(pointer.y + 14, 0, Mathf.Max(0, contentRect.height - 52));
        }
        private bool Inside(Vector2 point) => point.x >= 12 && point.y >= 12 && point.x <= contentRect.width - 12 && point.y <= contentRect.height - 12;
        private void Place(Label label, Vector3 world, bool show)
        {
            var point = Project(world); label.style.display = show && m_HasBounds && Inside(point) ? DisplayStyle.Flex : DisplayStyle.None;
            label.style.left = point.x + 8; label.style.top = point.y - 8;
        }
        internal void Relocalize() => MoonkovLocalization.Set(m_You, "YOU");
        private float GridStep => Mathf.Max(25, Mathf.Ceil(Mathf.Max(contentRect.width, contentRect.height) / Scale / 20 / 25) * 25);
        private float ScaleMeters
        {
            get
            {
                float desired = 100 / Scale, power = Mathf.Pow(10, Mathf.Floor(Mathf.Log10(desired)));
                float units = desired / power;
                return (units >= 5 ? 5 : units >= 2 ? 2 : 1) * power;
            }
        }
        private float Distance(Vector3 point) => Vector2.Distance(XZ(m_Local), XZ(point));
        private static float Bearing(Vector2 direction) => Mathf.Repeat(Mathf.Atan2(direction.x, direction.y) * Mathf.Rad2Deg, 360);
        private static int Degrees(Vector2 direction) => Mathf.RoundToInt(Bearing(direction)) % 360;
        private int SupplyType(int index) => Mathf.Clamp((int)m_ExpeditionSource.GetLootProfile(index), 0, 3);
        private bool Available(int index) => (m_TakenMask & (1UL << index)) == 0;

        // Readouts consume the same approved data as the markers. No scene/enemy scans.
        public void PresentReadouts(Label position, Label extraction, Label supplies, Label objective, Label navigation)
        {
            if (!m_HasLocal || !m_HasBounds)
            {
                MoonkovLocalization.Set(position, "POSITION UNAVAILABLE");
                MoonkovLocalization.Set(extraction, ""); MoonkovLocalization.Set(supplies, "");
                MoonkovLocalization.Set(objective, ""); MoonkovLocalization.Set(navigation, ""); return;
            }
            MoonkovLocalization.Set(position, "POSITION / X {0:0} · Z {1:0} · ALT {2:0} m\nHEADING / {3:000}° (000° NORTH)",
                m_Local.x, m_Local.z, m_Local.y, Degrees(m_Forward));
            if (m_Expedition)
            {
                MoonkovLocalization.Set(extraction, "EXIT / {0:0} m · {1:000}°\nENTER {2:0} m ZONE / HOLD {3:0.#} s", Distance(m_Exit), Degrees(XZ(m_Exit) - XZ(m_Local)),
                    m_ExpeditionSource.ExtractionRadius, m_ExpeditionSource.ExtractionSeconds);
                System.Array.Clear(m_AvailableByType, 0, m_AvailableByType.Length);
                int total = Mathf.Min(m_Supplies.Length, RaidLootContainers.FirstDeathBagId), remaining = 0;
                float nearest = float.MaxValue; m_Nearest = -1;
                for (int i = 0; i < total; i++)
                {
                    if (!Available(i)) continue;
                    remaining++; m_AvailableByType[SupplyType(i)]++;
                    float distance = Distance(m_Supplies[i]);
                    if (distance < nearest) { nearest = distance; m_Nearest = i; }
                }
                MoonkovLocalization.Set(supplies, "CACHES / {0}/{1} AVAILABLE\nSUP {2} · MED {3} · IND {4} · EQP {5}", remaining, total,
                    m_AvailableByType[0], m_AvailableByType[1], m_AvailableByType[2], m_AvailableByType[3]);
                if (m_Nearest >= 0)
                {
                    MoonkovLocalization.Set(objective, "NEAREST / #{0:00} {1}\n{2:0} m · {3:000}°", m_Nearest + 1, SupplyNames[SupplyType(m_Nearest)], nearest,
                        Degrees(XZ(m_Supplies[m_Nearest]) - XZ(m_Local)));
                    MoonkovLocalization.Set(m_NearestLabel, "#{0:00} {1}", m_Nearest + 1, SupplyNames[SupplyType(m_Nearest)]);
                }
                else MoonkovLocalization.Set(objective, "NO AVAILABLE CACHES");
            }
            else
            {
                MoonkovLocalization.Set(extraction, "EXIT A / {0:0} m · {1}\nEXIT B / {2:0} m · {3}", Distance(m_Snapshot.ExitPositionA), ExitStatus(m_Snapshot.ExitBlockA, m_Snapshot.ExitRemainingA),
                    Distance(m_Snapshot.ExitPositionB), ExitStatus(m_Snapshot.ExitBlockB, m_Snapshot.ExitRemainingB));
                int downed = 0; foreach (var friend in m_Friends) if (friend.Life == ClashLife.Downed) downed++;
                MoonkovLocalization.Set(supplies, "TEAM / {0} TEAMMATES\nDOWNED / {1}", m_Friends.Count, downed);
                if (m_Snapshot.Bounty == ClashBountyState.Extracted) MoonkovLocalization.Set(objective, "BOUNTY HAS LEFT THE ARENA");
                else if (m_Snapshot.Bounty == ClashBountyState.Carried && !m_Snapshot.IsCarrier && !m_Snapshot.TeamHasBounty)
                    MoonkovLocalization.Set(objective, "RIVAL BOUNTY / APPROXIMATE AREA ONLY");
                else
                {
                    bool bounty = m_Snapshot.Bounty == ClashBountyState.Ground || m_Snapshot.Bounty == ClashBountyState.Carried;
                    Vector3 target = bounty ? m_Snapshot.BountyPosition : m_Snapshot.TargetPosition;
                    MoonkovLocalization.Set(objective, "{0} / {1:0} m · {2:000}°", bounty ? "BOUNTY" : "TARGET", Distance(target), Degrees(XZ(target) - XZ(m_Local)));
                }
            }
            if (HasPin) MoonkovLocalization.Set(navigation, "PIN / X {0:0} · Z {1:0} / {2:0} m · {3:000}° / STRAIGHT LINE ONLY",
                m_Pin.x, m_Pin.y, Vector2.Distance(XZ(m_Local), m_Pin), Degrees(m_Pin - XZ(m_Local)));
            else MoonkovLocalization.Set(navigation, "RIGHT CLICK TO SET A PERSONAL PIN / DISTANCES ARE HORIZONTAL, NOT WALKABLE ROUTES");
            Changed();
        }
        private static string ExitStatus(ClashExitBlock block, float remaining) => block == ClashExitBlock.Enemy ? MoonkovLocalization.Text("ENEMY TEAM") :
            block == ClashExitBlock.DownedTeammate ? MoonkovLocalization.Text("TEAMMATE DOWNED") : block == ClashExitBlock.NotActive ? MoonkovLocalization.Text("NOT ACTIVE") :
            remaining >= 0 ? MoonkovLocalization.Format($"HOLD / {Mathf.CeilToInt(remaining)} s") : MoonkovLocalization.Text("ENTER ZONE / HOLD 30s");
        private void Changed()
        {
            Relocalize();
            Place(m_ExitA, m_Expedition ? m_Exit : (Vector3)m_Snapshot.ExitPositionA, true);
            Place(m_ExitB, m_Snapshot.ExitPositionB, !m_Expedition);
            Place(m_You, m_Local, false); MarkDirtyRepaint();
            m_ExitA.style.top = Project(m_Expedition ? m_Exit : (Vector3)m_Snapshot.ExitPositionA).y - 40;
            m_ExitB.style.top = Project(m_Snapshot.ExitPositionB).y - 40;
            if (m_Expedition) MoonkovLocalization.Set(m_ExitA, "EXTRACTION");
            else MoonkovLocalization.Set(m_ExitA, "A / {0:0} m", Distance(m_Snapshot.ExitPositionA));
            MoonkovLocalization.Set(m_ExitB, "B / {0:0} m", Distance(m_Snapshot.ExitPositionB));
            MoonkovLocalization.Set(m_PinLabel, "PIN / {0:0} m", Vector2.Distance(XZ(m_Local), m_Pin));
            Place(m_PinLabel, new Vector3(m_Pin.x, 0, m_Pin.y), HasPin);
            Place(m_NearestLabel, default, false);
            if (m_Expedition && m_Nearest >= 0 && m_Supplies != null && Vector2.Distance(Project(m_Local), Project(m_Supplies[m_Nearest])) < 90)
                m_NearestLabel.style.top = Project(m_Supplies[m_Nearest]).y + 14;
            if (HasPin && Vector2.Distance(Project(m_Local), ProjectXZ(m_Pin)) < 90) m_PinLabel.style.top = ProjectXZ(m_Pin).y - 30;
            for (int i = 0; i < m_Landmarks.Count; i++)
            {
                var landmark = m_SurfaceData.landmarks[i];
                Place(m_Landmarks[i], new Vector3(landmark.x, 0, landmark.z), true);
                m_Landmarks[i].style.top = ProjectXZ(new Vector2(landmark.x, landmark.z)).y + 20;
                ClampLabel(m_Landmarks[i]);
            }
            ClampLabel(m_ExitA); ClampLabel(m_ExitB); ClampLabel(m_You); ClampLabel(m_NearestLabel); ClampLabel(m_PinLabel);
            if (HasSurface) MoonkovLocalization.Set(m_Scale, "{0:0.#} m", ScaleMeters);
            else MoonkovLocalization.Set(m_Scale, "SCALE / {0:0.#} m · GRID / {1:0} m · NORTH UP", ScaleMeters, GridStep);
        }
        private void ClampLabel(Label label)
        {
            if (label.style.display == DisplayStyle.None) return;
            var size = label.layout.size;
            float width = float.IsFinite(size.x) && size.x > 0 ? size.x : 210;
            float height = float.IsFinite(size.y) && size.y > 0 ? size.y : 24;
            label.style.left = Mathf.Clamp(label.style.left.value.value, 8, Mathf.Max(8, contentRect.width - width - 8));
            label.style.top = Mathf.Clamp(label.style.top.value.value, 8, Mathf.Max(8, contentRect.height - height - 44));
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
            DrawSurface(context);
            var p = context.painter2D; var blue = new Color(.13f, .48f, .91f); var ink = new Color(.94f, .95f, .93f);
            var min = Unproject(new Vector2(0, contentRect.height)); var max = Unproject(new Vector2(contentRect.width, 0));
            float step = GridStep;
            if (!HasSurface)
            {
                p.lineWidth = 1; p.strokeColor = new Color(.7f, .76f, .72f, .15f);
                for (float x = Mathf.Ceil(min.x / step) * step; x <= max.x; x += step) Line(p, ProjectXZ(new Vector2(x, min.y)), ProjectXZ(new Vector2(x, max.y)));
                for (float z = Mathf.Ceil(min.y / step) * step; z <= max.y; z += step) Line(p, ProjectXZ(new Vector2(min.x, z)), ProjectXZ(new Vector2(max.x, z)));
            }
            p.strokeColor = ink; p.lineWidth = 2;
            var scaleStart = new Vector2(14, contentRect.height - 38); var scaleEnd = scaleStart + Vector2.right * (ScaleMeters * Scale);
            Line(p, scaleStart, scaleEnd); Line(p, scaleStart + Vector2.up * 4, scaleStart + Vector2.down * 4);
            Line(p, scaleEnd + Vector2.up * 4, scaleEnd + Vector2.down * 4);
            if (!m_Expedition)
            { p.strokeColor = new Color(.75f, .65f, .48f, .65f); p.BeginPath(); p.Arc(ProjectXZ(m_BoundsCenter), m_Radius * Scale, Angle.Degrees(0), Angle.Degrees(360)); p.Stroke(); }
            if (m_Expedition)
            {
                for (int i = 0; ShowSupplies && i < m_Supplies.Length && i < RaidLootContainers.FirstDeathBagId; i++)
                {
                    var point = Project(m_Supplies[i]); if (!Inside(point)) continue;
                    if (Available(i)) { Dot(p, point, i == m_Nearest ? 6 : 4, Color.white); Dot(p, point, i == m_Nearest ? 4 : 2.5f, SupplyColors[SupplyType(i)]); }
                    else
                    {
                        p.lineWidth = 1; p.strokeColor = new Color(.4f, .44f, .5f);
                        Line(p, point + new Vector2(-3, -3), point + new Vector2(3, 3));
                        Line(p, point + new Vector2(-3, 3), point + new Vector2(3, -3));
                    }
                }
                if (Inside(Project(m_Exit))) { Dot(p, Project(m_Exit), 7, Color.white); Dot(p, Project(m_Exit), 5, new Color(.16f, .57f, .32f)); }
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
            if (HasPin)
            {
                var pin = ProjectXZ(m_Pin); var red = new Color(.85f, .21f, .18f);
                p.lineWidth = 2; p.strokeColor = new Color(.13f, .48f, .91f, .7f);
                if (m_HasLocal) Line(p, Project(m_Local), pin);
                if (Inside(pin))
                {
                    p.fillColor = red; p.BeginPath(); p.MoveTo(pin + Vector2.up * 2);
                    p.LineTo(pin + new Vector2(-6, -8)); p.LineTo(pin + new Vector2(6, -8)); p.ClosePath(); p.Fill();
                    Dot(p, pin - Vector2.up * 10, 8, red); Dot(p, pin - Vector2.up * 10, 2.5f, Color.white);
                }
            }
            if (m_HasLocal)
            {
                var at = Project(m_Local); if (!Inside(at)) return;
                var forward = new Vector2(m_Forward.x, -m_Forward.y); var right = new Vector2(-forward.y, forward.x);
                p.fillColor = new Color(blue.r, blue.g, blue.b, .22f);
                p.BeginPath(); p.MoveTo(at); p.LineTo(at + forward * 30 + right * 17);
                p.LineTo(at + forward * 30 - right * 17); p.ClosePath(); p.Fill();
                Dot(p, at, 8, Color.white); Dot(p, at, 5.5f, blue);
            }
        }

        private void DrawSurface(MeshGenerationContext context)
        {
            if (!HasSurface) return;
            float half = m_SurfaceData.span * .5f;
            var topLeft = ProjectXZ(new Vector2(m_SurfaceData.centerX - half, m_SurfaceData.centerZ + half));
            var bottomRight = ProjectXZ(new Vector2(m_SurfaceData.centerX + half, m_SurfaceData.centerZ - half));
            var mesh = context.Allocate(4, 6, m_Surface);
            Color32 tint = Color.white;
            mesh.SetNextVertex(new Vertex { position = new Vector3(topLeft.x, topLeft.y, Vertex.nearZ), tint = tint, uv = new Vector2(0, 1) });
            mesh.SetNextVertex(new Vertex { position = new Vector3(bottomRight.x, topLeft.y, Vertex.nearZ), tint = tint, uv = new Vector2(1, 1) });
            mesh.SetNextVertex(new Vertex { position = new Vector3(bottomRight.x, bottomRight.y, Vertex.nearZ), tint = tint, uv = new Vector2(1, 0) });
            mesh.SetNextVertex(new Vertex { position = new Vector3(topLeft.x, bottomRight.y, Vertex.nearZ), tint = tint, uv = new Vector2(0, 0) });
            mesh.SetNextIndex(0); mesh.SetNextIndex(1); mesh.SetNextIndex(2); mesh.SetNextIndex(2); mesh.SetNextIndex(3); mesh.SetNextIndex(0);
        }
    }
}
