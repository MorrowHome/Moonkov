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
                if (MoonRaidMap.Active != null) context.BeginDeploy(connection, join.CarryCells, join);
                else
                {
                    SpawnPlayerCharacter(ref state, ecb, connection, join.Name, join.CharacterIndex);
                    AddPlayerToLeaderboard(SystemAPI.GetComponent<NetworkId>(connection).Value, join.Name);
                }
            }
            foreach (var connection in removeJoins) context.Joins.Remove(connection);

            var removeDeploys = new List<Entity>();
            foreach (var pair in context.Deployments)
            {
                var deploy = pair.Value;
                Entity connection = pair.Key;
                if (MoonRaidMap.Active == null || !SystemAPI.Exists(connection) || !SystemAPI.HasComponent<NetworkId>(connection) || SystemAPI.HasComponent<NetworkStreamRequestDisconnect>(connection))
                {
                    // Retain cleanup independently of the disappearing connection.
                    context.Abandon(deploy.Payload);
                    removeDeploys.Add(connection);
                    continue;
                }
                if (deploy.Task == null)
                {
                    if (SystemAPI.Time.ElapsedTime >= deploy.RetryAt) deploy.Task = context.DeployAsync(deploy.Payload);
                    continue;
                }
                if (!deploy.Task.IsCompleted) continue;
                if (deploy.Task.IsFaulted || deploy.Task.IsCanceled)
                {
                    if (deploy.Task.Exception?.GetBaseException() is RaidPersistenceContext.LoadoutRejectedException rejected)
                    {
                        context.Abandon(deploy.Payload);
                        removeDeploys.Add(connection);
                        if (deploy.Join != null)
                            ecb.AddComponent(connection, new NetworkStreamRequestDisconnect { Reason = NetworkStreamDisconnectReason.ConnectionClose });
                        else
                        {
                            var raid = SystemAPI.GetComponentRW<RaidSession>(connection);
                            raid.ValueRW.DeployPending = false;
                            raid.ValueRW.LoadoutError = rejected.Error;
                            raid.ValueRW.SnapshotTimer = 0;
                        }
                    }
                    else { deploy.Task = null; deploy.RetryAt = SystemAPI.Time.ElapsedTime + 3; }
                    continue;
                }
                var profile = deploy.Task.Result;
                context.Profiles[connection] = profile;
                RaidSession session = deploy.Join == null ? SystemAPI.GetComponent<RaidSession>(connection) : default;
                RaidRules.BeginNext(ref session, MoonRaidMap.Active.RaidDuration);
                session.SettlementId = deploy.Payload.DeploymentId;
                session.PersistentDeployment = true;
                session.SaveState = RaidSaveState.Saved;
                session.Cells = deploy.Payload.Cells;
                session.CellStackId = profile.CellStackId ?? "";
                session.StashDust = profile.Dust; session.StashAlloy = profile.Alloy; session.StashCells = profile.Cells;
                // DeployAsync has validated the complete authoritative kit; never substitute test gear.
                var graph = RaidInventoryTransport.Decode(profile.RaidInventoryJson);
                SetRaidInventory(ref state, connection, graph, session.RaidId);
                RaidInventoryState.UpdateTotals(graph, ref session);
                if (deploy.Join != null)
                {
                    context.ReadyRaids[connection] = session;
                    SpawnPlayerCharacter(ref state, ecb, connection, deploy.Join.Name, deploy.Join.CharacterIndex);
                    AddPlayerToLeaderboard(SystemAPI.GetComponent<NetworkId>(connection).Value, deploy.Join.Name);
                }
                else
                {
                    SystemAPI.SetComponent(connection, session);
                    var joined = SystemAPI.GetComponent<JoinedClient>(connection);
                    SpawnPlayerCharacter(ref state, ecb, connection, joined.PlayerName, joined.CharacterIndex);
                }
                removeDeploys.Add(connection);
            }
            foreach (var connection in removeDeploys) context.Deployments.Remove(connection);

            var removeSaves = new List<string>();
            foreach (var pair in context.Saves)
            {
                var save = pair.Value;
                if (save.Task == null)
                {
                    if (SystemAPI.Time.ElapsedTime >= save.RetryAt) save.Task = context.RetrySave(save);
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
            foreach (var connection in disconnected)
            {
                var profile = context.Profiles[connection];
                if (!string.IsNullOrEmpty(profile.DeploymentId))
                    context.Abandon(new RaidPersistenceContext.Deployment { PlayerId=profile.PlayerId, DeploymentId=profile.DeploymentId, Cells=profile.CarriedCells });
                context.Profiles.Remove(connection);
                context.Inventories.Remove(connection);
            }
        }
    }
}
