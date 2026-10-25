using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Unity.MP_FPS.Campaign;

namespace Unity.MP_FPS.EditorTools
{
    // A native, read-only fixture. No Worlds, PlayerProfileClient or player files are changed.
    public sealed class CampaignUIReview : EditorWindow
    {
        private static CampaignUIReview s_Window;
        private static readonly string[] StateNames = { "初始", "备用供电", "携物待撤离", "第一章完成" };
        private CampaignProgress m_Progress;
        private IDisposable m_View;
        private VisualElement m_ViewElement;
        private int m_State;
        private bool m_Busy;

        [MenuItem("Tools/Moonkov/Review First Chapter UI")]
        private static void Menu() => Run();

        public static string Run() => Show(0, 1280, 720);

        public static string Show(int state, int width, int height, bool busy = false)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play mode before opening this read-only UI review.");
            if (state < 0 || state >= StateNames.Length) throw new ArgumentOutOfRangeException(nameof(state));
            if (width < 800 || height < 500 || width > 1920 || height > 1200)
                throw new ArgumentOutOfRangeException(nameof(width), "Use a review window between 800x500 and 1920x1200.");
            var window = s_Window ? s_Window : GetWindow<CampaignUIReview>();
            s_Window = window;
            window.titleContent = new GUIContent("第一章任务预览");
            window.minSize = new Vector2(800, 500);
            window.m_State = state; window.m_Busy = busy;
            window.position = new Rect(40, 40, width, height);
            ((EditorWindow)window).Show();
            window.BuildView();
            return Status();
        }

        public static string Status()
        {
            if (!s_Window) return "CampaignUIReview closed";
            var root = s_Window.rootVisualElement;
            var bounds = root.worldBound;
            var viewBounds = s_Window.m_ViewElement?.worldBound ?? default;
            var track = s_Window.m_ViewElement?.Q<Button>("campaignTrack");
            return JsonUtility.ToJson(new ReviewStatus
            {
                State = StateNames[s_Window.m_State], Busy = s_Window.m_Busy,
                HasPanel = root.panel != null, Width = bounds.width, Height = bounds.height,
                ViewWidth = viewBounds.width, ViewHeight = viewBounds.height,
                DpiScale = EditorGUIUtility.pixelsPerPoint,
                TrackText = track?.text, TrackEnabled = track != null && track.enabledSelf,
                ReadOnly = true
            });
        }

        public static void CloseReview()
        { if (s_Window) s_Window.Close(); }

        public void CreateGUI()
        { s_Window = this; BuildView(); }

        private void BuildView()
        {
            m_View?.Dispose(); m_View = null; m_ViewElement = null;
            var root = rootVisualElement; root.Clear();
            root.AddToClassList("campaign-review");
            var style = Resources.Load<StyleSheet>("Moonkov/UI/CampaignChapterOne");
            if (style != null && !root.styleSheets.Contains(style)) root.styleSheets.Add(style);
            var toolbar = new VisualElement(); toolbar.AddToClassList("campaign-review-toolbar"); root.Add(toolbar);
            for (int i = 0; i < StateNames.Length; i++)
            {
                int selected = i;
                var button = new Button(() => { m_State = selected; BuildView(); }) { text = StateNames[i] };
                button.AddToClassList("campaign-button");
                if (selected == m_State) button.AddToClassList("campaign-primary"); toolbar.Add(button);
            }
            AddSize(toolbar, 1280, 720); AddSize(toolbar, 1600, 900);
            var busy = new Toggle("保存中"); busy.SetValueWithoutNotify(m_Busy);
            busy.RegisterValueChangedCallback(evt => { m_Busy = evt.newValue; BuildView(); }); toolbar.Add(busy);
            var note = new Label("只读预览。使用合成任务状态，不读取或写入玩家档案。");
            note.AddToClassList("campaign-review-note"); root.Add(note);
            var host = new VisualElement(); host.AddToClassList("campaign-review-host"); root.Add(host);
            m_Progress = BuildProgress(m_State);
            string error = CampaignRules.Validate(m_Progress);
            if (error != null) throw new InvalidOperationException("Review data invalid: " + error);
            var type = typeof(Client.MoonkovTerminal).Assembly.GetType("Unity.MP_FPS.Client.CampaignChapterView", true);
            var constructor = type.GetConstructor(new[] { typeof(VisualElement), typeof(Func<CampaignProgress>), typeof(Action), typeof(string) });
            if (constructor == null) throw new MissingMethodException(type.FullName, ".ctor");
            object view = constructor.Invoke(new object[] { host, new Func<CampaignProgress>(() => m_Progress), new Action(CloseReview), "关闭预览" });
            m_View = (IDisposable)view;
            var reviewMethod = type.GetMethod("SetReviewPresentation", BindingFlags.Public | BindingFlags.Instance);
            if (reviewMethod == null)
            {
                m_View.Dispose(); m_View = null; host.Clear();
                throw new MissingMethodException(type.FullName, "SetReviewPresentation");
            }
            try { reviewMethod.Invoke(view, new object[] { m_Busy }); }
            catch { m_View.Dispose(); m_View = null; host.Clear(); throw; }
            m_ViewElement = (VisualElement)type.GetProperty("Element", BindingFlags.Public | BindingFlags.Instance).GetValue(view);
            MoonkovLocalization.Bind(root);
        }

        private void AddSize(VisualElement toolbar, int width, int height)
        {
            var button = new Button(() => Show(m_State, width, height, m_Busy)) { text = width + " × " + height };
            button.AddToClassList("campaign-button"); toolbar.Add(button);
        }

        private static CampaignProgress BuildProgress(int state)
        {
            var progress = CampaignRules.Create();
            if (state >= 1)
            {
                progress.NodeMasks["CM01"] = 3;
                progress.NodeMasks["CM02"] = 1;
                progress.PrimaryTracked = "CM02";
            }
            if (state >= 2)
            {
                progress.NodeMasks["CM02"] = 3;
                progress.NodeMasks["CM03"] = 7;
                progress.NodeItemIds["CM03/recover"] = "00000000-0000-4000-8000-000000000003";
                progress.PrimaryTracked = "CM03";
            }
            if (state == 3)
            {
                progress.NodeMasks["CM01"] = 7;
                progress.NodeMasks["CM02"] = 7;
                progress.NodeMasks["CM03"] = 15;
                progress.CompletedTasks.UnionWith(new[] { "CM01", "CM02", "CM03" });
                progress.PrimaryTracked = null;
            }
            progress.Revision = state;
            return progress;
        }

        private void OnDisable()
        {
            m_View?.Dispose(); m_View = null; m_ViewElement = null;
            if (s_Window == this) s_Window = null;
        }

        [Serializable]
        private sealed class ReviewStatus
        {
            public string State, TrackText;
            public bool Busy, HasPanel, TrackEnabled, ReadOnly;
            public float Width, Height, ViewWidth, ViewHeight, DpiScale;
        }
    }
}
