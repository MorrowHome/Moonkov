// 局内库存的网络协议。分片传输同一份快照，RPC 字段也供已有客户端读取。
using Newtonsoft.Json;
using System.Collections.Generic;
using Unity.Collections;
using Unity.NetCode;
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS {
    public static class RaidInventoryTransport {
        // 每段 ASCII JSON 要明显小于传输层 1378 字节的包上限。
        public const int ChunkCharacters = 400;
        public const int MaxChunks = 2048;
        public const int MaxJsonCharacters = 800000;

        private static readonly JsonSerializerSettings JsonSettings = new() {
            StringEscapeHandling = StringEscapeHandling.EscapeNonAscii
        };

        // 纯库存编码给档案。如果还要表示搜刮箱、动作与结算页信息，调用 EncodeSnapshot 将它们放同一个包。
        public static string Encode(InventoryGraph graph) {
            return JsonConvert.SerializeObject(graph, JsonSettings);
        }

        public static InventoryGraph Decode(string json) {
            return JsonConvert.DeserializeObject<InventoryGraph>(json);
        }

        public static string EncodeSnapshot(
            InventoryGraph graph,
            int lootId,
            int lootVersion,
            int dissectingLootId = -1,
            float dissectionRemaining = 0,
            string medicalItemId = null,
            float medicalRemaining = 0,
            string actionFeedback = null,
            int corpseCharacterIndex = -1,
            Dictionary<string, string> corpseEquipmentSlots = null,
            RaidResultManifest result = null,
            bool medicalIsConsumable = false,
            int searchTotal = -1,
            List<RaidSearchFootprint> searchFootprints = null,
            float searchItemSeconds = 0) {
            return JsonConvert.SerializeObject(new RaidInventorySnapshot {
                    Graph = graph, LootId = lootId, LootVersion = lootVersion,
                    DissectingLootId = dissectingLootId, DissectionRemaining = dissectionRemaining,
                    MedicalItemId = medicalItemId, MedicalRemaining = medicalRemaining,
                    MedicalIsConsumable = medicalIsConsumable, ActionFeedback = actionFeedback,
                    CorpseCharacterIndex = corpseCharacterIndex, CorpseEquipmentSlots = corpseEquipmentSlots,
                    Result = result, SearchTotal = searchTotal, SearchFootprints = searchFootprints,
                    SearchItemSeconds = searchItemSeconds
                }, JsonSettings);
        }
    }

    public sealed class RaidSearchFootprint {
        public string Id, Region;
        public int X, Y, Width, Height;
    }

    public sealed class RaidInventorySnapshot {
        public RaidResultManifest Result;
        public InventoryGraph Graph;
        public int LootId = -1, LootVersion;
        public int CorpseCharacterIndex = -1;
        public Dictionary<string, string> CorpseEquipmentSlots;
        public int DissectingLootId = -1;
        public float DissectionRemaining;
        public int SearchTotal = -1;
        public List<RaidSearchFootprint> SearchFootprints;
        public float SearchItemSeconds;
        public string MedicalItemId;
        public float MedicalRemaining;
        public bool MedicalIsConsumable;
        public string ActionFeedback;
    }

    public struct RaidInventoryMoveRpc : IRpcCommand {
        public int RaidId, ExpectedVersion, X, Y, Quantity;
        public uint RequestId;
        public InventoryOperation Operation;
        public FixedString64Bytes ItemId, Parent, Region, TargetId;
        public bool Rotated;
    }

    public struct RaidInventoryDropRpc : IRpcCommand {
        public int RaidId, ExpectedVersion;
        public uint RequestId;
        public FixedString64Bytes ItemId;
    }

    public struct RaidActionCancelRpc : IRpcCommand {
        public int RaidId;
        public uint RequestId;
    }

    public struct RaidMedicalUseRpc : IRpcCommand {
        public int RaidId, ExpectedVersion;
        public uint RequestId;
        public FixedString64Bytes ItemId;
        public BodyPart Part;
    }

    public struct RaidConsumableUseRpc : IRpcCommand {
        public int RaidId, ExpectedVersion;
        public uint RequestId;
        public FixedString64Bytes ItemId;
    }

    public struct RaidLootOpenRpc : IRpcCommand {
        public int RaidId, LootId;
        public uint RequestId;
    }

    public sealed class RaidCorpseRecord {
        public Mathematics.float3 Position;
        public Mathematics.quaternion Rotation;
        public int CharacterIndex;
        public Dictionary<string, string> EquipmentSlots;
        public double CreatedAt;
        public double EmptySince = -1;
    }

    // 必须与较早的遗体标记 RPC 保持不同类型。增加尸体表现数据后，
    // 生成的序列化和 Burst 代码不能继续用原来较小的布局。
    public struct RaidCorpseRpc : IRpcCommand {
        public int LootId, Version, CharacterIndex;
        public Mathematics.float3 Position;
        public Mathematics.quaternion Rotation;
        public float Age;
        public bool Empty;
        public bool RequiresDissection => CharacterIndex == -1 && Version == 1;
    }

    // 显示状态与元数据一起更新，用新的序列化类型标识。
    public struct RaidGroundItemMarkerRpc : IRpcCommand {
        public FixedString64Bytes ItemCode;
        public uint Sequence;
        public int LootId, Version, CharacterIndex;
        public Mathematics.float3 Position;
        public Mathematics.quaternion Rotation;
        public float Age;
        public bool Empty, Removed, Deleted;
    }

    public struct RaidLootMoveRpc : IRpcCommand {
        public int RaidId, LootId, ExpectedLootVersion, ExpectedVersion, X, Y, Quantity;
        public uint RequestId;
        public InventoryOperation Operation;
        public FixedString64Bytes ItemId, Parent, Region, TargetId;
        public bool Rotated;
    }

    // V2 使用 512 字节字符串，类型名称与旧 4096 字节 RPC 分开，
    // 生成的 RPC 接收器和 Burst 不能重用旧布局。
    public struct RaidInventoryChunkV2Rpc : IRpcCommand {
        public int RaidId, Index, Count;
        public uint Sequence, RequestId;
        public InventoryError Error;
        public FixedString512Bytes Json;
    }

    // 未到齐的数据不交给 UI，更高序号一到便放弃旧分片。
}
