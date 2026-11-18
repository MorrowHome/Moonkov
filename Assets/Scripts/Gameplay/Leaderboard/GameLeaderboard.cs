// 实体同步的比赛分数和击杀播报入口。服务器写分数并排队发送播报，客户端读分数供记分板显示。
// 远征角色离场也有清理入口，以免下一局继续显示离场角色。
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;
using Unity.MP_FPS.UI;

// This is a GhostMonoBehaviour that will be spawned as a singleton manager for the leaderboard.
// It can be found on the client using FindObjectOfType<LeaderboardManager>()
// On the server, its entity can be found by querying for the PlayerScoreEntry buffer.
namespace Gameplay.Leaderboard {
    [ResetOnPlayMode("ResetStaticState")]
    public class LeaderboardManager : GhostMonoBehaviour, IUpdateServer, IUpdateClient {
        public static LeaderboardManager Instance { get; private set; }

        private struct KillInfo {
            public int KillerId;
            public int VictimId;
        }

        private Queue<KillInfo> _killQueue = new();
        private Queue<FixedString64Bytes> _joinedQueue = new();
#pragma warning disable UDR0001
        // This is reset from ResetOnPlayMode attribute
        // 连接建立时显示组件可能还未 Awake。先记录待加入项，等记分板能用再填入。
        private static Queue<(int networkId, FixedString64Bytes playerName)> _pendingPlayers = new();
        private static readonly HashSet<int> s_RetiredRaidActors = new();
#pragma warning restore UDR0001

        protected static void ResetStaticState() {
            _pendingPlayers.Clear();
            _pendingPlayers = null;
        }

        // 快照同步的分数缓冲按网络编号识别人。服务端写入数字，客户端查询给记分板。
        public struct PlayerScoreEntry : IBufferElementData {
            [GhostField] public int NetworkId; // Used as the unique key for a player
            [GhostField] public FixedString64Bytes PlayerName;
            [GhostField] public int Kills;
            [GhostField] public int Deaths;
        }

