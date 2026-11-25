// 离线档案中单人战役进度的读写和校验。仓库里改追踪目标走这里，撤离时也在这里把本局的任务记录和实际带出的物品写进战役进度。
using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using Unity.MP_FPS.Campaign;
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS {
    public sealed partial class OfflineProfileStore {
        private static void ValidateCampaignJson(string json) {
            var document = JObject.Parse(json);
            var tutorial = document.GetValue("Tutorial", StringComparison.OrdinalIgnoreCase);
            if (tutorial != null && tutorial.Type != JTokenType.Null) {
                if (!(tutorial is JObject block)) throw new InvalidDataException("Invalid tutorial block.");
                foreach (string field in new[] { "Version", "Status", "IntroPage", "CompletedSteps", "SupplyCycle" })
                    if (block.GetValue(field, StringComparison.OrdinalIgnoreCase)?.Type != JTokenType.Integer)
                        throw new InvalidDataException("Tutorial field is missing or invalid: " + field);
            }
            // 省略 Format 时与 SaveData 的默认格式保持一致。
            // 否则缺版本记号会绕过格式 4 的结构校验。
            var formatToken = document.GetValue("Format", StringComparison.OrdinalIgnoreCase);
            int format;
            try {
                format = formatToken == null || formatToken.Type == JTokenType.Null ? CurrentFormat : formatToken.ToObject<int>();
            } catch (Exception ex) when (ex is ArgumentException || ex is FormatException || ex is OverflowException ||
                                         ex is InvalidCastException) {
                throw new InvalidDataException("Single-player save has an invalid format marker.", ex);
            }
            // 当前格式要求正文带完整章节数据。旧档允许缺失并在后面迁移，但不能让新档缺字段也被读成初始状态。
            if (format != 4) return;
            var campaign = document.GetValue("Campaign", StringComparison.OrdinalIgnoreCase) as JObject;
            if (campaign == null) throw new InvalidDataException("Format 4 chapter data is missing or invalid.");
            foreach (string field in new[] {
                         "SchemaVersion", "ContentVersion", "Revision", "NodeMasks", "CompletedTasks", "NodeItemIds",
                         "ProcessedEvents"
                     }) {
                var value = campaign.GetValue(field, StringComparison.OrdinalIgnoreCase);
                if (value == null || value.Type == JTokenType.Null)
                    throw new InvalidDataException("Format 4 chapter data is missing a required field: " + field);
            }
        }

        private static void ValidateCampaign(SaveData save) {
            // 旧档缺失此项时按新章节起步，
            // 过去的 MissionFirsts 或仓库碎片都不能直接算成剧情进度。
            // 旧档第一次打开时可创建章节进度；当前格式下丢了章节块属于档案损坏。
            if (save.Campaign == null) {
                if (save.Format >= 4)
                    throw new InvalidDataException("Single-player chapter state is missing from a format 4 save.");
                save.Campaign = CampaignRules.Create();
            }
            string error = CampaignRules.Validate(save.Campaign);
            if (error != null || save.ActiveCampaignVersion < 0 || save.ActiveCampaignVersion > 1 ||
                (save.DeploymentId == null && save.ActiveCampaignVersion != 0) ||
                (save.Clash?.ActiveMatchId != null && save.ActiveCampaignVersion != 0))
                throw new InvalidDataException("Unsupported or damaged single-player chapter state: " + error);
            // 较早完成第一章的存档没有后续目标可追踪。
            // 新剧情开放时继续遵照玩家明确取消追踪的设置。
            if (!save.Campaign.TrackingSuppressed && save.Campaign.PrimaryTracked == null &&
                save.Campaign.CompletedTasks.Contains("CM03") && !save.Campaign.CompletedTasks.Contains("CM04")) {
                save.Campaign.PrimaryTracked = "CM04";
                save.Campaign.PreferenceRevision++;
            }
        }

        public CampaignProgress ReadCampaign() {
            lock (m_Lock) {
                if (m_Disposed) throw new ObjectDisposedException(nameof(OfflineProfileStore));
                // UI 拿到的是章节副本，只能通过显式追踪命令写回，不会在内存里误改战局事实。
                return CampaignRules.Clone(m_Save.Campaign);
            }
        }

        public void SetCampaignTracking(string taskId, bool secondary = false, bool remove = false) {
            lock (m_Lock) {
                if (m_Disposed) throw new ObjectDisposedException(nameof(OfflineProfileStore));
                var next = Copy();
                // 局内任务可能已经推到后续节点，但仓库档等撤离才写事实。允许追踪这个尚未入档的目标，追踪本身不会替它完成。
                if (remove) { CampaignRules.Untrack(next.Campaign, taskId); } else if (RaidActive &&
                 CampaignCatalog.Find(taskId) != null && !next.Campaign.CompletedTasks.Contains(taskId)) {
                    // 服务端在局内可能已经开放后续步骤，
                    // 正式档案还要等撤离才能保存。追踪
                    // 只影响界面显示，操作资格仍由任务规则判断。
                    if (secondary) {
                        if (next.Campaign.PrimaryTracked != taskId &&
                            !next.Campaign.SecondaryTracked.Contains(taskId)) {
                            if (next.Campaign.SecondaryTracked.Count >= 2)
                                throw new InvalidOperationException("campaign_tracking_full");
                            next.Campaign.SecondaryTracked.Add(taskId);
                            next.Campaign.PreferenceRevision++;
                        }
                    } else {
                        next.Campaign.PrimaryTracked = taskId;
                        next.Campaign.SecondaryTracked.Remove(taskId);
                        next.Campaign.TrackingSuppressed = false;
                        next.Campaign.PreferenceRevision++;
                    }
                } else if (!CampaignRules.TryTrack(next.Campaign, taskId, secondary, out string error)) {
                    throw new InvalidOperationException(error);
                }
                // Copy() 在持有档案锁时从最新快照开始复制。
                // UI 较早读到的版本不能覆盖新的玩法事实。
                Commit(next);
            }
        }

        private static void CopyCampaignTracking(CampaignProgress source, CampaignProgress target) {
            if (source == null || target == null) return;
            target.PrimaryTracked = source.PrimaryTracked;
            target.SecondaryTracked = new List<string>(source.SecondaryTracked);
            target.TrackingSuppressed = source.TrackingSuppressed;
            // 移植追踪偏好时保留更新程度更高的版本。加载回退也不让界面重新跳回玩家之前取消的目标。
            target.PreferenceRevision = Math.Max(target.PreferenceRevision, source.PreferenceRevision);
        }

        private void ApplyCampaignExtraction(
            SaveData next,
            RaidPersistenceContext.Settlement payload,
            InventoryGraph raid) {
            if (payload.CampaignProgress == null) return; // 已有待发结算回执仍然有效。
            var run = CampaignRules.Clone(payload.CampaignProgress);
            if (m_LoadingCheckpoints != 0)
                throw new InvalidDataException("Chapter results cannot commit before the world has finished loading.");
            // 局内版本和出战时存下的版本对齐后才能收下运行快照，避免旧场景的任务编号写入新版进度。
            if (next.ActiveCampaignVersion != run.ContentVersion || CampaignRules.Validate(run) != null)
                throw new InvalidDataException("Chapter settlement does not match its deployment content.");

            // 在图里曾经看到过样本或档案，还不能算带回成功。
            // 在冻结的撤离库存中再次核对它的实际 ID 和来源。
            // 曾捡起任务样本只说明路上拿过。要完成带出目标，仍需在定格的撤离背包找到同一个有正确来源的样本。
            foreach (string taskId in new[] { "CM03", "CM04" }) {
                var definition = CampaignCatalog.Find(taskId);
                int recover = Array.FindIndex(definition.Nodes, node => node.Id == "recover");
                int mask = run.NodeMasks.TryGetValue(taskId, out int value) ? value : 0;
                if (next.Campaign.CompletedTasks.Contains(taskId) || (mask & (1 << recover)) == 0) continue;
                run.NodeItemIds.TryGetValue(taskId + "/recover", out string itemId);
                var item = raid.Find(itemId);
                if (item == null || item.Code != definition.Nodes[recover].RequiredItemCode || !raid.Carried(item) ||
                    item.MissionRecovered || !MissionInventory.ValidSource(item)) {
                    // 普通物品照常结算，已经遗失的任务物不领取，
                    // 仓库也不会因此卡在结算重试。
                    // 丢样本只回退依赖它的带出节点。普通背包和不相关任务可以照常结算，无需整次出战反复重试。
                    int extract = Array.FindIndex(definition.Nodes, node => node.Kind == CampaignObjectiveKind.Extract);
                    run.NodeMasks[taskId] = mask & ~(1 << recover) & ~(1 << extract);
                    run.CompletedTasks.Remove(taskId);
                    run.NodeItemIds.Remove(taskId + "/recover");
                }
            }
            next.Campaign = CampaignRules.MergeExtracted(next.Campaign, run);
        }
    }
}