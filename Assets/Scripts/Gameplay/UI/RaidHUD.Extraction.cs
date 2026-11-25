using Unity.Entities;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MP_FPS {
    public sealed partial class RaidHUD {
        private void RefreshExpeditionStatus(bool ready, bool alive, bool settled, bool extracting,
            bool showReturn, Vector3 position) {
            int seconds = Mathf.CeilToInt(m_Snapshot.TimeLeft);
            MoonkovLocalization.Set(m_Timer,
                ready
                    ? MoonkovLocalization.Format(
                        $"Raid {m_Snapshot.RaidId}   {seconds / 60:00}:{seconds % 60:00} remaining")
                    : "Connecting...");
            var clock = ExpeditionClockPresentation.Active;
            MoonkovLocalization.Set(m_Clock,
                clock != null && clock.IsSynchronized ? ExpeditionClock.Display(clock.TotalHours) : "");
            int count = m_Snapshot.Dust + m_Snapshot.Alloy + m_Snapshot.Cells;
            if (count != m_PreviousCount) {
                m_PreviousCount = count;
                m_RevealUntil = Time.unscaledTime + 4;
            }
            RefreshSupplyReceipt(ready && !settled);
            var graph = m_InventoryQuery.IsEmptyIgnoreFilter ? null : CurrentInventory.Graph;
            if (graph == null) {
                MoonkovLocalization.Set(m_Bag, "CARRIED / {0} supplies\nDust {1}   Alloy {2}   Cells {3}", count,
                    m_Snapshot.Dust, m_Snapshot.Alloy, m_Snapshot.Cells);
            } else {
                MoonkovLocalization.Set(m_Bag,
                    "CARRIED / {0} supplies\nDust {1}   Alloy {2}   Cells {3}\nENERGY {4} / READY {5}\nMEDICAL / {6}",
                    count, m_Snapshot.Dust, m_Snapshot.Alloy, m_Snapshot.Cells, graph.CellEnergy(true),
                    graph.CellEnergy(accessibleOnly: true),
                    graph.Count("medkit", true) + graph.Count("med_gel", true) + graph.Count("trauma_kit", true));
            }
            // 撤离提示音按局记状态。进入或离开读条才放一次开始或取消声，最后几秒按间隔提示。
            if (m_ExtractionSoundRaid != m_Snapshot.RaidId) {
                m_ExtractionSoundRaid = m_Snapshot.RaidId;
                m_AudioExtracting = false;
                m_ExtractionSoundAt = 0;
            }
            if (extracting != m_AudioExtracting) {
                if (extracting || (alive && !settled))
                    MoonkovAudio.Play(
                        extracting ? MoonkovAudio.Library?.ExtractStart : MoonkovAudio.Library?.ExtractCancel,
                        Vector3.zero);
                m_AudioExtracting = extracting;
                m_ExtractionSoundAt = Time.unscaledTime + 1;
            }
            if (extracting && m_Snapshot.ExtractionRemaining <= 3 && Time.unscaledTime >= m_ExtractionSoundAt) {
                MoonkovAudio.Play(MoonkovAudio.Library?.ExtractTick, Vector3.zero, .65f);
                m_ExtractionSoundAt = Time.unscaledTime + 1;
            }
            bool returnBeacon = showReturn && m_ReturnGuidance.ShowBeacon;
            m_ExtractionPanel.EnableInClassList("is-visible",
                alive && !settled && showReturn && (extracting || returnBeacon));
            m_ExtractionTrack.style.display = DisplayStyle.None;
            // 这里用房主同步的剩余时间计算撤离进度。客户端只负责画进度条。
            if (extracting)
                m_ExtractionFill.style.width = Length.Percent(100 *
                                                              Mathf.Clamp01(1 - m_Snapshot.ExtractionRemaining /
                                                                  MoonRaidMap.Active.ExtractionSeconds));
            else
                m_ExtractionFill.style.width = Length.Percent(0);
            if (alive && !settled) {
                var delta = MoonRaidMap.Active.NearestExtraction(position) - position;
                string heading = Mathf.Abs(delta.x) > Mathf.Abs(delta.z) ? delta.x > 0 ? "E" : "W" :
                    delta.z > 0 ? "N" : "S";
                if (extracting)
                    MoonkovLocalization.Set(m_Exit,
                        MoonRaidMap.Active.ExtractionCount > 1 ? "EXTRACTION BEACON" : "SHUTTLE BEACON");
                else if (returnBeacon)
                    MoonkovLocalization.Set(m_Exit, m_ReturnGuidance.BeaconText, delta.magnitude,
                        MoonkovLocalization.Text(heading));
                else
                    MoonkovLocalization.Set(m_Exit, "");
            } else { MoonkovLocalization.Set(m_Exit, ""); }
            if (settled) {
                string message = m_Snapshot.SaveState switch {
                    RaidSaveState.SessionOnly => "Stash lasts for this connection only.",
                    RaidSaveState.Saved when m_Snapshot.Phase == RaidPhase.Extracted =>
                        "Extracted items are saved. If recovery items are pending, make room and claim them before deploying again.",
                    RaidSaveState.Saved => "Stash saved. You can reconnect later.",
                    RaidSaveState.Retrying => "Waiting to save. Retrying...",
                    _ => "Saving raid results..."
                };
                MoonkovLocalization.Set(m_SaveStatus, message);
                RenderResult();
            }
        }

        private void RefreshSettlementControls() {
            // LateUpdate 在角色清理后执行。清理过程可能恢复旧鼠标状态，所以此处再调整一次。
            Utils.SetCursorVisible(true);
            bool saving = m_Snapshot.SaveState == RaidSaveState.Saving ||
                          m_Snapshot.SaveState == RaidSaveState.Retrying;
            if (m_DeployRequestedRaid == m_Snapshot.RaidId && m_Snapshot.Sequence > m_DeployRequestedSequence &&
                m_Snapshot.LoadoutRequestId == m_DeployRequestId && !m_Snapshot.DeployPending &&
                m_Snapshot.LoadoutError != RaidLoadoutError.None)
                m_DeployRequestedRaid = 0;
            // 服务端同步出发状态前，本机也记住已经点过出发，以免在快照到达前重复提交。
            bool deploying = m_Snapshot.DeployPending || m_DeployRequestedRaid == m_Snapshot.RaidId;
            m_Deploy.SetEnabled(!saving && !deploying);
            if (OnlineLobbyClient.InInstance) MoonkovLocalization.Set(m_Deploy, "返回小队 · 下一局使用全新地图");
            m_Return.SetEnabled(!saving && !deploying && GameManager.CanUseMainMenu);
            m_CarryCells.SetEnabled(!saving && !deploying);
            // 玩家正在编辑时保留当下输入。
            if (m_CarryCells.panel?.focusController.focusedElement is not VisualElement focused ||
                !m_CarryCells.Contains(focused))
                m_CarryCells.SetValueWithoutNotify(PlayerProfileClient.CarryCells);
            string note = deploying ? "Preparing loadout. Waiting for server confirmation..." : m_Snapshot.LoadoutError switch {
                RaidLoadoutError.InsufficientCells => "Not enough cells in storage. Reduce the quantity or refresh storage on the ship.",
                RaidLoadoutError.InvalidCount => "Choose 0-12 energy cells.",
                RaidLoadoutError.ResearchSuppliesNoSpace => Client.ResearchQuestCopy.Text("任务配发物品放不下。请回仓库腾出空间。",
                    "Make room in your loadout for mission equipment."),
                RaidLoadoutError.Rejected => "Loadout rejected. You can retry or return to the ship.",
                _ => MoonkovLocalization.Format($"Storage: {m_Snapshot.StashCells} cells / Carry limit: {RaidRules.BagCapacity}. Reloading spends energy per round. Unspent energy returns on extraction.")
            };
            MoonkovLocalization.Set(m_LoadoutNote, note);
            if (!m_WasSettled && !saving) m_Return.Focus();
        }

        private void ResultBack() {
            m_ResultStep = Mathf.Max(0, m_ResultStep - 1);
            RenderResult();
        }

        private void ResultNext() {
            m_ResultStep = Mathf.Min(2, m_ResultStep + 1);
            RenderResult();
        }

        private void ReturnToShip() {
            if (GameManager.CanUseMainMenu) GameManager.Instance.ReturnToMainMenuAsync();
        }

        // 界面只显示本局且结果状态一致的物资清单。结算页依次展示战局结果、带回或丢失的物品和仓库余额。
        private void RenderResult() {
            RaidResultManifest manifest = null;
            if (m_World != null && m_World.IsCreated && !m_InventoryQuery.IsEmptyIgnoreFilter) {
                var inventory = CurrentInventory;
                // 结果状态和物资清单来自不同的同步消息。只有局号和结局对应上才展示清单，先到的状态可先显示标题。
                if (inventory.RaidId == m_Snapshot.RaidId && inventory.Result?.Outcome == m_Snapshot.Phase)
                    manifest = inventory.Result;
            }
            string title = m_Snapshot.Phase == RaidPhase.Extracted ? "SURVIVED" :
                m_Snapshot.Phase == RaidPhase.Dead ? "KILLED IN ACTION" : "TIME EXPIRED";
            MoonkovLocalization.Set(m_Root.Q<Label>("raidResultHeading"),
                m_ResultStep == 0 ? title : m_ResultStep == 1 ? "CARGO MANIFEST" : "RESOURCE BALANCES");
            if (manifest != null && m_ResultStep != 2)
                MoonkovLocalization.Set(m_ResultText, "EXPEDITION {0:000}\nReturned {1:N0} / Lost {2:N0}",
                    m_Snapshot.RaidId, manifest.ReturnedCount, manifest.LostCount);
            else
                MoonkovLocalization.Set(m_ResultText, "EXPEDITION {0:000}", m_Snapshot.RaidId);
            m_Result.EnableInClassList("raid-result-failed", m_Snapshot.Phase != RaidPhase.Extracted);
            m_Result.EnableInClassList("raid-result-cargo", m_ResultStep == 1);
            m_Result.EnableInClassList("raid-result-storage", m_ResultStep == 2);
            m_Root.Q("raidManifest").style.display = m_ResultStep == 0 ? DisplayStyle.None : DisplayStyle.Flex;
            m_Root.Q("raidStorageManifest").style.display = m_ResultStep == 2 ? DisplayStyle.Flex : DisplayStyle.None;
            m_Root.Q("raidCargoManifest").style.display = m_ResultStep == 1 ? DisplayStyle.Flex : DisplayStyle.None;
            if (m_ResultStep == 1) m_ResultCargo.Present(manifest);
            MoonkovLocalization.Set(m_Root.Q<Label>("raidManifestDust"), "{0:N0}",
                m_ResultStep == 2 ? m_Snapshot.StashDust : m_Snapshot.Dust);
            MoonkovLocalization.Set(m_Root.Q<Label>("raidManifestAlloy"), "{0:N0}",
                m_ResultStep == 2 ? m_Snapshot.StashAlloy : m_Snapshot.Alloy);
            MoonkovLocalization.Set(m_Root.Q<Label>("raidManifestCells"), "{0:N0}",
                m_ResultStep == 2 ? m_Snapshot.StashCells : m_Snapshot.Cells);
            for (int i = 0; i < 3; i++)
                m_Root.Q<Label>("raidStep" + i).EnableInClassList("raid-step-active", i == m_ResultStep);
            m_ResultBack.SetEnabled(m_ResultStep > 0);
            m_ResultNext.style.display = m_ResultStep < 2 ? DisplayStyle.Flex : DisplayStyle.None;
            // 结算操作由一个界面接管。教学局必须经作业船返回。
            // 结算的最后一页才提供再出发。教学局保留返回船舱的入口，避免越过教程的后续步骤。
            m_Deploy.style.display = m_ResultStep == 2 && !m_TutorialConfigured ? DisplayStyle.Flex : DisplayStyle.None;
            m_Return.style.display = DisplayStyle.Flex;
            m_LoadoutPanel.style.display = m_ResultStep == 2 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // 客户端 World 放着服务器同步回来的数据。这个界面只连实际玩家的客户端，找到拥有的角色、已入场的连接和库存读数。
    }
}
