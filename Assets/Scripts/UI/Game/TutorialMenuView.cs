using System;
using UnityEngine;
using UnityEngine.UIElements;
using Unity.MP_FPS.Campaign;

namespace Unity.MP_FPS.Client
{
    // One persistent card above the existing terminal; no replacement inventory or navigation.
    public sealed class TutorialMenuView : IDisposable
    {
        private readonly VisualElement m_Root, m_Card;
        private readonly Label m_Title, m_Copy;
        private readonly Button m_Next, m_Skip, m_Supply, m_Return;
        private readonly Action<int> m_Navigate;
        private readonly IVisualElementScheduledItem m_Timer;
        private bool m_Busy, m_Inline, m_Observing;
        private string m_Message;
        private int m_LastPage = -1;
        private TutorialStatus m_LastStatus;
        public TutorialMenuView(VisualElement root, Action<int> navigate)
        {
            m_Root = root; m_Navigate = navigate;
            var style = Resources.Load<StyleSheet>("Moonkov/UI/Tutorial"); if (style) root.styleSheets.Add(style);
            m_Card = new VisualElement { name = "tutorialMenu" }; m_Card.AddToClassList("tutorial-card");
            m_Title = new Label(); m_Title.AddToClassList("tutorial-title");
            m_Copy = new Label(); m_Copy.AddToClassList("tutorial-copy");
            var actions = new VisualElement(); actions.AddToClassList("tutorial-actions");
            m_Next = new Button(Next); m_Skip = new Button(Skip) { text = "跳过教学" };
            m_Supply = new Button(Supply) { text = "补领教学装备" };
            m_Return = new Button(Return) { text = "结束练习" };
            actions.Add(m_Next); actions.Add(m_Supply); actions.Add(m_Skip); actions.Add(m_Return);
            m_Card.Add(m_Title); m_Card.Add(m_Copy); m_Card.Add(actions); root.Add(m_Card);
            m_Timer = root.schedule.Execute(Refresh).Every(250); Refresh();
        }
        private void Refresh()
        {
            var state = PlayerProfileClient.Tutorial;
            bool visible = state != null && (state.Status == TutorialStatus.Active || PlayerProfileClient.IsTutorialPractice) &&
                !PlayerProfileClient.RaidActive && GameSettings.Instance.GameState == GlobalGameState.MainMenu;
            m_Card.EnableInClassList("tutorial-off", !visible);
            if (!visible || m_Busy || m_Root.resolvedStyle.display == DisplayStyle.None) return;
            bool active = state.Status == TutorialStatus.Active;
            bool intro = active && state.IntroPage < TutorialRules.Introduction.Length;
            bool inline = !intro;
            if (inline != m_Inline)
            {
                m_Inline = inline; m_Card.EnableInClassList("tutorial-inline", inline);
                var gear = m_Root.Q<VisualElement>(className: "stash-body");
                if (inline && gear != null) m_Root.Insert(m_Root.IndexOf(gear), m_Card);
                else m_Root.Add(m_Card);
            }
            int prep = TutorialRules.Preparation(PlayerProfileClient.Inventory);
            if (!intro && active && !m_Observing && (state.CompletedSteps & prep) != prep) ObservePreparation(prep);
            m_Title.text = PlayerProfileClient.IsTutorialPractice ? "教学练习" : "第一次月面行动";
            m_Copy.text = m_Message ?? (intro ? TutorialRules.Introduction[state.IntroPage] : !active ? "本次练习已结束。可以返回正式档案。" :
                (prep & TutorialRules.Equipped) == 0 ? "装备一件光环。打开仓库，把武器拖入对应装备栏。" :
                (prep & TutorialRules.Cells) == 0 ? "带上电池。放入口袋或胸挂，战斗时才能取用。" :
                (prep & TutorialRules.Medicine) == 0 ? "带上医疗注射器。放入口袋或胸挂，为回程留出余量。" :
                "沈禾：装备齐了。前往 MC00 入口登记，记住撤离点的方向。样本只有成功带回才算入库。");
            m_Next.text = intro ? "继续" : prep == 7 ? "准备出战" : "打开仓库";
            m_Next.style.display = active ? DisplayStyle.Flex : DisplayStyle.None;
            m_Skip.style.display = active ? DisplayStyle.Flex : DisplayStyle.None;
            m_Supply.style.display = active && !intro ? DisplayStyle.Flex : DisplayStyle.None;
            m_Return.style.display = PlayerProfileClient.IsTutorialPractice ? DisplayStyle.Flex : DisplayStyle.None;
            if (intro && (state.IntroPage != m_LastPage || state.Status != m_LastStatus)) m_Next.Focus();
            m_LastPage = state.IntroPage; m_LastStatus = state.Status;
            MoonkovLocalization.Bind(m_Card);
        }
        private async void ObservePreparation(int steps)
        {
            m_Observing = true;
            try { await PlayerProfileClient.UpdateTutorialAsync(steps: steps); }
            catch (Exception e) { m_Message = e.Message; }
            finally { m_Observing = false; }
        }
        private async void Next()
        {
            if (m_Busy) return;
            m_Message = null;
            await Run(async () =>
            {
                if (PlayerProfileClient.Tutorial.IntroPage < TutorialRules.Introduction.Length)
                    await PlayerProfileClient.UpdateTutorialAsync(nextIntro: true);
                else
                {
                    int prep = TutorialRules.Preparation(PlayerProfileClient.Inventory);
                    await PlayerProfileClient.UpdateTutorialAsync(steps: prep);
                    m_Navigate(prep == 7 ? 4 : 1);
                }
            });
        }
        private async void Skip() => await Run(() => PlayerProfileClient.UpdateTutorialAsync(skip: true));
        private async void Supply() => await Run(async () => m_Message = await PlayerProfileClient.ClaimTutorialSuppliesAsync());
        private void Return() { if (m_Busy) return; try { PlayerProfileClient.EndTutorialPractice(); } catch (Exception e) { m_Message = e.Message; } Refresh(); }
        private async System.Threading.Tasks.Task Run(Func<System.Threading.Tasks.Task> action)
        {
            if (m_Busy) return; m_Busy = true; m_Card.SetEnabled(false);
            try { await action(); } catch (Exception e) { m_Message = e.Message; }
            finally { m_Busy = false; m_Card.SetEnabled(true); Refresh(); }
        }
        public void Dispose() { m_Timer.Pause(); m_Card.RemoveFromHierarchy(); }
    }
}
