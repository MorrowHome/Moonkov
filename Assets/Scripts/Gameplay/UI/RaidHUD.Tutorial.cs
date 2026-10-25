using System.Collections.Generic;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.UIElements;
using Unity.MP_FPS.Campaign;

namespace Unity.MP_FPS
{
    public sealed partial class RaidHUD
    {
        private int m_TutorialRaid, m_TutorialObserved, m_TutorialPersisted;
        private Vector3 m_TutorialStart;
        private float m_TutorialYaw;
        private uint m_TutorialFirstShot;
        private bool m_TutorialWriting, m_TutorialConfigured, m_TutorialModeKnown;
        private readonly Queue<string> m_ChapterRadioQueue = new Queue<string>();
        private Label m_ChapterRadio;
        private float m_ChapterRadioRemaining;
        private string m_ChapterRadioCurrent;
        private Button m_TutorialSkip, m_RadioSkip;
        private Label m_TutorialShortcut;

        private void InitializeTutorialHUD()
        {
            m_TutorialShortcut = m_Root.Q<Label>("campaignJournalShortcut");
            m_TutorialRaid = 0; m_TutorialWriting = m_TutorialConfigured = m_TutorialModeKnown = false; m_ChapterRadioQueue.Clear();
            m_ChapterRadioRemaining = 0; m_ChapterRadioCurrent = null;
            m_ChapterRadio = new Label(); m_ChapterRadio.AddToClassList("campaign-hud-hint");
            m_ChapterRadio.pickingMode = PickingMode.Ignore; m_CampaignHud.Add(m_ChapterRadio);
            var journal = m_Root.Q("campaignJournalHost");
            m_RadioSkip = new Button(() => { m_ChapterRadioRemaining = 0; m_ChapterRadioCurrent = null; }) { text = "跳过当前通讯" };
            m_TutorialSkip = new Button(SkipTutorial) { text = "跳过操作教学" };
            journal.Add(m_RadioSkip); journal.Add(m_TutorialSkip);
        }
        private async void SkipTutorial()
        {
            m_TutorialSkip.SetEnabled(false);
            try { await PlayerProfileClient.UpdateTutorialAsync(skip: true); }
            catch (System.Exception e) { MissionNotice(e.Message); }
            finally { m_TutorialSkip?.SetEnabled(true); }
        }
        private void UpdateTutorialHUD(bool live)
        {
            if (!m_TutorialModeKnown)
            {
                m_TutorialConfigured = TutorialRuntime.Enabled(m_World.EntityManager); m_TutorialModeKnown = true;
            }
            // Return through the ship so the next expedition captures fresh encounter settings and kit.
            m_Deploy.style.display = m_TutorialConfigured ? DisplayStyle.None : DisplayStyle.Flex;
            TutorialRuntime.UpdateDevice();
            var state = PlayerProfileClient.Tutorial;
            bool active = state != null && state.Status == TutorialStatus.Active;
            m_Root.EnableInClassList("campaign-tutorial", active);
            m_TutorialSkip.style.display = active ? DisplayStyle.Flex : DisplayStyle.None;
            m_TutorialShortcut.text = TutorialRuntime.UsingGamepad ? "方向键上  任务与通讯" : "J  任务与通讯";
            bool visible = live && !m_CampaignHud.ClassListContains("campaign-hidden");
            if (visible)
            {
                if (m_ChapterRadioCurrent == null && m_ChapterRadioQueue.Count > 0)
                {
                    m_ChapterRadioCurrent = m_ChapterRadioQueue.Dequeue();
                    m_ChapterRadioRemaining = Mathf.Clamp(m_ChapterRadioCurrent.Length * .16f, 6, 14);
                }
                if (m_ChapterRadioCurrent != null)
                {
                    m_ChapterRadioRemaining -= Time.unscaledDeltaTime;
                    if (m_ChapterRadioRemaining <= 0) m_ChapterRadioCurrent = null;
                }
            }
            m_ChapterRadio.text = m_ChapterRadioCurrent ?? "";
            m_ChapterRadio.style.display = visible && m_ChapterRadioCurrent != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (!active || !live || !m_PlayerQuery.HasSingleton<LocalTransform>()) return;
            var player = m_PlayerQuery.GetSingleton<PredictedPlayerGhost>();
            Vector3 position = m_PlayerQuery.GetSingleton<LocalTransform>().Position;
            if (m_TutorialRaid != m_Snapshot.RaidId)
            {
                m_TutorialRaid = m_Snapshot.RaidId; m_TutorialStart = position;
                m_TutorialYaw = player.ControllerState.YawDegrees; m_TutorialFirstShot = player.LastShotTick;
                m_TutorialObserved = m_TutorialPersisted = 0;
            }
            if (Vector3.Distance(position, m_TutorialStart) > 3) m_TutorialObserved |= TutorialRules.Moved;
            if (Mathf.Abs(Mathf.DeltaAngle(m_TutorialYaw, player.ControllerState.YawDegrees)) > 20) m_TutorialObserved |= TutorialRules.Looked;
            if (TutorialRuntime.Aiming) m_TutorialObserved |= TutorialRules.Aimed;
            if (player.LastShotTick != 0 && player.LastShotTick != m_TutorialFirstShot) m_TutorialObserved |= TutorialRules.Fired;
            if (!m_TutorialWriting && m_TutorialObserved != m_TutorialPersisted) PersistTutorialObservation();
            if (!visible || Time.unscaledTime < m_MissionNoticeUntil) return;
            string hint = null;
            if (player.CurrentHealth < player.MaxHealth * .7f)
                hint = TutorialRuntime.HealthHint;
            else if ((m_TutorialObserved & (TutorialRules.Moved | TutorialRules.Looked)) != (TutorialRules.Moved | TutorialRules.Looked))
                hint = TutorialRuntime.MoveHint + "，留意撤离方向";
            else if (m_CampaignProgress != null && CampaignRules.NodeCompleted(m_CampaignProgress, "CM01", 1))
            {
                if (player.CurrentAmmo == 0) hint = TutorialRuntime.ReloadHint;
                else if ((m_TutorialObserved & TutorialRules.Fired) == 0) hint = TutorialRuntime.AimHint + "。不必清空整栋建筑";
            }
            if (hint != null) m_CampaignHint.text = hint;
        }
        private async void PersistTutorialObservation()
        {
            m_TutorialWriting = true;
            int observed = m_TutorialObserved, raid = m_TutorialRaid;
            try
            {
                await PlayerProfileClient.UpdateTutorialAsync(steps: observed);
                if (raid == m_TutorialRaid) m_TutorialPersisted = observed;
            }
            catch (System.Exception e) { MissionNotice("教学记录暂未保存：" + e.Message); }
            finally { m_TutorialWriting = false; }
        }
    }
}
