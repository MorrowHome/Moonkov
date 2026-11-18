// MOONRAT 的任务、剧情和结局规则同时给客户端与存档后端使用。接取、交付、回顾和最终选择都在这份进度模型上检查前置条件。
#nullable disable
using System;
using System.Collections.Generic;
#if !MOON_PERSISTENCE
using Newtonsoft.Json;
#endif
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS {
    public enum ResearchQuest {
        // These values are stored as bits. Presentation order is separate.
        Exposure,
        HaloTrial,
        Biology,
        Tablet,
        FlashDrive,
        HardDrive,
        HaloStorage,
        Sentry,
        NoWayBack,
        EndOfTransmission
    }

    public enum ResearchQuestStatus {
        Locked,
        Available,
        Active,
        Ready,
        Completed
    }

    public enum ResearchEnding {
        None,
        Expose,
        Corporate,
        Erase
    }

    public enum ResearchQuestAction {
        Accept = 1,
        Claim,
        Decide,
        ReviewStory
    }

    public enum ResearchStoryChapter {
        Recruitment,
        Spectrum,
        Nadir,
        Lockdown,
        Qualification,
        Lucent,
        Recall,
        Ultimatum,
        Originals
    }

    [Serializable]
    public sealed class ResearchQuestCommand {
        public ResearchQuestAction Action;
        public ResearchQuest Quest;
        public ResearchEnding Ending;
        public ResearchStoryChapter Chapter;
    }

    [Serializable]
    public sealed class ResearchQuestProgress {
        public int Accepted, Ready, Completed, HaloKills, SentryKills;
        public int SequenceVersion, LegacyAccepted;
        public int StorySeen;
        public long Revision;
        public string CredentialsId, EvidenceId;
        public ResearchEnding Ending;
        // 三个掩码分别表示接取、达到交付条件和已领取。已经准备好交付的任务就不用局内继续记满一次。
        public int Active => Accepted & ~(Ready | Completed);
        public ResearchQuestProgress Copy() { return (ResearchQuestProgress)MemberwiseClone(); }
    }

    // The raid server freezes this evidence in the existing settlement journal.
    [Serializable]
    public sealed class ResearchQuestResult {
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

    public static class ResearchQuestRules {
        public const int Count = 10;
        public const int StoryCount = 9;
        public const int DeploymentVersion = 1;
        public const int ExposureSeconds = 300, HaloKillGoal = 5, SampleGoal = 3, SentryKillGoal = 3;

        public const string Tablet = "rq_tablet",
            Flash = "rq_flash",
            Drive = "rq_drive",
            Sample = "rq_sample",
            Package = "rq_package";

        public const string SampleKit = "rq_sample_kit",
            NadirRecords = "rq_nadir_records",
            Credentials = "rq_lucent_access",
            OriginalArchive = "rq_original_archive";

        public const int CorporateDust = 800;
        public const string CorporateHalo = "rifle", CorporateModule = "halo_mooncore_recycler";
        private const int All = (1 << Count) - 1;
        private const int FirstEight = (1 << 8) - 1;
        private const int SequenceVersion = 1;

        private static readonly ResearchQuest[] s_Order = {
            ResearchQuest.Exposure, ResearchQuest.HaloTrial, ResearchQuest.Biology, ResearchQuest.Tablet,
            ResearchQuest.FlashDrive,
            ResearchQuest.HaloStorage, ResearchQuest.Sentry, ResearchQuest.HardDrive, ResearchQuest.NoWayBack,
            ResearchQuest.EndOfTransmission
        };

        public static int Bit(ResearchQuest quest) { return 1 << (int)quest; }
        public static bool Has(int mask, ResearchQuest quest) { return (mask & Bit(quest)) != 0; }
        public static bool ValidQuest(ResearchQuest quest) { return (uint)quest < Count; }
        public static ResearchQuest At(int index) { return s_Order[index]; }
        public static int Number(ResearchQuest quest) { return Array.IndexOf(s_Order, quest) + 1; }

        public static ResearchQuest? Prerequisite(ResearchQuest quest) {
            return quest switch {
                ResearchQuest.Exposure => null,
                ResearchQuest.HaloStorage => ResearchQuest.FlashDrive,
                ResearchQuest.Sentry => ResearchQuest.HaloStorage,
                ResearchQuest.HardDrive => ResearchQuest.Sentry,
                ResearchQuest.NoWayBack => ResearchQuest.HardDrive,
                ResearchQuest.EndOfTransmission => ResearchQuest.NoWayBack,
                _ => (ResearchQuest)((int)quest - 1)
            };
        }

        // 同一条进度链按任务表查前置。客户端能解释为什么暂未开放，档案提交也用相同条件。
        public static ResearchQuest? MissingPrerequisite(ResearchQuestProgress progress, ResearchQuest quest) {
            var prerequisite = Prerequisite(quest);
            if (prerequisite.HasValue && !Has(progress.Completed, prerequisite.Value)) return prerequisite;
            // A legacy profile may have delivered the database before equipment qualification.
            if (quest == ResearchQuest.NoWayBack && (progress.Completed & FirstEight) != FirstEight)
                for (int i = 0; i < 8; i++)
                    if (!Has(progress.Completed, At(i)))
                        return At(i);
            return null;
        }

        public static ResearchQuestStatus Status(ResearchQuestProgress progress, ResearchQuest quest) {
            if (progress == null || !ValidQuest(quest)) return ResearchQuestStatus.Locked;
            if (Has(progress.Completed, quest)) return ResearchQuestStatus.Completed;
            if (Has(progress.Ready, quest)) return ResearchQuestStatus.Ready;
            if (Has(progress.Accepted, quest)) return ResearchQuestStatus.Active;
            return MissingPrerequisite(progress, quest).HasValue
                ? ResearchQuestStatus.Locked
                : ResearchQuestStatus.Available;
        }

        // Keep prior deliveries and pending contracts. Only the two contracts whose
        // prerequisites changed can have legacy authorization.
        // 旧档第一次加载时转换版本和已知进度位，随后必须通过当前结构验证。
        public static void Migrate(ResearchQuestProgress progress) {
            if (progress == null || progress.SequenceVersion != 0 || (progress.Accepted & ~FirstEight) != 0 ||
                progress.LegacyAccepted != 0)
                return;
            foreach (var quest in new[] { ResearchQuest.HaloStorage, ResearchQuest.HardDrive }) {
                var legacy = quest == ResearchQuest.HaloStorage ? ResearchQuest.HaloTrial : ResearchQuest.FlashDrive;
                if (Has(progress.Accepted, quest) && Has(progress.Completed, legacy) &&
                    MissingPrerequisite(progress, quest).HasValue)
                    progress.LegacyAccepted |= Bit(quest);
            }
            progress.SequenceVersion = SequenceVersion;
        }

        // 状态位、计数、凭据与最终证据要互相对应。不能只因 JSON 反序列化成功就当作合法完成。
        public static bool Valid(ResearchQuestProgress progress) {
            if (progress == null || progress.SequenceVersion != SequenceVersion || progress.Revision < 0 ||
                (progress.StorySeen & ~((1 << StoryCount) - 1)) != 0 || (progress.Accepted & ~All) != 0 ||
                (progress.Ready & progress.Completed) != 0 ||
                ((progress.Ready | progress.Completed) & ~progress.Accepted) != 0 ||
                (progress.LegacyAccepted & ~(Bit(ResearchQuest.HaloStorage) | Bit(ResearchQuest.HardDrive))) != 0 ||
                (progress.LegacyAccepted & ~progress.Accepted) != 0 || (uint)progress.HaloKills > HaloKillGoal ||
                (uint)progress.SentryKills > SentryKillGoal || (uint)progress.Ending > (uint)ResearchEnding.Erase)
                return false;
            for (int i = 0; i < StoryCount; i++)
                if (StoryReviewed(progress, (ResearchStoryChapter)i) &&
                    !StoryAvailable(progress, (ResearchStoryChapter)i))
                    return false;
            for (int i = 0; i < Count; i++) {
                var quest = (ResearchQuest)i;
                if (!Has(progress.Accepted, quest)) continue;
                if (Has(progress.LegacyAccepted, quest)) {
                    var legacy = quest == ResearchQuest.HaloStorage
                        ? ResearchQuest.HaloTrial
                        : ResearchQuest.FlashDrive;
                    if (!Has(progress.Completed, legacy)) return false;
                } else if (MissingPrerequisite(progress, quest).HasValue) { return false; }
            }
            int secured = progress.Ready | progress.Completed;
            if (!ValidEvidence(progress.CredentialsId, Has(secured, ResearchQuest.NoWayBack)) ||
                !ValidEvidence(progress.EvidenceId,
                    Has(secured, ResearchQuest.EndOfTransmission) && progress.Ending == ResearchEnding.None) ||
                (progress.Ending != ResearchEnding.None && !Has(progress.Completed, ResearchQuest.EndOfTransmission)))
                return false;
            return ValidCounter(progress, ResearchQuest.HaloTrial, progress.HaloKills, HaloKillGoal) &&
                   ValidCounter(progress, ResearchQuest.Sentry, progress.SentryKills, SentryKillGoal);
        }

        private static bool ValidEvidence(string id, bool required) {
            return required ? Guid.TryParse(id, out _) : id == null;
        }

        private static bool ValidCounter(ResearchQuestProgress progress, ResearchQuest quest, int count, int goal) {
            return (count == 0 || Has(progress.Accepted, quest)) &&
                   (count == goal) == Has(progress.Ready | progress.Completed, quest);
        }

        // 接取通过后加入已接位并推进修订号，提供给后面的出战资料。
        public static bool Accept(ResearchQuestProgress progress, ResearchQuest quest) {
            if (Status(progress, quest) != ResearchQuestStatus.Available) return false;
            Migrate(progress);
            progress.Accepted |= Bit(quest);
            progress.Revision++;
            return true;
        }

        // 要先达到交付条件才允许领取。已完成的前置可解锁下个任务，领取与现场拿物是两条路径。
        public static bool Claim(ResearchQuestProgress progress, ResearchQuest quest) {
            if (Status(progress, quest) != ResearchQuestStatus.Ready) return false;
            progress.Ready &= ~Bit(quest);
            progress.Completed |= Bit(quest);
            progress.Revision++;
            return true;
        }

        public static bool DecisionAvailable(ResearchQuestProgress progress) {
            return progress != null && Has(progress.Completed, ResearchQuest.EndOfTransmission);
        }

        private static ResearchQuest? StoryPrerequisite(ResearchStoryChapter chapter) {
            return chapter switch {
                ResearchStoryChapter.Spectrum => ResearchQuest.Biology,
                ResearchStoryChapter.Nadir => ResearchQuest.Tablet,
                ResearchStoryChapter.Lockdown => ResearchQuest.FlashDrive,
                ResearchStoryChapter.Qualification => ResearchQuest.Sentry,
                ResearchStoryChapter.Lucent or ResearchStoryChapter.Recall => ResearchQuest.HardDrive,
                ResearchStoryChapter.Ultimatum => ResearchQuest.NoWayBack,
                ResearchStoryChapter.Originals => ResearchQuest.EndOfTransmission,
                _ => null
            };
        }

        public static bool StoryAvailable(ResearchQuestProgress progress, ResearchStoryChapter chapter) {
            if (progress == null || (uint)chapter >= StoryCount) return false;
            if (chapter is ResearchStoryChapter.Lucent or ResearchStoryChapter.Recall &&
                !Has(progress.Completed, ResearchQuest.Sentry))
                return false;
            var prerequisite = StoryPrerequisite(chapter);
            return !prerequisite.HasValue || Has(progress.Completed, prerequisite.Value);
        }

        public static bool StoryReviewed(ResearchQuestProgress progress, ResearchStoryChapter chapter) {
            return progress != null && (uint)chapter < StoryCount && (progress.StorySeen & (1 << (int)chapter)) != 0;
        }

        private static bool ReviewStory(ResearchQuestProgress progress, ResearchStoryChapter chapter) {
            if (!StoryAvailable(progress, chapter)) return false;
            if (StoryReviewed(progress, chapter)) return true;
            progress.StorySeen |= 1 << (int)chapter;
            progress.Revision++;
            return true;
        }

        // 最后选择需要已经存在的证据 ID 和尚未做过决定。选择后消费该证据，防止再次走另一结局。
        public static bool ChooseEnding(ResearchQuestProgress progress, ResearchEnding ending) {
            if (!DecisionAvailable(progress) || progress.Ending != ResearchEnding.None ||
                !Guid.TryParse(progress.EvidenceId, out _) || ending <= ResearchEnding.None ||
                ending > ResearchEnding.Erase)
                return false;
            progress.EvidenceId = null;
            progress.Ending = ending;
            progress.Revision++;
            return true;
        }

        // 接取或领取同一任务的请求可以因刷新或重试再发一次。这里先认已成功的状态，重复的接取或领取不会再改进度。
        public static bool ApplyCommand(ResearchQuestProgress progress, ResearchQuestCommand command) {
            if (command == null || progress == null) return false;
            return command.Action switch {
                ResearchQuestAction.Accept when ValidQuest(command.Quest) => Has(progress.Accepted, command.Quest) ||
                                                                             Accept(progress, command.Quest),
                ResearchQuestAction.Claim when ValidQuest(command.Quest) => Has(progress.Completed, command.Quest) ||
                                                                            Claim(progress, command.Quest),
                ResearchQuestAction.Decide when command.Ending > ResearchEnding.None &&
                                                command.Ending <= ResearchEnding.Erase => progress.Ending ==
                    command.Ending || ChooseEnding(progress, command.Ending),
                ResearchQuestAction.ReviewStory => ReviewStory(progress, command.Chapter),
                _ => false
            };
        }

        // 依次尝试把公司奖励存入仓库，再给新光环装模块。调用方只在全套放置都成功后写盘。
        public static bool GrantCorporateReward(InventoryGraph inventory) {
            string weapon = Guid.NewGuid().ToString("D"), module = Guid.NewGuid().ToString("D");
            if (!DepositReward(inventory, "dust", CorporateDust) ||
                !DepositReward(inventory, CorporateHalo, 1, weapon) ||
                !DepositReward(inventory, CorporateModule, 1, module))
                return false;
            return inventory.TryApply(new InventoryCommand {
                ExpectedVersion = inventory.Version, Operation = InventoryOperation.Move,
                ItemId = module, Parent = weapon, Region = "Storage"
            }) == InventoryError.None;
        }

        private static bool DepositReward(InventoryGraph inventory, string code, int quantity, string id = null) {
            InventoryError error;
            while ((error = inventory.AddSupply(code, quantity, InventoryCatalog.Stash, id: id)) ==
                   InventoryError.Full && inventory.StashRows < 4096)
                inventory.StashRows = Math.Min(4096, inventory.StashRows * 2);
            return error == InventoryError.None;
        }

        // 角色出战时根据当前活动任务发配电子设备、物品和证据。放不下就阻挡本局扣装，不能带着缺组件的档案进图。
        public static InventoryError Issue(InventoryGraph loadout, ResearchQuestProgress progress) {
            foreach ((var quest, string code) in new[] {
                         (ResearchQuest.FlashDrive, Flash),
                         (ResearchQuest.HardDrive, Drive), (ResearchQuest.HaloStorage, Package),
                         (ResearchQuest.Biology, SampleKit),
                         (ResearchQuest.EndOfTransmission, Credentials)
                     }) {
                if (!Has(progress.Active, quest)) continue;
                var error = loadout.AddSupply(code, 1,
                    id: quest == ResearchQuest.EndOfTransmission ? progress.CredentialsId : null);
                if (error != InventoryError.None) return error;
            }
            return InventoryError.None;
        }

        public static string IssuedId(InventoryGraph loadout, string code) {
            return loadout?.Items.Find(item => item.Code == code && loadout.Carried(item))?.Id;
        }

        // 代码和具体物品编号都要相同。任务要求确为现场取得的证据时还会核对来源。
        public static bool Holds(InventoryGraph graph, string id, string code, bool found = false) {
            if (string.IsNullOrEmpty(id)) return false;
            var item = graph?.Find(id);
            return item != null && item.Code == code && item.Quantity == 1 && graph.Carried(item) &&
                   (!found || item.FoundInRaid);
        }

        public static bool StoredInHalo(InventoryGraph graph, string id) {
            if (!Holds(graph, id, Package)) return false;
            var item = graph.Find(id);
            var weapon = graph.Find(item.Parent);
            return item.Region == InventoryCatalog.HaloCargoRegion.Id && weapon?.Parent == InventoryCatalog.Equipment &&
                   InventoryCatalog.CanStoreHaloCargo(weapon.Code);
        }

        public static bool Record(
            ResearchQuestProgress progress,
            ResearchQuestResult result,
            InventoryGraph loadout,
            InventoryGraph raid,
            bool extracted) {
            // 只有成功撤离且任务入口与出战时一致才接本次结果。计数上限和背包完整也要满足。
            if (!extracted || raid == null || result == null || result.Active != progress.Active ||
                (uint)result.ExposureSeconds > ExposureSeconds || (uint)result.HaloKills > HaloKillGoal ||
                (uint)result.SentryKills > SentryKillGoal || result.SampleIds == null ||
                result.SampleIds.Length > SampleGoal)
                return false;
            int ready = progress.Ready, haloKills = progress.HaloKills, sentryKills = progress.SentryKills;
            if (Has(result.Active, ResearchQuest.Exposure) && result.ExposureSeconds == ExposureSeconds)
                progress.Ready |= Bit(ResearchQuest.Exposure);
            if (Has(result.Active, ResearchQuest.HaloTrial)) {
                progress.HaloKills = Math.Min(HaloKillGoal, progress.HaloKills + result.HaloKills);
                if (progress.HaloKills == HaloKillGoal) progress.Ready |= Bit(ResearchQuest.HaloTrial);
            }
            if (Has(result.Active, ResearchQuest.Sentry)) {
                progress.SentryKills = Math.Min(SentryKillGoal, progress.SentryKills + result.SentryKills);
                if (progress.SentryKills == SentryKillGoal) progress.Ready |= Bit(ResearchQuest.Sentry);
            }
            // 不同任务要核对实际带回的东西，有些要求本局预先配发的同一件工具。完成计数本身不能伪造样品或原档证据。
            if (Has(result.Active, ResearchQuest.Biology) && IssuedId(loadout, SampleKit) != null &&
                result.SampleIds.Length == SampleGoal && new HashSet<string>(result.SampleIds).Count == SampleGoal &&
                Array.TrueForAll(result.SampleIds, id => Holds(raid, id, Sample, true)))
                progress.Ready |= Bit(ResearchQuest.Biology);
            if (Has(result.Active, ResearchQuest.Tablet) && Holds(raid, result.TabletId, Tablet, true))
                progress.Ready |= Bit(ResearchQuest.Tablet);
            if (Has(result.Active, ResearchQuest.FlashDrive) && result.FlashId == IssuedId(loadout, Flash) &&
                Holds(raid, result.FlashId, Flash))
                progress.Ready |= Bit(ResearchQuest.FlashDrive);
            if (Has(result.Active, ResearchQuest.HardDrive) && result.HardDriveId == IssuedId(loadout, Drive) &&
                Holds(raid, result.HardDriveId, Drive))
                progress.Ready |= Bit(ResearchQuest.HardDrive);
            if (Has(result.Active, ResearchQuest.HaloStorage) && result.PackageSecured &&
                result.PackageId == IssuedId(loadout, Package) && StoredInHalo(raid, result.PackageId))
                progress.Ready |= Bit(ResearchQuest.HaloStorage);
            if (Has(result.Active, ResearchQuest.NoWayBack) && Guid.TryParse(result.CredentialsId, out _) &&
                Holds(raid, result.NadirRecordsId, NadirRecords, true) &&
                Holds(raid, result.CredentialsId, Credentials, true)) {
                progress.CredentialsId = result.CredentialsId;
                progress.Ready |= Bit(ResearchQuest.NoWayBack);
            }
            // 最后的证据要对应已经保存的凭据，并见到机器关闭和带回的实际档案。由当前确认条件决定是否可选结局。
            if (Has(result.Active, ResearchQuest.EndOfTransmission) && result.SystemsDisabled &&
                Guid.TryParse(result.OriginalArchiveId, out _) && result.CredentialsId == progress.CredentialsId &&
                result.CredentialsId == IssuedId(loadout, Credentials) &&
                Holds(raid, result.OriginalArchiveId, OriginalArchive, true)) {
                progress.EvidenceId = result.OriginalArchiveId;
                progress.Ready |= Bit(ResearchQuest.EndOfTransmission);
            }
            if (ready == progress.Ready && haloKills == progress.HaloKills && sentryKills == progress.SentryKills)
                return false;
            progress.Revision++;
            return true;
        }

        // 临时研究道具只为这趟任务提供工具。结算检查完再从可带回物品里剔除，防止留着大量同款配发物。
        public static void RemoveSupplies(InventoryGraph graph) {
            if (graph != null && graph.Items.RemoveAll(item => InventoryCatalog.IsResearchSupply(item.Code)) != 0)
                graph.Version++;
        }
    }
}