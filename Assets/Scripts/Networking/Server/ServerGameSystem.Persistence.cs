using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;

namespace Unity.MP_FPS
{
    public partial struct ServerGameSystem
    {
        private Entity m_PersistenceEntity;

        private RaidPersistenceContext Persistence(ref SystemState state) =>
            state.EntityManager.GetComponentObject<RaidPersistenceContext>(m_PersistenceEntity);

        public void OnDestroy(ref SystemState state)
        {
            if (state.EntityManager.Exists(m_PersistenceEntity)) Persistence(ref state).Dispose();
        }

        private void PollPersistence(ref SystemState state, EntityCommandBuffer ecb)
        {
            var context = Persistence(ref state);
            if (!context.Enabled) return;
            var removeJoins = new List<Entity>();
            foreach (var pair in context.Joins)
            {
                Entity connection = pair.Key;
                var join = pair.Value;
                if (!join.Task.IsCompleted) continue;
                if (!SystemAPI.Exists(connection) || !SystemAPI.HasComponent<NetworkId>(connection) ||
                    SystemAPI.HasComponent<NetworkStreamRequestDisconnect>(connection))
                {
                    removeJoins.Add(connection);
                    continue;
                }
                if (join.Task.IsFaulted || join.Task.IsCanceled)
                {
                    removeJoins.Add(connection);
                    Debug.LogError("[Raid] Cannot load persistent profile; connection rejected. " + join.Task.Exception?.GetBaseException().GetType().Name);
                    ecb.AddComponent(connection, new NetworkStreamRequestDisconnect { Reason = NetworkStreamDisconnectReason.ConnectionClose });
                    continue;
                }
                var profile = join.Task.Result;
                // A reconnect must wait for an earlier outbox receipt, then reload the committed stash.
                bool pendingReceipt = false;
                foreach (var save in context.Saves.Values)
                    if (save.Payload.PlayerId == profile.PlayerId) pendingReceipt = true;
                if (pendingReceipt) { join.NeedsRefresh = true; continue; }
                if (join.NeedsRefresh) { context.RefreshJoin(join); continue; }
                removeJoins.Add(connection);
                // A copied guest credential cannot enter two simultaneous sessions in this World.
                bool alreadyPlaying = false;
                foreach (var existing in context.Profiles)
                    if (existing.Key != connection && SystemAPI.Exists(existing.Key) && existing.Value.PlayerId == profile.PlayerId)
                        alreadyPlaying = true;
                if (alreadyPlaying)
                {
                    Debug.LogWarning("[Raid] This profile is already connected to this server.");
                    ecb.AddComponent(connection, new NetworkStreamRequestDisconnect { Reason = NetworkStreamDisconnectReason.ConnectionClose });
                    continue;
                }
                context.Profiles[connection] = profile;
                join.Name.CopyFromTruncated(profile.DisplayName);
                SpawnPlayerCharacter(ref state, ecb, connection, join.Name, join.CharacterIndex);
                AddPlayerToLeaderboard(SystemAPI.GetComponent<NetworkId>(connection).Value, join.Name);
            }
            foreach (var connection in removeJoins) context.Joins.Remove(connection);

            var removeSaves = new List<string>();
            foreach (var pair in context.Saves)
            {
                var save = pair.Value;
                if (save.Task == null)
                {
                    if (SystemAPI.Time.ElapsedTime >= save.RetryAt) save.Task = context.SaveAsync(save.Payload);
                    continue;
                }
                if (!save.Task.IsCompleted) continue;
                bool matching = SystemAPI.Exists(save.Connection) && SystemAPI.HasComponent<RaidSession>(save.Connection) &&
                    SystemAPI.GetComponent<RaidSession>(save.Connection).SettlementId.ToString() == pair.Key;
                if (save.Task.IsFaulted || save.Task.IsCanceled)
                {
                    if (!save.Warned)
                    {
                        Debug.LogWarning("[Raid] Settlement pending; receipt retained and retrying. " + save.Task.Exception?.GetBaseException().GetType().Name);
                        save.Warned = true;
                    }
                    save.Task = null;
                    save.RetryAt = SystemAPI.Time.ElapsedTime + 3;
                    if (matching)
                    {
                        var session = SystemAPI.GetComponentRW<RaidSession>(save.Connection);
                        session.ValueRW.SaveState = RaidSaveState.Retrying;
                        session.ValueRW.SnapshotTimer = 0;
                    }
                    continue;
                }
                var profile = save.Task.Result;
                if (matching)
                {
                    var session = SystemAPI.GetComponentRW<RaidSession>(save.Connection);
                    session.ValueRW.StashDust = profile.Dust;
                    session.ValueRW.StashAlloy = profile.Alloy;
                    session.ValueRW.StashCells = profile.Cells;
                    session.ValueRW.SaveState = RaidSaveState.Saved;
                    session.ValueRW.SnapshotTimer = 0;
                    context.Profiles[save.Connection] = profile;
                }
                removeSaves.Add(pair.Key);
            }
            foreach (var key in removeSaves) context.Saves.Remove(key);
            var disconnected = new List<Entity>();
            foreach (var connection in context.Profiles.Keys)
                if (!SystemAPI.Exists(connection)) disconnected.Add(connection);
            foreach (var connection in disconnected) context.Profiles.Remove(connection);
        }
    }
}
