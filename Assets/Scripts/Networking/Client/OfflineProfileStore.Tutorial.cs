using System;
using System.IO;
using System.Linq;
using Unity.MP_FPS.Campaign;
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS
{
    public sealed partial class OfflineProfileStore
    {
        private static void ValidateTutorial(SaveData save)
        {
            if (save.Tutorial == null)
            {
                if (save.Receipts.ContainsKey("tutorial/schema-v1")) throw new InvalidDataException("Tutorial state is missing.");
                // Older profiles remain opt-in. No old chapter facts imply tutorial completion.
                save.Tutorial = new TutorialProgress { Status = TutorialStatus.Available };
            }
            if (TutorialRules.Validate(save.Tutorial) != null) throw new InvalidDataException("Tutorial state is invalid.");
        }

        public TutorialProgress ReadTutorial()
        {
            lock (m_Lock)
            {
                if (m_Disposed) throw new ObjectDisposedException(nameof(OfflineProfileStore));
                return new TutorialProgress { Version = m_Save.Tutorial.Version, Status = m_Save.Tutorial.Status,
                    IntroPage = m_Save.Tutorial.IntroPage, CompletedSteps = m_Save.Tutorial.CompletedSteps, SupplyCycle = m_Save.Tutorial.SupplyCycle };
            }
        }

        public void UpdateTutorial(bool skip = false, bool nextIntro = false, int steps = 0)
        {
            lock (m_Lock)
            {
                if (m_Disposed) throw new ObjectDisposedException(nameof(OfflineProfileStore));
                if (m_Save.Tutorial.Status != TutorialStatus.Active) return;
                if ((steps & ~TutorialRules.AllSteps) != 0) throw new ArgumentOutOfRangeException(nameof(steps));
                var next = Copy();
                if (skip) next.Tutorial.Status = TutorialStatus.Skipped;
                if (nextIntro) next.Tutorial.IntroPage = Math.Min(TutorialRules.Introduction.Length, next.Tutorial.IntroPage + 1);
                next.Tutorial.CompletedSteps |= steps;
                if (next.Tutorial.Status == m_Save.Tutorial.Status && next.Tutorial.IntroPage == m_Save.Tutorial.IntroPage &&
                    next.Tutorial.CompletedSteps == m_Save.Tutorial.CompletedSteps) return;
                next.Receipts["tutorial/schema-v1"] = "1";
                Commit(next);
            }
        }

        public string ClaimTutorialSupplies()
        {
            lock (m_Lock)
            {
                if (m_Disposed) throw new ObjectDisposedException(nameof(OfflineProfileStore));
                if (RaidActive || m_LoadingCheckpoints != 0) return "请返回作业船，等待本次行动结束。";
                if (m_Save.Tutorial.Status != TutorialStatus.Active) return "当前没有进行中的教学。";
                if (m_Save.Recovery.ItemCount() > 0) return "请先领取待接收物品，再检查补给。";
                string key = "tutorial/supply/" + m_Save.Tutorial.SupplyCycle;
                if (m_Save.Receipts.ContainsKey(key)) return "本轮补给已领取。请整理装备后出战。";
                var next = Copy(); var graph = next.Inventory;
                bool changed = false;
                if (!graph.Items.Any(i => TutorialRules.IsWeapon(i.Code)))
                {
                    if (graph.AddSupply("pistol", 1, "stash", emergencySupply: true) != InventoryError.None) return "仓库空间不足，请先整理仓库。";
                    changed = true;
                }
                foreach (var supply in new[] { (code: "cells", minimum: 3), (code: "medkit", minimum: 1) })
                {
                    int missing = Math.Max(0, supply.minimum - graph.Count(supply.code));
                    if (missing == 0) continue;
                    if (graph.AddSupply(supply.code, missing, "stash", emergencySupply: true) != InventoryError.None)
                        return "仓库空间不足，请先整理仓库。";
                    changed = true;
                }
                if (!changed) return "必要物资已齐备，请从仓库装入随身装备。";
                next.Receipts[key] = "1";
                Commit(next);
                return "教学补给已放入仓库。";
            }
        }

        private static void FinishTutorialAttempt(SaveData save, bool extracted)
        {
            if (save.Tutorial.Status != TutorialStatus.Active) return;
            if (extracted && save.Campaign.CompletedTasks.Contains("CM03")) save.Tutorial.Status = TutorialStatus.Completed;
            else
            {
                save.Tutorial.SupplyCycle++;
                // Preparation is re-evaluated from inventory; in-raid actions are learned again on retry.
                save.Tutorial.CompletedSteps &= TutorialRules.Equipped | TutorialRules.Cells | TutorialRules.Medicine;
            }
        }
    }
}
