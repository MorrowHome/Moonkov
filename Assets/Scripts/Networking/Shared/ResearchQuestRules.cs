#nullable disable
using System;
using System.Collections.Generic;
#if !MOON_PERSISTENCE
using Newtonsoft.Json;
#endif
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS
{
    public enum ResearchQuest
    {
        // These values are stored as bits. Presentation order is separate.
        Exposure, HaloTrial, Biology, Tablet, FlashDrive, HardDrive, HaloStorage, Sentry, NoWayBack, EndOfTransmission
    }

    public enum ResearchQuestStatus { Locked, Available, Active, Ready, Completed }
    public enum ResearchEnding { None, Expose, Corporate, Erase }
    public enum ResearchQuestAction { Accept = 1, Claim, Decide }

    [Serializable]
    public sealed class ResearchQuestCommand
    {
        public ResearchQuestAction Action;
        public ResearchQuest Quest;
        public ResearchEnding Ending;
    }

    [Serializable]
    public sealed class ResearchQuestProgress
    {
        public int Accepted, Ready, Completed, HaloKills, SentryKills;
        public int SequenceVersion, LegacyAccepted;
        public long Revision;
        public string CredentialsId, EvidenceId;
        public ResearchEnding Ending;
        public int Active => Accepted & ~(Ready | Completed);
        public ResearchQuestProgress Copy() => (ResearchQuestProgress)MemberwiseClone();
    }

    // The raid server freezes this evidence in the existing settlement journal.
    [Serializable]
    public sealed class ResearchQuestResult
    {
        public int Active, ExposureSeconds, HaloKills, SentryKills;
        public string TabletId, FlashId, HardDriveId, PackageId;
#if !MOON_PERSISTENCE
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
#endif
        public string NadirRecordsId;
#if !MOON_PERSISTENCE
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
#endif
        public string CredentialsId;
#if !MOON_PERSISTENCE
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
#endif
        public string OriginalArchiveId;
#if !MOON_PERSISTENCE
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
#endif
        public bool PackageSecured;
#if !MOON_PERSISTENCE
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
#endif
        public bool SystemsDisabled;
        public string[] SampleIds = Array.Empty<string>();
    }

    public static class ResearchQuestRules
    {
        public const int Count = 10;
        public const int DeploymentVersion = 1;
        public const int ExposureSeconds = 300, HaloKillGoal = 5, SampleGoal = 3, SentryKillGoal = 3;
        public const string Tablet = "rq_tablet", Flash = "rq_flash", Drive = "rq_drive", Sample = "rq_sample", Package = "rq_package";
        public const string SampleKit = "rq_sample_kit", NadirRecords = "rq_nadir_records", Credentials = "rq_lucent_access", OriginalArchive = "rq_original_archive";
        public const int CorporateDust = 800;
        public const string CorporateHalo = "rifle", CorporateModule = "halo_mooncore_recycler";
        private const int All = (1 << Count) - 1;
        private const int FirstEight = (1 << 8) - 1;
        private const int SequenceVersion = 1;
        private static readonly ResearchQuest[] s_Order =
        {
            ResearchQuest.Exposure, ResearchQuest.HaloTrial, ResearchQuest.Biology, ResearchQuest.Tablet, ResearchQuest.FlashDrive,
            ResearchQuest.HaloStorage, ResearchQuest.Sentry, ResearchQuest.HardDrive, ResearchQuest.NoWayBack, ResearchQuest.EndOfTransmission
        };

        public static int Bit(ResearchQuest quest) => 1 << (int)quest;
        public static bool Has(int mask, ResearchQuest quest) => (mask & Bit(quest)) != 0;
        public static bool ValidQuest(ResearchQuest quest) => (uint)quest < Count;
        public static ResearchQuest At(int index) => s_Order[index];
        public static int Number(ResearchQuest quest) => Array.IndexOf(s_Order, quest) + 1;

        public static ResearchQuest? Prerequisite(ResearchQuest quest) => quest switch
        {
            ResearchQuest.Exposure => null,
            ResearchQuest.HaloStorage => ResearchQuest.FlashDrive,
            ResearchQuest.Sentry => ResearchQuest.HaloStorage,
            ResearchQuest.HardDrive => ResearchQuest.Sentry,
            ResearchQuest.NoWayBack => ResearchQuest.HardDrive,
            ResearchQuest.EndOfTransmission => ResearchQuest.NoWayBack,
            _ => (ResearchQuest)((int)quest - 1)
        };

        public static ResearchQuest? MissingPrerequisite(ResearchQuestProgress progress, ResearchQuest quest)
        {
            var prerequisite = Prerequisite(quest);
            if (prerequisite.HasValue && !Has(progress.Completed, prerequisite.Value)) return prerequisite;
            // A legacy profile may have delivered the database before equipment qualification.
            if (quest == ResearchQuest.NoWayBack && (progress.Completed & FirstEight) != FirstEight)
                for (int i = 0; i < 8; i++)
                    if (!Has(progress.Completed, At(i))) return At(i);
            return null;
        }

        public static ResearchQuestStatus Status(ResearchQuestProgress progress, ResearchQuest quest)
        {
            if (progress == null || !ValidQuest(quest)) return ResearchQuestStatus.Locked;
            if (Has(progress.Completed, quest)) return ResearchQuestStatus.Completed;
            if (Has(progress.Ready, quest)) return ResearchQuestStatus.Ready;
            if (Has(progress.Accepted, quest)) return ResearchQuestStatus.Active;
            return MissingPrerequisite(progress, quest).HasValue
                ? ResearchQuestStatus.Locked : ResearchQuestStatus.Available;
        }

        // Keep prior deliveries and pending contracts. Only the two contracts whose
        // prerequisites changed can have legacy authorization.
        public static void Migrate(ResearchQuestProgress progress)
        {
            if (progress == null || progress.SequenceVersion != 0 || (progress.Accepted & ~FirstEight) != 0 ||
                progress.LegacyAccepted != 0) return;
            foreach (var quest in new[] { ResearchQuest.HaloStorage, ResearchQuest.HardDrive })
            {
                var legacy = quest == ResearchQuest.HaloStorage ? ResearchQuest.HaloTrial : ResearchQuest.FlashDrive;
                if (Has(progress.Accepted, quest) && Has(progress.Completed, legacy) && MissingPrerequisite(progress, quest).HasValue)
                    progress.LegacyAccepted |= Bit(quest);
            }
            progress.SequenceVersion = SequenceVersion;
        }

        public static bool Valid(ResearchQuestProgress progress)
        {
            if (progress == null || progress.SequenceVersion != SequenceVersion || progress.Revision < 0 ||
                (progress.Accepted & ~All) != 0 || (progress.Ready & progress.Completed) != 0 ||
                ((progress.Ready | progress.Completed) & ~progress.Accepted) != 0 ||
                (progress.LegacyAccepted & ~(Bit(ResearchQuest.HaloStorage) | Bit(ResearchQuest.HardDrive))) != 0 ||
                (progress.LegacyAccepted & ~progress.Accepted) != 0 ||
                (uint)progress.HaloKills > HaloKillGoal || (uint)progress.SentryKills > SentryKillGoal ||
                (uint)progress.Ending > (uint)ResearchEnding.Erase) return false;
            for (int i = 0; i < Count; i++)
            {
                var quest = (ResearchQuest)i;
                if (!Has(progress.Accepted, quest)) continue;
                if (Has(progress.LegacyAccepted, quest))
                {
                    var legacy = quest == ResearchQuest.HaloStorage ? ResearchQuest.HaloTrial : ResearchQuest.FlashDrive;
                    if (!Has(progress.Completed, legacy)) return false;
                }
                else if (MissingPrerequisite(progress, quest).HasValue) return false;
            }
            int secured = progress.Ready | progress.Completed;
            if (!ValidEvidence(progress.CredentialsId, Has(secured, ResearchQuest.NoWayBack)) ||
                !ValidEvidence(progress.EvidenceId, Has(secured, ResearchQuest.EndOfTransmission) && progress.Ending == ResearchEnding.None) ||
                (progress.Ending != ResearchEnding.None && !Has(progress.Completed, ResearchQuest.EndOfTransmission))) return false;
            return ValidCounter(progress, ResearchQuest.HaloTrial, progress.HaloKills, HaloKillGoal) &&
                ValidCounter(progress, ResearchQuest.Sentry, progress.SentryKills, SentryKillGoal);
        }

        private static bool ValidEvidence(string id, bool required) => required ? Guid.TryParse(id, out _) : id == null;

        private static bool ValidCounter(ResearchQuestProgress progress, ResearchQuest quest, int count, int goal) =>
            (count == 0 || Has(progress.Accepted, quest)) &&
            (count == goal) == Has(progress.Ready | progress.Completed, quest);

        public static bool Accept(ResearchQuestProgress progress, ResearchQuest quest)
        {
            if (Status(progress, quest) != ResearchQuestStatus.Available) return false;
            Migrate(progress);
            progress.Accepted |= Bit(quest);
            progress.Revision++;
            return true;
        }

        public static bool Claim(ResearchQuestProgress progress, ResearchQuest quest)
        {
            if (Status(progress, quest) != ResearchQuestStatus.Ready) return false;
            progress.Ready &= ~Bit(quest);
            progress.Completed |= Bit(quest);
            progress.Revision++;
            return true;
        }

        public static bool DecisionAvailable(ResearchQuestProgress progress) =>
            progress != null && Has(progress.Completed, ResearchQuest.EndOfTransmission);

        public static bool ChooseEnding(ResearchQuestProgress progress, ResearchEnding ending)
        {
            if (!DecisionAvailable(progress) || progress.Ending != ResearchEnding.None || !Guid.TryParse(progress.EvidenceId, out _) ||
                ending <= ResearchEnding.None || ending > ResearchEnding.Erase) return false;
            progress.EvidenceId = null;
            progress.Ending = ending;
            progress.Revision++;
            return true;
        }

        public static bool ApplyCommand(ResearchQuestProgress progress, ResearchQuestCommand command)
        {
            if (command == null || progress == null) return false;
            return command.Action switch
            {
                ResearchQuestAction.Accept when ValidQuest(command.Quest) =>
                    Has(progress.Accepted, command.Quest) || Accept(progress, command.Quest),
                ResearchQuestAction.Claim when ValidQuest(command.Quest) =>
                    Has(progress.Completed, command.Quest) || Claim(progress, command.Quest),
                ResearchQuestAction.Decide when command.Ending > ResearchEnding.None && command.Ending <= ResearchEnding.Erase =>
                    progress.Ending == command.Ending || ChooseEnding(progress, command.Ending),
                _ => false
            };
        }

        public static bool GrantCorporateReward(InventoryGraph inventory)
        {
            string weapon = Guid.NewGuid().ToString("D"), module = Guid.NewGuid().ToString("D");
            if (!DepositReward(inventory, "dust", CorporateDust) ||
                !DepositReward(inventory, CorporateHalo, 1, weapon) ||
                !DepositReward(inventory, CorporateModule, 1, module)) return false;
            return inventory.TryApply(new InventoryCommand { ExpectedVersion = inventory.Version, Operation = InventoryOperation.Move,
                ItemId = module, Parent = weapon, Region = "Storage" }) == InventoryError.None;
        }

        private static bool DepositReward(InventoryGraph inventory, string code, int quantity, string id = null)
        {
            InventoryError error;
            while ((error = inventory.AddSupply(code, quantity, InventoryCatalog.Stash, id: id)) == InventoryError.Full && inventory.StashRows < 4096)
                inventory.StashRows = Math.Min(4096, inventory.StashRows * 2);
            return error == InventoryError.None;
        }

        public static InventoryError Issue(InventoryGraph loadout, ResearchQuestProgress progress)
        {
            foreach (var (quest, code) in new[] { (ResearchQuest.FlashDrive, Flash),
                (ResearchQuest.HardDrive, Drive), (ResearchQuest.HaloStorage, Package), (ResearchQuest.Biology, SampleKit),
                (ResearchQuest.EndOfTransmission, Credentials) })
            {
                if (!Has(progress.Active, quest)) continue;
                var error = loadout.AddSupply(code, 1, id: quest == ResearchQuest.EndOfTransmission ? progress.CredentialsId : null);
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

        public static bool Record(ResearchQuestProgress progress, ResearchQuestResult result, InventoryGraph loadout, InventoryGraph raid, bool extracted)
        {
            if (!extracted || raid == null || result == null || result.Active != progress.Active || (uint)result.ExposureSeconds > ExposureSeconds ||
                (uint)result.HaloKills > HaloKillGoal || (uint)result.SentryKills > SentryKillGoal ||
                result.SampleIds == null || result.SampleIds.Length > SampleGoal) return false;

            int ready = progress.Ready, haloKills = progress.HaloKills, sentryKills = progress.SentryKills;
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
            if (Has(result.Active, ResearchQuest.Biology) && IssuedId(loadout, SampleKit) != null && result.SampleIds.Length == SampleGoal &&
                new HashSet<string>(result.SampleIds).Count == SampleGoal &&
                Array.TrueForAll(result.SampleIds, id => Holds(raid, id, Sample, found: true)))
                progress.Ready |= Bit(ResearchQuest.Biology);
            if (Has(result.Active, ResearchQuest.Tablet) && Holds(raid, result.TabletId, Tablet, found: true))
                progress.Ready |= Bit(ResearchQuest.Tablet);
            if (Has(result.Active, ResearchQuest.FlashDrive) && result.FlashId == IssuedId(loadout, Flash) && Holds(raid, result.FlashId, Flash))
                progress.Ready |= Bit(ResearchQuest.FlashDrive);
            if (Has(result.Active, ResearchQuest.HardDrive) && result.HardDriveId == IssuedId(loadout, Drive) && Holds(raid, result.HardDriveId, Drive))
                progress.Ready |= Bit(ResearchQuest.HardDrive);
            if (Has(result.Active, ResearchQuest.HaloStorage) && result.PackageSecured &&
                result.PackageId == IssuedId(loadout, Package) && StoredInHalo(raid, result.PackageId))
                progress.Ready |= Bit(ResearchQuest.HaloStorage);
            if (Has(result.Active, ResearchQuest.NoWayBack) && Guid.TryParse(result.CredentialsId, out _) &&
                Holds(raid, result.NadirRecordsId, NadirRecords, found: true) && Holds(raid, result.CredentialsId, Credentials, found: true))
            {
                progress.CredentialsId = result.CredentialsId;
                progress.Ready |= Bit(ResearchQuest.NoWayBack);
            }
            if (Has(result.Active, ResearchQuest.EndOfTransmission) && result.SystemsDisabled && Guid.TryParse(result.OriginalArchiveId, out _) &&
                result.CredentialsId == progress.CredentialsId && result.CredentialsId == IssuedId(loadout, Credentials) &&
                Holds(raid, result.OriginalArchiveId, OriginalArchive, found: true))
            {
                progress.EvidenceId = result.OriginalArchiveId;
                progress.Ready |= Bit(ResearchQuest.EndOfTransmission);
            }
            if (ready == progress.Ready && haloKills == progress.HaloKills && sentryKills == progress.SentryKills) return false;
            progress.Revision++;
            return true;
        }

        public static void RemoveSupplies(InventoryGraph graph)
        {
            if (graph != null && graph.Items.RemoveAll(item => InventoryCatalog.IsResearchSupply(item.Code)) != 0) graph.Version++;
        }
    }
}
