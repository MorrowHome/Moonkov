using System;
using UnityEngine;
using UnityEngine.UIElements;
using Unity.MP_FPS.Client;
using static Unity.MP_FPS.Client.ResearchQuestCopy;

namespace Unity.MP_FPS
{
    public sealed partial class RaidHUD
    {
        private static readonly ResearchDevice[] s_ResearchDevices = (ResearchDevice[])Enum.GetValues(typeof(ResearchDevice));
        private VisualElement m_ResearchList, m_ResearchPrompt;
        private Label m_ResearchPromptText;
        private readonly Label[] m_ResearchLines = new Label[ResearchQuestRules.Count];
        private ResearchDevice? m_ResearchHold;
        private readonly ResearchQuestRadio m_ResearchRadio = new();
        private int m_LastNadirRaid = -1;
        private float m_ResearchNextRefresh;
        private bool m_ResearchInputPriority;

        private void InitializeResearchQuests()
        {
            var style = Resources.Load<StyleSheet>("Moonkov/UI/ResearchQuests");
            if (style && !m_Root.styleSheets.Contains(style)) m_Root.styleSheets.Add(style);
            m_ResearchList = new VisualElement { pickingMode = PickingMode.Ignore };
            m_ResearchList.AddToClassList("research-quest-raid-list");
            m_ResearchList.AddToClassList("moonkov-style-font");
            m_ResearchList.style.display = DisplayStyle.None;
            m_Root.Q<ScrollView>("raidRightRail")?.Add(m_ResearchList);
            for (int i = 0; i < m_ResearchLines.Length; i++)
            {
                var label = new Label { pickingMode = PickingMode.Ignore };
                label.AddToClassList("research-quest-raid-line");
                m_ResearchLines[i] = label;
                m_ResearchList.Add(label);
            }
            m_ResearchPrompt = new VisualElement { pickingMode = PickingMode.Ignore };
            m_ResearchPrompt.AddToClassList("research-quest-interaction");
            m_ResearchPrompt.AddToClassList("moonkov-style-font");
            m_ResearchPrompt.style.display = DisplayStyle.None;
            m_ResearchPromptText = new Label { pickingMode = PickingMode.Ignore };
            m_ResearchPromptText.AddToClassList("research-quest-raid-line");
            m_ResearchPrompt.Add(m_ResearchPromptText);
            m_Root.Add(m_ResearchPrompt);
            m_ResearchNextRefresh = 0;
            m_ResearchHold = null;
        }

