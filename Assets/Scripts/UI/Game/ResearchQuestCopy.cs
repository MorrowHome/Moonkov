using System;
using UnityEngine;

namespace Unity.MP_FPS.Client
{
    internal static class ResearchQuestCopy
    {
        [Serializable] private sealed class Story { public Locale zh, en; }
        [Serializable] private sealed class Locale
        {
            public QuestText[] quests;
            public EndingText[] endings;
            public string halo, haloClassified;
        }
        [Serializable] internal sealed class QuestText { public string title, goal, description, item; }
        [Serializable] internal sealed class EndingText { public string title, description, epilogue; }

        internal sealed class Entry
        {
            internal readonly string NodeEn, Art, Item, Photo;
            internal readonly ResearchQuest Quest;
            internal Entry(ResearchQuest quest, string node, string art = null, string item = null, string photo = null)
            {
                Quest = quest; NodeEn = node; Art = art; Item = item; Photo = photo ?? item;
            }

            private QuestText Copy => Current.quests[ResearchQuestRules.Number(Quest) - 1];
            internal string Title => Copy.title;
            internal string NodeTitle => Chinese ? Title : NodeEn;
            internal string Goal => Copy.goal;
            internal string Brief => Copy.description;
            internal string ItemDescription => Copy.item;
        }

        internal static bool Chinese => MoonkovLocalization.CurrentLocaleCode?.StartsWith("zh", StringComparison.OrdinalIgnoreCase) == true;
        internal static string Text(string zh, string en) => Chinese ? zh : en;
        private static Story s_Story;
        private static Locale Current
        {
            get
            {
                if (s_Story == null) s_Story = JsonUtility.FromJson<Story>(Resources.Load<TextAsset>("Moonkov/ResearchStory").text);
                return Chinese ? s_Story.zh : s_Story.en;
            }
        }

        private static readonly Entry[] Entries =
        {
            new Entry(ResearchQuest.Exposure, "Readiness", "PageArt/Base"),
            new Entry(ResearchQuest.HaloTrial, "Contact", "PageArt/Technical"),
            new Entry(ResearchQuest.Biology, "Samples", "OperationArt/OperatorMission"),
            new Entry(ResearchQuest.Tablet, "Tablet", item: ResearchQuestRules.Tablet),
            new Entry(ResearchQuest.FlashDrive, "Logs", item: ResearchQuestRules.Flash),
            new Entry(ResearchQuest.HardDrive, "Database", item: ResearchQuestRules.Drive),
            new Entry(ResearchQuest.HaloStorage, "Halo Cargo", "PageArt/Operations"),
            new Entry(ResearchQuest.Sentry, "Sentry", "OperationArt/BriefingMission"),
            new Entry(ResearchQuest.NoWayBack, "Access", item: ResearchQuestRules.Credentials, photo: ResearchQuestRules.Flash),
            new Entry(ResearchQuest.EndOfTransmission, "Archive", item: ResearchQuestRules.OriginalArchive, photo: "mc00_archive_drive")
        };

        internal static Entry For(ResearchQuest quest) => Entries[(int)quest];
        internal static EndingText Ending(ResearchEnding ending) => Current.endings[(int)ending - 1];
        internal static string HaloDescription() => Current.halo +
            (ResearchQuestRules.Status(PlayerProfileClient.ResearchQuests, ResearchQuest.HardDrive) == ResearchQuestStatus.Completed
                ? "\n" + Current.haloClassified : "");

        internal static string State(ResearchQuestStatus status) => status switch
        {
            ResearchQuestStatus.Available => Text("可接取", "Available"),
            ResearchQuestStatus.Active => Text("进行中", "Active"),
            ResearchQuestStatus.Ready => Text("待交付", "Ready to turn in"),
            ResearchQuestStatus.Completed => Text("已交付", "Completed"),
            _ => Text("未解锁", "Locked")
        };

        internal static string ItemName(string code) => code switch
        {
            ResearchQuestRules.Tablet => Text("研究员信息平板", "Personnel information tablet"),
            ResearchQuestRules.Flash => Text("加密数据 U 盘", "Encrypted data extraction drive"),
            ResearchQuestRules.Drive => Text("大容量研究硬盘", "High-capacity research storage drive"),
            ResearchQuestRules.Sample => Text("封存生物样本", "Sealed biological sample"),
            ResearchQuestRules.SampleKit => Text("生物采样套件", "Biological sample kit"),
            ResearchQuestRules.Package => Text("封存研究包", "Sealed research package"),
            ResearchQuestRules.NadirRecords => Text("奈迪尔的研究记录", "Nadir's research records"),
            ResearchQuestRules.Credentials => Text("LUCENT 门禁凭证", "LUCENT access credentials"),
            ResearchQuestRules.OriginalArchive => Text("LUCENT 原始研究档案", "Original LUCENT research archive"),
            _ => null
        };

        internal static string ItemDescription(string code) => code switch
        {
            ResearchQuestRules.Tablet => For(ResearchQuest.Tablet).ItemDescription,
            ResearchQuestRules.Flash => For(ResearchQuest.FlashDrive).ItemDescription,
            ResearchQuestRules.Drive => For(ResearchQuest.HardDrive).ItemDescription,
            ResearchQuestRules.SampleKit or ResearchQuestRules.Sample => For(ResearchQuest.Biology).ItemDescription,
            ResearchQuestRules.Package => For(ResearchQuest.HaloStorage).ItemDescription,
            ResearchQuestRules.Credentials => For(ResearchQuest.NoWayBack).ItemDescription,
            ResearchQuestRules.NadirRecords => Text("奈迪尔博士留在隔离区的最后一组记录。保留了实验警告、封锁时间和一段录音。",
                "Dr. Nadir's final records from the isolated sector. Contains the experiment warnings, lockdown timings, and a personal recording."),
            ResearchQuestRules.OriginalArchive => For(ResearchQuest.EndOfTransmission).ItemDescription,
            _ => null
        };
    }
}
