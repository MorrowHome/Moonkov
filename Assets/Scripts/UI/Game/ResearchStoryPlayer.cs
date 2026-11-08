using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using static Unity.MP_FPS.Client.ResearchQuestCopy;

namespace Unity.MP_FPS.Client
{
    internal static class ResearchSceneLibrary
    {
        [Serializable]
        internal sealed class Scene
        {
            public int id;
            public string titleZh, titleEn;
            public Shot[] frames;
            internal string Title => Text(titleZh, titleEn);
        }

        [Serializable]
        internal sealed class Shot
        {
            public string titleZh, titleEn, actionZh, actionEn, art, speakerZh, speakerEn, visual;
            public string[] cues, evidenceZh, evidenceEn;
            public bool fit;
        }

        [Serializable]
        private sealed class Library
        {
            public Scene[] chapters, endings;
        }

        private static Library s_Library;
        private static Library Data => s_Library ??= JsonUtility.FromJson<Library>(
            Resources.Load<TextAsset>("Moonkov/ResearchScenes").text);

        internal static Scene Chapter(ResearchStoryChapter chapter) => Data.chapters[(int)chapter];
        internal static Scene Epilogue(ResearchEnding ending) => Data.endings[(int)ending - 1];
    }

    internal sealed class ResearchStoryPlayer
    {
        private readonly ResearchSceneLibrary.Scene m_Scene;
        private readonly ResearchQuestRadio m_Radio;
        private readonly Func<Task<bool>> m_Finish;
        private readonly VisualElement m_Evidence, m_Trace;
        private readonly Image m_Art;
        private readonly Label m_Title, m_Count, m_Speaker, m_Error;
        private readonly Button m_Play, m_Prev, m_Next;
        private int m_Index;
        private bool m_Chinese;
        internal bool Saving { get; private set; }
        internal VisualElement Element { get; }

        internal ResearchStoryPlayer(VisualElement host, ResearchSceneLibrary.Scene scene, ResearchQuestRadio radio, Func<Task<bool>> finish)
        {
            m_Scene = scene;
            m_Radio = radio;
            m_Finish = finish;
            Element = Add(host, "research-story-player");
            Element.name = "researchStoryPlayer";
            var head = Add(Element, "research-story-shot-header");
            m_Title = AddLabel(head, "research-story-title", "questStoryShot");
            m_Count = AddLabel(head, "research-story-meta");
            var shot = Add(Element, "research-story-shot");
            m_Art = new Image { pickingMode = PickingMode.Ignore };
            m_Art.AddToClassList("research-story-art");
            shot.Add(m_Art);
            var archive = Add(shot, "research-story-archive");
            m_Speaker = AddLabel(archive, "research-story-speaker", "questStorySpeaker");
            m_Trace = Add(archive, "research-story-trace");
            m_Trace.pickingMode = PickingMode.Ignore;
            m_Trace.generateVisualContent += DrawTrace;
            m_Evidence = Add(archive, "research-story-evidence");
            var actions = Add(Element, "research-story-actions");
            m_Prev = AddButton(actions, "research-quest-nav", "questStoryPrevious", () => Show(m_Index - 1, true));
            m_Play = AddButton(actions, "research-quest-nav", "questStoryListen", ToggleAudio);
            m_Next = AddButton(actions, "research-quest-primary", "questStoryNext", Next);
            m_Next.AddToClassList("research-story-next");
            m_Error = AddLabel(Element, "research-quest-feedback", "questStoryError");
            Show(0, true);
        }

        private ResearchSceneLibrary.Shot Current => m_Scene.frames[m_Index];

        private void Show(int index, bool play)
        {
            if (index < 0 || index >= m_Scene.frames.Length) return;
            m_Index = index;
            m_Chinese = Chinese;
            var frame = Current;
            m_Title.text = Text(frame.titleZh, frame.titleEn);
            m_Count.text = $"{index + 1:00} / {m_Scene.frames.Length:00}";
            m_Speaker.text = Text(frame.speakerZh, frame.speakerEn);
            m_Art.image = Resources.Load<Texture2D>("Moonkov/" + frame.art);
            m_Art.scaleMode = frame.fit ? ScaleMode.ScaleToFit : ScaleMode.ScaleAndCrop;
            m_Art.tintColor = frame.fit ? Color.white : new Color32(146, 151, 163, 255);
            m_Trace.style.display = frame.visual == "record" ? DisplayStyle.None : DisplayStyle.Flex;
            m_Trace.MarkDirtyRepaint();
            m_Evidence.Clear();
            foreach (string evidence in m_Chinese ? frame.evidenceZh : frame.evidenceEn)
                AddLabel(m_Evidence, "research-story-readout").text = evidence;
            m_Prev.text = Text("上一段", "Previous");
            m_Prev.SetEnabled(index > 0);
            m_Next.text = Text(frame.actionZh, frame.actionEn);
            m_Error.style.display = DisplayStyle.None;
            if (play) m_Radio.PlayStory(frame.cues, m_Chinese);
            RefreshAudio();
            if (play) m_Next.Focus();
        }