        private bool UpdateResearchQuests(bool live, bool canInteract, Vector3 position)
        {
            m_ResearchInputPriority = false;
            if (m_ResearchList == null) return false;
            int raid = m_Snapshot.RaidId;
            if (!live || !PlayerProfileClient.IsOffline || !ResearchQuestRuntime.TryRead(raid, out var quest))
            {
                m_ResearchList.style.display = DisplayStyle.None;
                m_ResearchPrompt.style.display = DisplayStyle.None;
                ReleaseResearchHold();
                m_ResearchRadio.Stop();
                return false;
            }
            if (quest.HeardNadir && m_LastNadirRaid != raid)
            {
                m_LastNadirRaid = raid;
                m_ResearchRadio.PlayStory("nadir", Chinese);
            }
            m_ResearchRadio.Update();
            m_ResearchList.style.display = DisplayStyle.Flex;
            if (Time.unscaledTime >= m_ResearchNextRefresh)
            {
                m_ResearchNextRefresh = Time.unscaledTime + .16f;
                for (int i = 0; i < m_ResearchLines.Length; i++)
                {
                    var id = ResearchQuestRules.At(i);
                    var label = m_ResearchLines[i];
                    bool active = ResearchQuestRules.Has(quest.Active, id);
                    label.style.display = active ? DisplayStyle.Flex : DisplayStyle.None;
                    if (active) MoonkovLocalization.Set(label, For(id).Title + "   " + FieldProgress(id, quest));
                }
            }

            bool held = canInteract && m_InteractionBindings.Operate.IsPressed();
            if (!held) ReleaseResearchHold();
            ResearchDevice? target = null;
            float best = m_WorldFocusScore;
            if (canInteract)
                foreach (var id in s_ResearchDevices)
                {
                    if (!ResearchQuestRuntime.NeedsDevice(raid, id)) continue;
                    var anchor = ResearchQuestRuntime.Anchor(id);
                    if (anchor == null) continue;
                    float score = RaidInteractionAim.Score(Camera.main, position, ResearchQuestRuntime.InteractionPoint(id, anchor), 3,
                        ResearchQuestRuntime.AimSurface(raid, id, anchor));
                    if (score >= 0 && score >= best) { best = score; target = id; }
                }
            if (!m_ResearchHold.HasValue && target.HasValue && held && m_InteractionBindings.Operate.WasPressedThisFrame())
                m_ResearchHold = target;
            if (m_ResearchHold.HasValue)
            {
                if (target == m_ResearchHold) ResearchQuestRuntime.Hold(raid, target.Value, true);
                else ReleaseResearchHold();
            }
            string notice = quest.Notice switch
            {
                "interrupted" => Text("操作中断", "Transfer interrupted"),
                "space" => Text("背包空间不足", "Not enough inventory space"),
                "extract" => Text("带着道具撤离", "Extract with the item"),
                "lower-open" => Text("隔离解除。寻找记录和凭证", "Isolation released. Find the records and credentials."),
                "credentials" => Text("记录已取得。寻找门禁凭证", "Records recovered. Find the access credentials."),
                "nadir-records" => Text("凭证已取得。寻找奈迪尔的记录", "Credentials recovered. Find Nadir's records."),
                "control-open" => Text("凭证已验证。回收档案并关闭实验", "Access granted. Recover the archive and disable the experiments."),
                "shutdown" => Text("档案已取得。关闭实验系统", "Archive secured. Disable the experiments."),
                "archive" => Text("实验系统已关闭。回收原始档案", "Experiments disabled. Recover the original archive."),
                "discharge" => Text("能量放电。可用光环护盾抵挡", "Energy discharge. Use your Halo shield."),
                _ => null
            };
            string prompt = target.HasValue ? m_InteractionBindings.Label(m_InteractionBindings.Operate) + "   " + DeviceAction(target.Value)
                + (quest.CanOperate && quest.Target == target.Value ? $"   {Mathf.FloorToInt(quest.InteractionProgress * 100)}%" : "")
                : notice;
            m_ResearchPrompt.style.display = canInteract && prompt != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (prompt != null && m_ResearchPromptText.text != prompt) MoonkovLocalization.Set(m_ResearchPromptText, prompt);
            return m_ResearchInputPriority = target.HasValue || m_ResearchHold.HasValue;
        }

        private void ReleaseResearchHold()
        {
            if (!m_ResearchHold.HasValue) return;
            ResearchQuestRuntime.Hold(m_Snapshot.RaidId, m_ResearchHold.Value, false);
            m_ResearchHold = null;
        }

        private static string DeviceAction(ResearchDevice device) => device switch
        {
            ResearchDevice.Tablet => Text("拿取研究员平板", "Take personnel tablet"),
            ResearchDevice.Administrator => Text("读取管理员日志", "Copy administrator logs"),
            ResearchDevice.Server => Text("拷贝研究数据库", "Copy research database"),
            ResearchDevice.LowerSeal => Text("解除 L21 研究区封锁", "Release L21 isolation"),
            ResearchDevice.NadirTerminal => Text("回收奈迪尔的记录", "Recover Nadir's records"),
            ResearchDevice.CredentialsLocker => Text("拿取 LUCENT 门禁凭证", "Take LUCENT credentials"),
            ResearchDevice.ControlAccess => Text("验证凭证并解锁控制室", "Authenticate at LUCENT control"),
            ResearchDevice.OriginalArchive => Text("回收原始档案", "Secure the original archive"),
            ResearchDevice.Shutdown => Text("关闭实验系统", "Disable experimental systems"),
            _ => ""
        };

