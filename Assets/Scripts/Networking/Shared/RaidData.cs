// 一次远征的阶段、倒计时、撤离进度、携带资源和存档结果。RaidRules 推进阶段，RaidSnapshotV2Rpc 将能展示的部分同步到 HUD。
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;

namespace Unity.MP_FPS {
    public enum RaidPhase : byte {
        Active,
        Extracted,
        Dead,
        TimedOut
    }

    public enum RaidSaveState : byte {
        SessionOnly,
        Saved,
        Saving,
        Retrying
    }

    public enum RaidLoadoutError : byte {
        None,
        InvalidCount,
        InsufficientCells,
        Rejected,
        ResearchSuppliesNoSpace
    }

    // 此组件挂在连接上，角色销毁后仍可用来结算。
    public struct RaidSession : IComponentData {
        public int RaidId;
        public uint SnapshotSequence;
        public uint LoadoutRequestId;
        public RaidPhase Phase;
        public RaidSaveState SaveState;
        // RaidId 是同一连接的逐局编号，SettlementId 是整个存档层认可的唯一出战回执。两者分别挡旧网络请求和旧落盘结果。
        public FixedString64Bytes SettlementId;
        public FixedString64Bytes CellStackId;
        public int Dust, Alloy, Cells;
        public int StashDust, StashAlloy, StashCells;
        // 已收到正式出装和正在等扣装是不同状态。后一种还不能生成角色或再次接受出战请求。
        public bool PersistentDeployment, DeployPending;
        public RaidLoadoutError LoadoutError;
        public byte RequestedCompanions, SpawnedCompanions;
        public int ExpeditionSquadId;
        public double CompanionSpawnUntil;

        // 只供服务端记录正在推进撤离计时的出口索引。
        public int ExtractionIndex;
        public float TimeLeft, ExtractionProgress, SnapshotTimer;
        public int BagCount => Dust + Alloy + Cells;
    }

    // 用一位表示一个地图箱是否已空，供客户端地图显示。不传具体有什么，实际搜刮仍要向服务端查询。
    public struct RaidLootWorld : IComponentData {
        public ulong TakenMask;
        public FixedList512Bytes<float> RespawnTimers;
    }

    public struct RaidPickupRpc : IRpcCommand {
        public int RaidId;
        public int LootId;
    }

    public struct RaidDeployRpc : IRpcCommand {
        public int SettledRaidId, CarryCells;
        public uint RequestId;
    }

    // 可靠快照定向发给连接，观察者和晚到玩家也能收到共用物资状态。
    // V2 把 TakenMask 从 uint 扩成 ulong。换 RPC 类型名
    // 可以令按旧 32 位布局编译的 Burst 缓存失效。
    public struct RaidSnapshotV2Rpc : IRpcCommand {
        public int RaidId;
        public uint Sequence;
        public uint LoadoutRequestId;
        public RaidPhase Phase;
        public RaidSaveState SaveState;
        public int Dust, Alloy, Cells;
        public int StashDust, StashAlloy, StashCells;
        public bool DeployPending;
        public RaidLoadoutError LoadoutError;
        public FixedString64Bytes CellStackId;
        public float TimeLeft, ExtractionRemaining;
        public ulong TakenMask;
    }

    public struct RaidClientState : IComponentData {
        public RaidSnapshotV2Rpc Snapshot;
    }

    public static class RaidRules {
        public const int BagCapacity = 12;
        public const float PickupRange = 3f;
        public const int MaxCompanions = 3;
        public static bool ValidLoadout(int cells) { return cells >= 0 && cells <= BagCapacity; }

        public static bool TryConsumeCell(ref RaidSession session) {
            if (session.Phase != RaidPhase.Active || session.Cells <= 0) return false;
            session.Cells--;
            session.SnapshotTimer = 0;
            return true;
        }

        // 只允许活动阶段一次性变成结束。无持久化时可以马上更新仓库合计，正式档案等待保存回执。
        public static bool TrySettle(ref RaidSession session, RaidPhase outcome, bool awardImmediately = true) {
            if (session.Phase != RaidPhase.Active || outcome == RaidPhase.Active) return false;
            if (outcome == RaidPhase.Extracted && awardImmediately) {
                session.StashDust += session.Dust;
                session.StashAlloy += session.Alloy;
                session.StashCells += session.Cells;
            }
            session.Phase = outcome;
            session.ExtractionProgress = 0;
            session.ExtractionIndex = -1;
            session.SnapshotTimer = 0;
            return true;
        }

        // 重新开始给本连接提高出战号，清空旧局携带、同伴和撤离状态，设置新倒计时。
        public static void BeginNext(ref RaidSession session, float duration) {
            session.RaidId++;
            session.SettlementId = System.Guid.NewGuid().ToString("D");
            session.Phase = RaidPhase.Active;
            session.SpawnedCompanions = 0;
            session.ExpeditionSquadId = 0;
            session.CompanionSpawnUntil = 0;
            session.Dust = session.Alloy = session.Cells = 0;
            session.CellStackId = default;
            session.PersistentDeployment = session.DeployPending = false;
            session.LoadoutError = RaidLoadoutError.None;
            session.ExtractionProgress = session.SnapshotTimer = 0;
            session.ExtractionIndex = -1;
            session.TimeLeft = duration;
        }

        // 等待保存或保存重试还没完成不能再次部署。按钮提交的上一局编号必须正好对应当前已结束记录。
        public static bool CanDeploy(RaidSession session, int settledRaidId) {
            return session.Phase != RaidPhase.Active && session.RaidId == settledRaidId && !session.DeployPending &&
                   session.SaveState != RaidSaveState.Saving && session.SaveState != RaidSaveState.Retrying;
        }

        // 非持久入口可以直接从本次会话仓库扣电池，然后建立新局。正式模式使用另一个异步扣装流程。
        public static bool TryDeploy(ref RaidSession session, int settledRaidId, float duration, int carryCells = 0) {
            if (!CanDeploy(session, settledRaidId) || !ValidLoadout(carryCells) || carryCells > session.StashCells)
                return false;
            session.StashCells -= carryCells;
            BeginNext(ref session, duration);
            session.Cells = carryCells;
            if (carryCells > 0) session.CellStackId = System.Guid.NewGuid().ToString("D");
            return true;
        }
    }
}