        private void Awake() {
            if (Instance != null && Instance != this) {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        public static void AddPlayer(int networkId, FixedString64Bytes playerName) {
            s_RetiredRaidActors.Remove(networkId);
            _pendingPlayers.Enqueue((networkId, playerName));
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRetiredActors() { s_RetiredRaidActors.Clear(); }

        // 远征角色正常离场要清掉对应记分项。留下退役编号，免得有队列里的加入记录又加回来。
        public static void RetireRaidActor(int networkId) {
            // Cancel an addition even if the manager has not consumed the spawn queue.
            int count = _pendingPlayers.Count;
            for (int i = 0; i < count; i++) {
                var pending = _pendingPlayers.Dequeue();
                if (pending.networkId != networkId) _pendingPlayers.Enqueue(pending);
            }
            s_RetiredRaidActors.Add(networkId);
        }

        public void RemovePlayer(int networkId) {
            if (Role != MultiplayerRole.Server) {
                Debug.LogWarning("RemovePlayer can only be called on the server.");
                return;
            }
            if (GhostGameObject == null || !GhostGameObject.IsGhostLinked() || !GhostGameObject.World.IsCreated) return;
            var buffer = GhostGameObject.GetGhostDynamicBuffer<PlayerScoreEntry>();
            for (int i = 0; i < buffer.Length; i++)
                if (buffer[i].NetworkId == networkId) {
                    buffer.RemoveAt(i);
                    return;
                }
        }

        // 一次确认的击杀更新双方的分数或播报。进入具体模式前先确认需要记录哪类角色。
        public void AddKill(int killer, int victim) {
            if (Role != MultiplayerRole.Server) {
                Debug.LogWarning("AddKill can only be called on the server.");
                return;
            }
            if (GhostGameObject == null || !GhostGameObject.IsGhostLinked() || !GhostGameObject.World.IsCreated) {
                Debug.LogWarning("[SERVER] NOT LINKED!");
                return;
            }
            var buffer = GhostGameObject.GetGhostDynamicBuffer<PlayerScoreEntry>();
            for (int i = 0; i < buffer.Length; i++)
                if (buffer[i].NetworkId == killer) {
                    var entry = buffer[i];
                    entry.Kills++;
                    buffer[i] = entry;
                    break;
                }
            for (int i = 0; i < buffer.Length; i++)
                if (buffer[i].NetworkId == victim) {
                    var entry = buffer[i];
                    entry.Deaths++;
                    buffer[i] = entry;
                    break;
                }
            AnnounceKillFeed(killer, victim);
        }

        public void AnnouncePlayerJoined(FixedString64Bytes playerName) { _joinedQueue.Enqueue(playerName); }

        private void AnnounceKillFeed(int killer, int victim) {
            _killQueue.Enqueue(new KillInfo { KillerId = killer, VictimId = victim });
        }

        public void AddDeath(int networkId) {
            if (Role != MultiplayerRole.Server) {
                Debug.LogWarning("AddDeath can only be called on the server.");
                return;
            }
            if (!GhostGameObject.IsGhostLinked()) return;
            var buffer = GhostGameObject.GetGhostDynamicBuffer<PlayerScoreEntry>();
            for (int i = 0; i < buffer.Length; i++)
                if (buffer[i].NetworkId == networkId) {
                    var entry = buffer[i];
                    entry.Deaths++;
                    buffer[i] = entry;
                    return;
                }
        }

        // 取当前记分缓冲的快照给界面，调用者可自行排序而不影响服务端缓冲。
        public List<PlayerScoreEntry> GetScores() {
            var scores = new List<PlayerScoreEntry>();
            if (GhostGameObject.IsGhostLinked()) {
                var buffer = GhostGameObject.GetGhostDynamicBuffer<PlayerScoreEntry>();
                foreach (var entry in buffer) scores.Add(entry);
            }
            return scores;
        }

        // 服务器从待发送队列取加入和击杀事件，打包成轻量 RPC 给客户端。
        public void UpdateServer(float deltaTime) {
            if (!GhostGameObject.IsGhostLinked()) {
                Debug.Log("[Server] LeaderboardManager not linked yet.");
                return;
            }
            var buffer = GhostGameObject.GetGhostDynamicBuffer<PlayerScoreEntry>();
            while (_pendingPlayers.Count > 0) {
                (int networkId, var playerName) = _pendingPlayers.Dequeue();
                bool alreadyExists = false;
                for (int i = 0; i < buffer.Length; i++)
                    if (buffer[i].NetworkId == networkId) {
                        alreadyExists = true;
                        Debug.LogWarning(
                            $"[Server] Player {networkId} already exists in leaderboard, skipping add from queue.");
                        break;
                    }
                if (!alreadyExists) {
                    buffer.Add(new PlayerScoreEntry {
                        NetworkId = networkId,
                        PlayerName = playerName,
                        Kills = 0,
                        Deaths = 0
                    });
                    Debug.Log($"[Server] Added player {networkId} ({playerName}) to leaderboard from queue.");

                    // Announce join *after* successfully adding to the buffer
                    AnnouncePlayerJoined(playerName);
                }
            }
            while (_killQueue.Count > 0) {
                var kill = _killQueue.Dequeue();
                FixedString64Bytes killerName = "[Unknown]";
                FixedString64Bytes victimName = "[Unknown]";

                // Find names from the buffer
                for (int i = 0; i < buffer.Length; i++) {
                    if (buffer[i].NetworkId == kill.KillerId) killerName = buffer[i].PlayerName;
                    if (buffer[i].NetworkId == kill.VictimId) victimName = buffer[i].PlayerName;
                }

                // Create and broadcast the RPC
                var killFeedRpc = new KillFeedEntryRpc {
                    KillerName = killerName,
                    VictimName = victimName
                };
                GhostGameObject.BroadcastRPC(killFeedRpc);
                ActionFeed.Instance.AnnounceKill(killerName.ToString(), victimName.ToString());
            }
            while (_joinedQueue.Count > 0) {
                var playerName = _joinedQueue.Dequeue();
                var joinRpc = new PlayerJoinedEntryRpc {
                    PlayerName = playerName
                };
                GhostGameObject.BroadcastRPC(joinRpc);
                ActionFeed.Instance.AnnouncePlayerJoined(playerName.ToString());
            }
            // Keep names available until this frame's kill feed has been emitted.
            for (int i = buffer.Length - 1; i >= 0; i--)
                if (s_RetiredRaidActors.Contains(buffer[i].NetworkId))
                    buffer.RemoveAt(i);
            s_RetiredRaidActors.Clear();
        }

        // 客户端消费收到的播报消息，把网络编号换成需要显示的名字。
        public void UpdateClient(float deltaTime) {
            if (!GhostGameObject.IsGhostLinked()) return;

            // Consume any kill feed RPCs received this frame
            while (GhostGameObject.ConsumeRPC(out KillFeedEntryRpc killFeedRpc))
                // Invoke the event for the UI to handle
                ActionFeed.Instance.AnnounceKill(killFeedRpc.KillerName.ToString(), killFeedRpc.VictimName.ToString());
            while (GhostGameObject.ConsumeRPC(out PlayerJoinedEntryRpc joinRpc))
                ActionFeed.Instance.AnnouncePlayerJoined(joinRpc.PlayerName.ToString());
        }
    }

    public struct KillFeedEntryRpc : IRpcCommand {
        public FixedString64Bytes KillerName;
        public FixedString64Bytes VictimName;
    }

    public struct PlayerJoinedEntryRpc : IRpcCommand {
        public FixedString64Bytes PlayerName;
    }
}