        private static string FieldProgress(ResearchQuest id, ResearchQuestRuntime.Presentation view)
        {
            if (ResearchQuestRules.Has(view.Recovered, id)) return Text("带回后交付", "Extract to turn in");
            return id switch
            {
                ResearchQuest.Exposure when view.Seconds == ResearchQuestRules.ExposureSeconds => Text("测试完成，前往撤离", "Assessment complete. Extract."),
                ResearchQuest.Exposure => $"{view.Seconds / 60:00}:{view.Seconds % 60:00} / 05:00",
                ResearchQuest.HaloTrial when view.HaloKills == ResearchQuestRules.HaloKillGoal => Text("击杀达标，前往撤离", "Kill quota met. Extract."),
                ResearchQuest.HaloTrial => $"{view.HaloKills} / {ResearchQuestRules.HaloKillGoal}",
                ResearchQuest.Biology when !ResearchQuestRules.Has(view.Issued, id) && view.Samples < ResearchQuestRules.SampleGoal =>
                    Text("取回配发的采样套件", "Recover the issued sampling kit"),
                ResearchQuest.Biology => $"{Mathf.Min(view.Samples, ResearchQuestRules.SampleGoal)} / {ResearchQuestRules.SampleGoal}  " +
                    (view.Samples >= ResearchQuestRules.SampleGoal ? Text("带回后交付", "Extract to turn in") : Text("封存样本", "samples")),
                ResearchQuest.Tablet => view.TabletAvailable ? Text("MC00 L22 隔离柜", "MC00 L22 isolation cabinet") : Text("取回平板", "Recover the tablet"),
                ResearchQuest.FlashDrive => ResearchQuestRules.Has(view.Issued, id) ? Text("MC00 L22 管理工作站", "MC00 L22 administrator workstation") :
                    Text("取回配发的 U 盘", "Recover the issued flash drive"),
                ResearchQuest.HardDrive => ResearchQuestRules.Has(view.Issued, id) ? Text("MC00 L22 研究服务器", "MC00 L22 research server") :
                    Text("取回配发的硬盘", "Recover the issued hard drive"),
                ResearchQuest.HaloStorage when view.PackageRemoved => Text("封存包已取出。下次出战重试", "Package removed. Retry on the next raid."),
                ResearchQuest.HaloStorage => ResearchQuestRules.Has(view.Issued, id) ? Text("切换收纳形态并装入封存包", "Open Halo storage and load the package") :
                    Text("取回封存包", "Recover the research package"),
                ResearchQuest.NoWayBack => !view.LowerOpened ? Text("MC00 L21 研究区隔离开关", "MC00 L21 isolation control") :
                    !view.RecordsHeld && !view.CredentialsHeld ? Text("回收奈迪尔的记录和门禁凭证", "Recover Nadir's records and the access credentials") :
                    !view.RecordsHeld ? Text("带上奈迪尔的记录", "Carry Nadir's records") : Text("带上门禁凭证", "Carry the access credentials"),
                ResearchQuest.EndOfTransmission => !view.ControlOpened
                    ? ResearchQuestRules.Has(view.Issued, id) ? Text("MC00 L22 LUCENT 控制室", "MC00 L22 LUCENT control room") :
                        Text("取回 LUCENT 门禁凭证", "Recover the LUCENT access credentials")
                    : !view.ArchiveHeld && !view.SystemsDisabled ? Text("回收原始档案并关闭实验", "Secure the archive and disable the experiments") :
                        !view.ArchiveHeld ? Text("带上 LUCENT 原始档案", "Carry the original LUCENT archive") : Text("关闭实验系统", "Disable experimental systems"),
                ResearchQuest.Sentry when view.SentryKills == ResearchQuestRules.SentryKillGoal => Text("炮塔击杀达标，前往撤离", "Sentry quota met. Extract."),
                _ => $"{view.SentryKills} / {ResearchQuestRules.SentryKillGoal}"
            };
        }
    }
}
