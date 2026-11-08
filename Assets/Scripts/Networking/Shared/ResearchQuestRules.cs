using System;
using System.Collections.Generic;
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS
{
    public enum ResearchQuest
    {
        Exposure, HaloTrial, Biology, Tablet, FlashDrive, HardDrive, HaloStorage, Sentry
    }

    public enum ResearchQuestStatus { Locked, Available, Active, Ready, Completed }

    [Serializable]
    public sealed class ResearchQuestProgress
    {
        public int Accepted, Ready, Completed, HaloKills, SentryKills;
        public int Active => Accepted & ~(Ready | Completed);
        public ResearchQuestProgress Copy() => (ResearchQuestProgress)MemberwiseClone();
    }

    // The offline server freezes this evidence in the existing settlement journal.
    [Serializable]
    public sealed class ResearchQuestResult
    {
        public int Active, ExposureSeconds, HaloKills, SentryKills;
        public string TabletId, FlashId, HardDriveId, PackageId;
        public string[] SampleIds = Array.Empty<string>();
    }

    public static class ResearchQuestRules
    {
        public const int Count = 8;
        public const int ExposureSeconds = 300, HaloKillGoal = 5, SampleGoal = 3, SentryKillGoal = 3;
        public const string Tablet = "rq_tablet", Flash = "rq_flash", Drive = "rq_drive", Sample = "rq_sample", Package = "rq_package";
        private const int All = (1 << Count) - 1;

        public static int Bit(ResearchQuest quest) => 1 << (int)quest;
        public static bool Has(int mask, ResearchQuest quest) => (mask & Bit(quest)) != 0;
        public static bool ValidQuest(ResearchQuest quest) => (uint)quest < Count;

        public static ResearchQuest? Prerequisite(ResearchQuest quest) => quest switch
        {
            ResearchQuest.Exposure => null,
            ResearchQuest.HaloStorage => ResearchQuest.HaloTrial,
            ResearchQuest.Sentry => ResearchQuest.HaloStorage,
            _ => (ResearchQuest)((int)quest - 1)
        };

        public static ResearchQuestStatus Status(ResearchQuestProgress progress, ResearchQuest quest)
        {
            if (progress == null || !ValidQuest(quest)) return ResearchQuestStatus.Locked;
            if (Has(progress.Completed, quest)) return ResearchQuestStatus.Completed;
            if (Has(progress.Ready, quest)) return ResearchQuestStatus.Ready;
            if (Has(progress.Accepted, quest)) return ResearchQuestStatus.Active;
            var prerequisite = Prerequisite(quest);
            return prerequisite.HasValue && !Has(progress.Completed, prerequisite.Value)
                ? ResearchQuestStatus.Locked : ResearchQuestStatus.Available;
        }

        public static bool Valid(ResearchQuestProgress progress)
        {
            if (progress == null || (progress.Accepted & ~All) != 0 || (progress.Ready & progress.Completed) != 0 ||
                ((progress.Ready | progress.Completed) & ~progress.Accepted) != 0 ||
                (uint)progress.HaloKills > HaloKillGoal || (uint)progress.SentryKills > SentryKillGoal) return false;
            for (int i = 0; i < Count; i++)
            {
                var quest = (ResearchQuest)i;
                var prerequisite = Prerequisite(quest);
                if (Has(progress.Accepted, quest) && prerequisite.HasValue && !Has(progress.Completed, prerequisite.Value)) return false;
            }
            return ValidCounter(progress, ResearchQuest.HaloTrial, progress.HaloKills, HaloKillGoal) &&
                ValidCounter(progress, ResearchQuest.Sentry, progress.SentryKills, SentryKillGoal);
        }

        private static bool ValidCounter(ResearchQuestProgress progress, ResearchQuest quest, int count, int goal) =>
            (count == 0 || Has(progress.Accepted, quest)) &&
            (count == goal) == Has(progress.Ready | progress.Completed, quest);

        public static bool Accept(ResearchQuestProgress progress, ResearchQuest quest)
        {
            if (Status(progress, quest) != ResearchQuestStatus.Available) return false;
            progress.Accepted |= Bit(quest);
            return true;
        }

        public static bool Claim(ResearchQuestProgress progress, ResearchQuest quest)
        {
            if (Status(progress, quest) != ResearchQuestStatus.Ready) return false;
            progress.Ready &= ~Bit(quest);
            progress.Completed |= Bit(quest);
            return true;
        }

        public static InventoryError Issue(InventoryGraph loadout, int active)
        {
            foreach (var (quest, code) in new[] { (ResearchQuest.FlashDrive, Flash),
                (ResearchQuest.HardDrive, Drive), (ResearchQuest.HaloStorage, Package) })
            {
                if (!Has(active, quest)) continue;
                var error = loadout.AddSupply(code, 1);
                if (error != InventoryError.None) return error;
            }
            return InventoryError.None;
        }

        public static string IssuedId(InventoryGraph loadout, string code) =>
            loadout?.Items.Find(item => item.Code == code && loadout.Carried(item))?.Id;

        public static bool Holds(InventoryGraph graph, string id, string code, bool found = false)
        {
            if (string.IsNullOrEmpty(id)) return false;
            var item = graph?.Find(id);
            return item != null && item.Code == code && item.Quantity == 1 && graph.Carried(item) && (!found || item.FoundInRaid);
        }

        public static bool StoredInHalo(InventoryGraph graph, string id)
        {
            if (!Holds(graph, id, Package)) return false;
            var item = graph.Find(id);
            var weapon = graph.Find(item.Parent);
            return item.Region == InventoryCatalog.HaloCargoRegion.Id && weapon?.Parent == InventoryCatalog.Equipment &&
                InventoryCatalog.CanStoreHaloCargo(weapon.Code);
        }

        public static void Record(ResearchQuestProgress progress, ResearchQuestResult result, InventoryGraph loadout, InventoryGraph raid, bool extracted)
        {
            if (result == null || result.Active != progress.Active || (uint)result.ExposureSeconds > ExposureSeconds ||
                (uint)result.HaloKills > HaloKillGoal || (uint)result.SentryKills > SentryKillGoal ||
                result.SampleIds == null || result.SampleIds.Length > SampleGoal) return;

            if (Has(result.Active, ResearchQuest.Exposure) && result.ExposureSeconds == ExposureSeconds)
                progress.Ready |= Bit(ResearchQuest.Exposure);
            if (Has(result.Active, ResearchQuest.HaloTrial))
            {
                progress.HaloKills = Math.Min(HaloKillGoal, progress.HaloKills + result.HaloKills);
                if (progress.HaloKills == HaloKillGoal) progress.Ready |= Bit(ResearchQuest.HaloTrial);
            }
            if (Has(result.Active, ResearchQuest.Sentry))
            {
                progress.SentryKills = Math.Min(SentryKillGoal, progress.SentryKills + result.SentryKills);
                if (progress.SentryKills == SentryKillGoal) progress.Ready |= Bit(ResearchQuest.Sentry);
            }
            if (!extracted || raid == null) return;
            if (Has(result.Active, ResearchQuest.Biology) && result.SampleIds.Length == SampleGoal &&
                new HashSet<string>(result.SampleIds).Count == SampleGoal &&
                Array.TrueForAll(result.SampleIds, id => Holds(raid, id, Sample, found: true)))
                progress.Ready |= Bit(ResearchQuest.Biology);
            if (Has(result.Active, ResearchQuest.Tablet) && Holds(raid, result.TabletId, Tablet, found: true))
                progress.Ready |= Bit(ResearchQuest.Tablet);
            if (Has(result.Active, ResearchQuest.FlashDrive) && result.FlashId == IssuedId(loadout, Flash) && Holds(raid, result.FlashId, Flash))
                progress.Ready |= Bit(ResearchQuest.FlashDrive);
            if (Has(result.Active, ResearchQuest.HardDrive) && result.HardDriveId == IssuedId(loadout, Drive) && Holds(raid, result.HardDriveId, Drive))
                progress.Ready |= Bit(ResearchQuest.HardDrive);
            if (Has(result.Active, ResearchQuest.HaloStorage) && result.PackageId == IssuedId(loadout, Package) && StoredInHalo(raid, result.PackageId))
                progress.Ready |= Bit(ResearchQuest.HaloStorage);
        }

        public static void RemoveSupplies(InventoryGraph graph)
        {
            if (graph != null && graph.Items.RemoveAll(item => InventoryCatalog.IsResearchSupply(item.Code)) != 0) graph.Version++;
        }
    }
}
