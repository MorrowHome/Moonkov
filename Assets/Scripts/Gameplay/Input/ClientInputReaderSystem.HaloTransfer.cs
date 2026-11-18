// 采集光环布置和支援操作。客户端用准星、距离和遮挡挑候选队友，提交 Ghost 身份后由服务器再次验证转移请求。
using Unity.Entities;
using Unity.Mathematics;
using Unity.MP_FPS;
using Unity.NetCode;
using UnityEngine;
using UnityEngine.SceneManagement;

public partial class ClientInputReaderSystem {
    public Entity HaloTransferCandidate { get; private set; }
    private Entity _transferTarget;
    private bool _transferGestureHeld;

    private void ReadHaloTransfer(Entity local, in PredictedPlayerGhost sender, bool allowed, ref PlayerInput input) {
        input.SetFlag(PlayerInput.InputFlag.HaloAnchor, allowed && _dollSingerInput.HaloAnchorHeld);
        input.SetFlag(PlayerInput.InputFlag.HaloMode, allowed && _dollSingerInput.HaloModeHeld);
        input.SetFlag(PlayerInput.InputFlag.HaloScan, allowed && _dollSingerInput.HaloScanHeld);
        bool held = allowed && _dollSingerInput.TransferHeld && !input.FreeLooking && !input.FireSuppressed;
        HaloTransferCandidate = Entity.Null;
        var camera = _dollSingerPresentation.OwnedCamera;
        if (allowed && camera && HaloTransferRules.CanSend(sender) && !HaloTransferRules.Occupied(sender)) {
            float best = .978f; // A narrow camera-centre cone, with world distance checked separately.
            Vector3 from = HaloTransferRules.Anchor(sender);
            var physics = _dollSingerPresentation.gameObject.scene.GetPhysicsScene();
            foreach (var (candidate, identity, owner, entity) in SystemAPI
                         .Query<RefRO<PredictedPlayerGhost>, RefRO<GhostInstance>, RefRO<GhostOwner>>()
                         .WithEntityAccess()) {
                if (entity == local || identity.ValueRO.ghostId <= 0 ||
                    !HaloSupportRules.CanProtect(candidate.ValueRO) ||
                    !HaloTransferRules.Teammates(sender, candidate.ValueRO, true, owner.ValueRO.NetworkId > 0))
                    continue;
                Vector3 to = HaloTransferRules.Anchor(candidate.ValueRO);
                if (!HaloTransferRules.InRange(from, to)) continue;
                float dot = Vector3.Dot(camera.transform.forward, (to - camera.transform.position).normalized);
                if (dot <= best) continue;
                var delta = to - from;
                if (delta.sqrMagnitude > .0001f && physics.Raycast(from, delta.normalized, out _, delta.magnitude,
                        (1 << (int)LayerIndex.Default) | (1 << (int)LayerIndex.Ground), QueryTriggerInteraction.Ignore))
                    continue;
                best = dot;
                HaloTransferCandidate = entity;
            }
        }
        // 按下时锁定这一轮的目标，继续按住保持原选择。Ghost 的编号配上出生 tick，避免编号被新实体复用时把光环交错人。
        if (held && !_transferGestureHeld) _transferTarget = HaloTransferCandidate;
        if (!held) _transferTarget = Entity.Null;
        _transferGestureHeld = held;
        input.SetFlag(PlayerInput.InputFlag.HaloTransfer, held);
        input.SetFlag(PlayerInput.InputFlag.HaloGuard, held && _dollSingerInput.GuardHeld);
        if (held && EntityManager.HasComponent<GhostInstance>(_transferTarget)) {
            var target = EntityManager.GetComponentData<GhostInstance>(_transferTarget);
            input.HaloTransferGhostId = target.ghostId;
            input.HaloTransferSpawnTick = target.spawnTick.SerializedData;
        }
    }
}