        internal void RefreshAudio()
        {
            if (m_Chinese != Chinese) Show(m_Index, false);
            m_Play.style.display = Current.cues.Length == 0 ? DisplayStyle.None : DisplayStyle.Flex;
            m_Play.text = m_Radio.Playing ? Text("停止语音", "Stop audio") : Text("重听", "Replay");
        }

        private void ToggleAudio()
        {
            if (m_Radio.Playing) m_Radio.Stop();
            else m_Radio.PlayStory(Current.cues, Chinese);
            RefreshAudio();
        }

        private async void Next()
        {
            if (Saving) return;
            if (m_Index + 1 < m_Scene.frames.Length) { Show(m_Index + 1, true); return; }
            Saving = true;
            Element.SetEnabled(false);
            m_Next.text = Text("正在保存", "Saving");
            bool ok;
            try { ok = await m_Finish(); }
            catch (Exception) { ok = false; }
            finally { Saving = false; }
            if (Element.panel == null) return;
            Element.SetEnabled(true);
            m_Next.text = Text(Current.actionZh, Current.actionEn);
            if (!ok)
            {
                m_Error.text = Text("剧情进度未保存，请重试。", "Could not save story progress. Try again.");
                m_Error.style.display = DisplayStyle.Flex;
                m_Next.Focus();
            }
        }

        private void DrawTrace(MeshGenerationContext context)
        {
            var rect = context.visualElement.contentRect;
            if (rect.width < 12 || rect.height < 12) return;
            var painter = context.painter2D;
            float w = rect.width, h = rect.height;
            Vector2 Point(float x, float y) => new Vector2(x * w, y * h);
            void Line(Vector2 a, Vector2 b)
            {
                painter.MoveTo(a);
                painter.LineTo(b);
            }

            painter.strokeColor = new Color32(54, 58, 63, 255);
            painter.lineWidth = 1;
            painter.BeginPath();
            for (int i = 1; i < 8; i++) Line(Point(i / 8f, 0), Point(i / 8f, 1));
            for (int i = 1; i < 4; i++) Line(Point(0, i / 4f), Point(1, i / 4f));
            painter.Stroke();

            var accent = new Color32(105, 136, 245, 255);
            painter.lineWidth = 2;
            if (Current.visual is "wave" or "signal" or "offline")
            {
                int traces = Current.visual == "wave" ? 2 : 1;
                for (int trace = 0; trace < traces; trace++)
                {
                    painter.strokeColor = trace == 0 ? accent : new Color32(185, 193, 210, 255);
                    painter.BeginPath();
                    for (int i = 0; i <= 80; i++)
                    {
                        float t = i / 80f;
                        float y = .5f + (.15f + .11f * Mathf.Sin(t * 11f)) * Mathf.Sin(t * 39f) + trace * .07f;
                        if (Current.visual == "offline") y = Mathf.Lerp(y, .5f, Mathf.SmoothStep(0, 1, t * 2.5f));
                        if (i == 0) painter.MoveTo(Point(t, y));
                        else painter.LineTo(Point(t, y));
                    }
                    painter.Stroke();
                }
                return;
            }

            painter.strokeColor = accent;
            var a = Point(.12f, .5f);
            var b = Point(.88f, .5f);
            painter.BeginPath();
            if (Current.visual == "route")
            {
                var mid = Point(.48f, .28f);
                painter.MoveTo(a);
                painter.LineTo(Point(.28f, .5f));
                painter.LineTo(mid);
                painter.LineTo(Point(.67f, .28f));
                painter.LineTo(Point(.79f, .5f));
                painter.LineTo(b);
            }
            else Line(a, b);
            Line(b, Point(.84f, .37f));
            Line(b, Point(.84f, .63f));
            painter.Stroke();
            painter.strokeColor = new Color32(185, 193, 210, 255);
            painter.BeginPath();
            Line(Point(.12f, .35f), Point(.12f, .65f));
            if (Current.visual == "timeline") Line(Point(.62f, .35f), Point(.62f, .65f));
            painter.Stroke();
        }

        private static VisualElement Add(VisualElement host, string style)
        {
            var element = new VisualElement(); element.AddToClassList(style); host.Add(element); return element;
        }

        private static Label AddLabel(VisualElement host, string style, string name = null)
        {
            var label = new Label { name = name, pickingMode = PickingMode.Ignore };
            label.AddToClassList(style); host.Add(label); return label;
        }

        private static Button AddButton(VisualElement host, string style, string name, Action action)
        {
            var button = new Button(action) { name = name }; button.AddToClassList(style); host.Add(button); return button;
        }
    }
}
