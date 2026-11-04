using Unity.Entities;
using Unity.Mathematics;
using Unity.MP_FPS;
using Unity.NetCode;
using UnityEngine;
using UnityEngine.SceneManagement;

public partial class ClientInputReaderSystem
{
    public Entity HaloTransferCandidate { get; private set; }
    private Entity _transferTarget;
    private bool _transferGestureHeld;

    private void ReadHaloTransfer(Entity local, in PredictedPlayerGhost sender, bool allowed, ref PlayerInput input)
    {
        bool held = allowed && _dollSingerInput.TransferHeld && !input.FreeLooking && !input.FireSuppressed;
        HaloTransferCandidate = Entity.Null;
        var camera = _dollSingerPresentation.OwnedCamera;
        if (allowed && camera && HaloTransferRules.CanSend(sender) && !HaloTransferRules.Occupied(sender))
        {
            float best = .978f; // A narrow camera-centre cone, with world distance checked separately.
            Vector3 from = HaloTransferRules.Anchor(sender);
            var physics = _dollSingerPresentation.gameObject.scene.GetPhysicsScene();
            foreach (var (candidate, identity, owner, entity) in SystemAPI.Query<RefRO<PredictedPlayerGhost>,
                         RefRO<GhostInstance>, RefRO<GhostOwner>>().WithEntityAccess())
            {
                if (entity == local || identity.ValueRO.ghostId <= 0 || !HaloTransferRules.CanReceive(candidate.ValueRO) ||
                    !HaloTransferRules.Teammates(sender, candidate.ValueRO, true, owner.ValueRO.NetworkId > 0)) continue;
                Vector3 to = HaloTransferRules.Anchor(candidate.ValueRO);
                if (!HaloTransferRules.InRange(from, to)) continue;
                float dot = Vector3.Dot(camera.transform.forward, (to - camera.transform.position).normalized);
                if (dot <= best) continue;
                Vector3 delta = to - from;
                if (delta.sqrMagnitude > .0001f && physics.Raycast(from, delta.normalized, out _, delta.magnitude,
                        (1 << (int)LayerIndex.Default) | (1 << (int)LayerIndex.Ground), QueryTriggerInteraction.Ignore)) continue;
                best = dot;
                HaloTransferCandidate = entity;
            }
        }
        if (held && !_transferGestureHeld) _transferTarget = HaloTransferCandidate;
        if (!held) _transferTarget = Entity.Null;
        _transferGestureHeld = held;
        input.SetFlag(PlayerInput.InputFlag.HaloTransfer, held);
        if (held && EntityManager.HasComponent<GhostInstance>(_transferTarget))
        {
            var target = EntityManager.GetComponentData<GhostInstance>(_transferTarget);
            input.HaloTransferGhostId = target.ghostId;
            input.HaloTransferSpawnTick = target.spawnTick.SerializedData;
        }
    }
}
