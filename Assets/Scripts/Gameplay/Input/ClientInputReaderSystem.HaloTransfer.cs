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
        // 可供 HUD 高亮的队友候选每帧重算。已经按下转移时使用的目标另存，准星稍移也能维持这次选择。
        HaloTransferCandidate = Entity.Null;
        var camera = _dollSingerPresentation.OwnedCamera;
        if (allowed && camera && HaloTransferRules.CanSend(sender) && !HaloTransferRules.Occupied(sender)) {
            float best = .978f; // 用镜头中心附近的窄锥选目标，另外检查真实距离。
            Vector3 from = HaloTransferRules.Anchor(sender);
            var physics = _dollSingerPresentation.gameObject.scene.GetPhysicsScene();
            foreach (var (candidate, identity, owner, entity) in SystemAPI
                         .Query<RefRO<PredictedPlayerGhost>, RefRO<GhostInstance>, RefRO<GhostOwner>>()
                         .WithEntityAccess()) {
                // 先筛能接收保护的队友和真正有网络身份的实体。候选还得在光环锚点的作用距离里。
                if (entity == local || identity.ValueRO.ghostId <= 0 ||
                    !HaloSupportRules.CanProtect(candidate.ValueRO) ||
                    !HaloTransferRules.Teammates(sender, candidate.ValueRO, true, owner.ValueRO.NetworkId > 0))
                    continue;
                Vector3 to = HaloTransferRules.Anchor(candidate.ValueRO);
                if (!HaloTransferRules.InRange(from, to)) continue;
                // 点积越接近一，候选越靠近准星中央。找到更靠中的目标后，还要查两个锚点之间有没有墙。
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
        // 防守键在按住转移时才随指令发。只有目标仍能读出 Ghost 身份，才把具体接收人字段写进去。
        input.SetFlag(PlayerInput.InputFlag.HaloGuard, held && _dollSingerInput.GuardHeld);
        if (held && EntityManager.HasComponent<GhostInstance>(_transferTarget)) {
            var target = EntityManager.GetComponentData<GhostInstance>(_transferTarget);
            input.HaloTransferGhostId = target.ghostId;
            input.HaloTransferSpawnTick = target.spawnTick.SerializedData;
        }
    }
}