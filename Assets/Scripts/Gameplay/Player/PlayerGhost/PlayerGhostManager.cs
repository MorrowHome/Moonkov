// 管理当前已经链接的角色列表。按服务器、自己控制的客户端、其他客户端角色分类，提供注册和注销通知，方便系统在合适的时间找角色。
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace Unity.MP_FPS {
    public class PlayerGhostManager : GhostSingleton<PlayerGhostManager>, IGhostManager {
        public const int k_MaxTotalPlayers = 4;
        private Dictionary<MultiplayerRole, List<PlayerGhost>> m_PlayerGhostsByRole = new();
        public Color CameraClearColour { get; private set; } = Color.black;

        public delegate void PlayerRegisteredCallback(PlayerGhost player);

        public PlayerRegisteredCallback OnPlayerRegistered;

        public delegate void PlayerUnRegisteredCallback(PlayerGhost player);

        public PlayerUnRegisteredCallback OnPlayerUnRegistered;

        // 注册后才向其他子系统发信号，此时调用者拿到的是已经链接的角色。
        public void Register(PlayerGhost player) {
            AddPlayerWithRole(player, player.Role);
            if (player.Role != MultiplayerRole.Server) AddPlayerWithRole(player, MultiplayerRole.ClientAll);
            OnPlayerRegistered?.Invoke(player);
        }

        // 一个角色可放到它特定职责的列表里，客户端全体查询还可包含自己控制的角色。
        private void AddPlayerWithRole(PlayerGhost player, MultiplayerRole role) {
            if (!m_PlayerGhostsByRole.ContainsKey(role)) m_PlayerGhostsByRole.Add(role, new List<PlayerGhost>());
            Debug.Assert(!m_PlayerGhostsByRole[role].Contains(player),
                $"Player {player.gameObject.name} with role {role} already registered to the PlayerGhostManager");
            m_PlayerGhostsByRole[role].Add(player);
        }

        // 角色销毁时从需要的列表一并移除，收到注销事件的系统就能停止引用。
        public void Unregister(PlayerGhost player) {
            RemovePlayerWithRole(player, player.Role);
            if (player.Role != MultiplayerRole.Server) RemovePlayerWithRole(player, MultiplayerRole.ClientAll);
            OnPlayerUnRegistered?.Invoke(player);
        }

        private void RemovePlayerWithRole(PlayerGhost player, MultiplayerRole role) {
            Debug.Assert(m_PlayerGhostsByRole.ContainsKey(role),
                $"Trying to unregister player {player.gameObject.name} but list does not exist");
            Debug.Assert(m_PlayerGhostsByRole[role].Contains(player),
                $"Trying to unregister player {player.gameObject.name} but they aren't in the list");
            m_PlayerGhostsByRole[role].RemoveSwapBack(player);
            if (m_PlayerGhostsByRole[role].Count == 0) m_PlayerGhostsByRole.Remove(role);
        }

        // 调用方可以只取远端，也可以用汇总的客户端列表。房主不把服务器角色误混到画面列表。
        public List<PlayerGhost> GetPlayersByRole(MultiplayerRole role, bool allClients = true) {
            role = allClients && role != MultiplayerRole.Server ? MultiplayerRole.ClientAll : role;
            if (m_PlayerGhostsByRole.ContainsKey(role)) return m_PlayerGhostsByRole[role];
            return default;
        }

        public bool TryGetPlayersByRole(MultiplayerRole role, out List<PlayerGhost> players) {
            if (m_PlayerGhostsByRole.ContainsKey(role)) {
                players = m_PlayerGhostsByRole[role];
                return true;
            }
            players = default;
            return false;
        }

        public int GetNumPlayersByRole(MultiplayerRole role, bool allClients = true) {
            role = allClients && role != MultiplayerRole.Server ? MultiplayerRole.ClientAll : role;
            if (m_PlayerGhostsByRole.ContainsKey(role)) return m_PlayerGhostsByRole[role].Count;
            return 0;
        }
    }
}