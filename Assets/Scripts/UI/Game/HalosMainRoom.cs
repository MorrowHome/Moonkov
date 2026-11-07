using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MP_FPS.Client
{
    // Port of Halos mainRoomControl. Coordinates, phases and rates use its 1920x1080 / 60 Hz space.
    public sealed partial class HalosMainRoom : IDisposable
    {
        [Serializable] private sealed class Layout { public Icon[] icons; }
        [Serializable] private sealed class Icon
        {
            public string name;
            public int room, page;
            public float x, y, width, height, hoverOffset, hitX, hitY, hitWidth, hitHeight;
        }
        private sealed class Monitor
        {
            public Icon Art;
            public Image Image;
            public Button Button;
            public Texture2D Normal, Hover;
            public bool Pointed, Focused;
        }
        private const string ArtPath = "Moonkov/UI/Halos/MainRoom/";
        private const float RoomSlideSeconds = .65f;
        private static readonly string[] PageNames = { "SHIP", "STASH", "HALO BUILD", "SUPPLIERS", "OPERATIONS", "TASKS", "BASE", "COMMS", "SETTINGS" };
        private readonly VisualElement m_Owner, m_Viewport, m_World, m_Left, m_Right;
        private readonly Image m_Tablet;
        private readonly List<Monitor> m_Monitors = new List<Monitor>();
        private readonly Image[] m_Lights = new Image[3];
        private readonly System.Random m_Random = new System.Random();
        private readonly Action<int> m_Navigate;
        private readonly IVisualElementScheduledItem m_Timer;
        private float m_Camera = 1920, m_Cooldown, m_Extrude, m_Flicker;
        private float m_SlideFrom = 1920, m_SlideAge = RoomSlideSeconds;
        private float m_TabletState = 100;
        private double m_LastTime;
        private bool m_Visible, m_Busy, m_Disposed;
        private int m_Edge;
        private Action m_Opened;
        public VisualElement Root { get; }
        public int Room { get; private set; } = 1;
        public float TabletState => m_TabletState;
        public bool IsOpening => m_Opened != null;
        public bool IsMoving => m_SlideAge < RoomSlideSeconds;

        public HalosMainRoom(VisualElement parent, Action<int> navigate)
        {
            m_Navigate = navigate; m_Owner = parent;
            var tree = Resources.Load<VisualTreeAsset>("Moonkov/UI/HalosMainRoom").CloneTree();
            Root = tree.Q("halosOriginalHome"); Root.RemoveFromHierarchy(); parent.Insert(1, Root);
            m_Viewport = Root.Q("halosRoomViewport"); m_World = Root.Q("halosRoomWorld");
            m_Tablet = Root.Q<Image>("halosRoomTablet"); m_Tablet.image = Load("Tablet"); m_Tablet.scaleMode = ScaleMode.StretchToFill;
            m_Left = Root.Q("halosRoomLeft"); m_Right = Root.Q("halosRoomRight");
            m_Left.RegisterCallback<PointerEnterEvent>(_ => EnterEdge(-1));
            m_Right.RegisterCallback<PointerEnterEvent>(_ => EnterEdge(1));
            m_Left.RegisterCallback<PointerLeaveEvent>(_ => m_Edge = 0);
            m_Right.RegisterCallback<PointerLeaveEvent>(_ => m_Edge = 0);
            Sprite(m_World, "Moon", new Rect(2010, 0, 1536, 1536));
            for (int i = 0; i < 3; i++) Sprite(m_World, "Room" + i, new Rect(i * 1920, 0, 1920, 1080));
            for (int i = 0; i < 3; i++)
                m_Lights[i] = Sprite(m_World, "Lights" + i, new Rect(i * 1920, 0, i == 2 ? 1880 : 1920, 1080));
            InitializeSubjects();
            var layout = JsonUtility.FromJson<Layout>(Resources.Load<TextAsset>(ArtPath + "Layout").text);
            foreach (var art in layout.icons)
            {
                var monitor = new Monitor { Art = art, Normal = Load(art.name), Hover = Load(art.name + "Hover") };
                monitor.Image = Sprite(m_World, art.name, new Rect(art.room * 1920 + art.x, art.y, art.width, art.height));
                monitor.Button = new Button(() => Open(art.page)) { name = "halosRoomPage" + art.page, tooltip = MoonkovLocalization.Text(PageNames[art.page]) };
                monitor.Button.AddToClassList("halos-room-hit");
                Place(monitor.Button, new Rect(art.room * 1920 + art.hitX, art.hitY, art.hitWidth, art.hitHeight));
                monitor.Button.RegisterCallback<PointerEnterEvent>(_ => monitor.Pointed = true);
                monitor.Button.RegisterCallback<PointerLeaveEvent>(_ => monitor.Pointed = false);
                monitor.Button.RegisterCallback<FocusInEvent>(_ => monitor.Focused = true);
                monitor.Button.RegisterCallback<FocusOutEvent>(_ => monitor.Focused = false);
                m_World.Add(monitor.Button); m_Monitors.Add(monitor);
            }
            Root.RegisterCallback<GeometryChangedEvent>(Resize);
            Root.RegisterCallback<KeyDownEvent>(KeyDown);
            Root.RegisterCallback<DetachFromPanelEvent>(Detached);
            m_LastTime = TerminalMotion.Now;
            m_Timer = Root.schedule.Execute(Tick).Every(16);
            Paint();
        }

        private static Texture2D Load(string name) => Resources.Load<Texture2D>(ArtPath + name);
        private static void Place(VisualElement element, Rect rect)
        {
            element.style.left = rect.x; element.style.top = rect.y;
            element.style.width = rect.width; element.style.height = rect.height;
        }
        private static Image Sprite(VisualElement parent, string name, Rect rect)
        {
            var image = new Image { name = "halosArt" + name, image = Load(name), scaleMode = ScaleMode.StretchToFill, pickingMode = PickingMode.Ignore };
            image.AddToClassList("halos-room-sprite"); Place(image, rect); parent.Add(image); return image;
        }
        private bool Animate => TerminalMotion.Animate(Root) && !HalosTerminalBackdrop.PrefersReducedMotion;
        public void Present(bool busy) { m_Busy = busy; UpdateInput(); }
        public void Show(bool returning)
        {
            ClosePreview(false);
            m_Visible = true; m_Opened = null; m_Edge = 0;
            m_Camera = Room * 1920; m_SlideAge = RoomSlideSeconds; m_Cooldown = 0;
            Root.style.display = DisplayStyle.Flex;
            if (returning) m_TabletState = 100;
            if (!Animate) m_TabletState = 0;
            m_LastTime = TerminalMotion.Now; m_Timer.Resume(); Paint();
        }
        public void Hide()
        {
            ClosePreview(false); ReleaseHomeCharacter();
            m_Visible = false; m_Opened = null; m_Edge = 0;
            m_Timer.Pause(); Root.style.display = DisplayStyle.None;
        }
        public void Open(int page)
        {
            if (!m_Visible || m_Disposed || m_Busy || IsMoving || IsPreviewOpen || IsOpening || m_TabletState > 0) return;
            if (!Animate) { m_Navigate(page); return; }
            m_Opened = () => m_Navigate(page); m_Edge = 0; UpdateInput();
        }
        public void MoveRoom(int direction)
        {
            if (!m_Visible || m_Disposed || IsMoving || IsPreviewOpen || IsOpening || m_TabletState > 0 || m_Cooldown > 0) return;
            int next = Mathf.Clamp(Room + direction, 0, 2);
            if (next == Room) return;
            m_SlideFrom = m_Camera; m_SlideAge = Animate ? 0 : RoomSlideSeconds;
            Room = next; m_Cooldown = Animate ? 60 : 0; m_Extrude = 0;
            foreach (var monitor in m_Monitors) { monitor.Pointed = false; monitor.Focused = false; }
            if (!Animate) m_Camera = Room * 1920;
            Paint();
        }
        private void Left() => MoveRoom(-1);
        private void Right() => MoveRoom(1);
        private void EnterEdge(int direction)
        {
            m_Edge = direction;
            MoveRoom(direction);
        }
        private void KeyDown(KeyDownEvent evt)
        {
            if (IsPreviewOpen)
            {
                if (evt.keyCode == KeyCode.Escape) { ClosePreview(); evt.StopImmediatePropagation(); }
                return;
            }
            if (evt.keyCode == KeyCode.LeftArrow) { Left(); evt.StopPropagation(); }
            else if (evt.keyCode == KeyCode.RightArrow) { Right(); evt.StopPropagation(); }
        }
        private void Detached(DetachFromPanelEvent evt)
        {
            if (evt.target != Root) return;
            m_Opened = null; m_Edge = 0; ClosePreview(false); ReleaseHomeCharacter();
        }
        private void Resize(GeometryChangedEvent evt)
        {
            float scale = Mathf.Min(Root.contentRect.width / 1920, Root.contentRect.height / 1080);
            m_Viewport.style.scale = new Scale(new Vector3(scale, scale, 1));
            m_Viewport.style.left = (Root.contentRect.width - 1920 * scale) * .5f;
            m_Viewport.style.top = (Root.contentRect.height - 1080 * scale) * .5f;
        }
        private void Tick()
        {
            double now = TerminalMotion.Now;
            float frames = Mathf.Clamp((float)(now - m_LastTime), 0, .1f) * 60; m_LastTime = now;
            if (!m_Visible || m_Disposed) return;
            for (var parent = Root; parent != null; parent = parent.parent)
                if (parent.resolvedStyle.display == DisplayStyle.None)
                {
                    if (IsOpening) { m_Opened = null; m_TabletState = 0; }
                    m_Edge = 0; ClosePreview(false); ReleaseHomeCharacter(); return;
                }
            if (IsPreviewOpen) return;
            bool animate = Animate;
            if (!animate)
            {
                m_Camera = Room * 1920; m_SlideAge = RoomSlideSeconds; m_Cooldown = 0;
                m_TabletState = IsOpening ? 100 : 0;
            }
            else
            {
                m_Cooldown = Mathf.Max(0, m_Cooldown - frames);
                if (m_Edge != 0) MoveRoom(m_Edge);
                m_SlideAge = Mathf.Min(RoomSlideSeconds, m_SlideAge + frames / 60);
                m_Camera = Mathf.Lerp(m_SlideFrom, Room * 1920, TerminalMotion.InOutCubic(m_SlideAge / RoomSlideSeconds));
                m_TabletState = Mathf.MoveTowards(m_TabletState, IsOpening ? 100 : 0, frames);
                m_Flicker += frames;
                if (m_Flicker >= 6)
                {
                    m_Flicker %= 6;
                    float alpha = .6f + (float)m_Random.NextDouble() * .2f;
                    foreach (var light in m_Lights) light.style.opacity = alpha;
                }
            }
            bool hover = false;
            foreach (var monitor in m_Monitors)
                hover |= monitor.Art.room == Room && (monitor.Pointed || monitor.Focused);
            m_Extrude = hover && m_TabletState == 0 ? Mathf.Min(30, m_Extrude + frames * 10) : m_Extrude * Mathf.Pow(.5f, frames);
            if (!animate) { m_Extrude = hover ? 30 : 0; foreach (var light in m_Lights) light.style.opacity = .7f; }
            Paint();
            if (IsOpening && m_TabletState >= 100)
            {
                var opened = m_Opened; m_Opened = null; opened();
            }
        }
        private void Paint()
        {
            m_World.style.translate = new Translate(-m_Camera, 0);
            foreach (var monitor in m_Monitors)
            {
                bool hover = m_TabletState == 0 && monitor.Art.room == Room && (monitor.Pointed || monitor.Focused);
                monitor.Image.image = hover ? monitor.Hover : monitor.Normal;
                monitor.Image.style.translate = new Translate(hover ? m_Extrude * monitor.Art.hoverOffset / 30 : 0, 0);
            }
            float scale = m_TabletState >= 80 ? 1.5f : m_TabletState >= 40 ? m_TabletState / 80 * 1.5f : .75f;
            float y = m_TabletState >= 80 ? 0 : m_TabletState >= 40 ? 540 * (1 - m_TabletState / 80) :
                m_TabletState >= 15 ? 270 : 270 + (15 - m_TabletState) * 85;
            m_Tablet.style.scale = new Scale(new Vector3(scale, scale, 1)); m_Tablet.style.translate = new Translate(0, y);
            UpdateInput();
            UpdateHomeCharacter();
        }
        private void UpdateInput()
        {
            bool ready = m_TabletState == 0 && !IsOpening && !IsPreviewOpen;
            m_Left.style.display = Room > 0 && ready ? DisplayStyle.Flex : DisplayStyle.None;
            m_Right.style.display = Room < 2 && ready ? DisplayStyle.Flex : DisplayStyle.None;
            foreach (var monitor in m_Monitors)
            {
                monitor.Button.style.display = monitor.Art.room == Room && ready ? DisplayStyle.Flex : DisplayStyle.None;
                monitor.Button.SetEnabled(!m_Busy && !IsMoving);
            }
            UpdateSubjectInput(ready && Room == 1 && !m_Busy && !IsMoving);
        }
        public void Dispose()
        {
            if (m_Disposed) return;
            m_Disposed = true; m_Opened = null; m_Timer.Pause();
            ClosePreview(false); ReleaseHomeCharacter();
            Root.UnregisterCallback<GeometryChangedEvent>(Resize); Root.UnregisterCallback<KeyDownEvent>(KeyDown);
            Root.UnregisterCallback<DetachFromPanelEvent>(Detached); Root.RemoveFromHierarchy();
        }
    }
}
