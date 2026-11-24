// 每个客户端渲染帧采集键鼠、视角和角色按键，整理成 PlayerInput。ClientInputSenderSystem 再把它按网络 tick 组织。
using Unity.Entities;
using Unity.MP_FPS;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;
using Unity.MP_FPS.DollSinger;

[UpdateInGroup(typeof(GhostInputSystemGroup))]
public partial class ClientInputReaderSystem : SystemBase {
    private float2 _accumulatedLook;
    private Entity _lastKnownPlayerEntity = Entity.Null;
    private DollSingerInput _dollSingerInput;
    private DollSingerNetworkPresentation _dollSingerPresentation;

    protected override void OnUpdate() {
        var player = BindLocalPlayer();
        foreach (var (input, movement) in SystemAPI.Query<RefRW<ClientInput>, RefRW<ClientMovementInput>>()) {
            bool blocked = GameSettings.Instance.IsPauseMenuOpen || RaidHUD.GameplayActionsBlocked;
            var frame = _dollSingerInput != null ? ReadCharacter(player, blocked) : ReadTemplate(blocked);
            var sanitized = frame.Sanitized();
            input.ValueRW.PlayerInput = sanitized;
            movement.ValueRW.PlayerInput = sanitized;
        }
    }

    private Entity BindLocalPlayer() {
        var currentLocalPlayer = Entity.Null;
        foreach (var (transform, entity) in SystemAPI.Query<RefRO<LocalTransform>>()
                     .WithAll<PredictedPlayerGhost, GhostOwnerIsLocal>().WithEntityAccess()) {
            currentLocalPlayer = entity;
            if (entity != _lastKnownPlayerEntity) {
                var forward = math.mul(transform.ValueRO.Rotation, new float3(0, 0, 1));
                _accumulatedLook = new float2(math.degrees(math.atan2(forward.x, forward.z)), 0f);
            }
            break;
        }
        if (currentLocalPlayer != _lastKnownPlayerEntity) {
            _lastKnownPlayerEntity = currentLocalPlayer;
            _dollSingerInput = null;
            _dollSingerPresentation = null;
            _transferGestureHeld = false;
            _transferTarget = HaloTransferCandidate = Entity.Null;
        }
        if (_dollSingerInput == null && currentLocalPlayer != Entity.Null &&
            EntityManager.HasComponent<GhostGameObjectLink>(currentLocalPlayer)) {
            var link = EntityManager.GetComponentObject<GhostGameObjectLink>(currentLocalPlayer);
            if (link.LinkedInstance != null &&
                link.LinkedInstance.TryGetComponent<DollSingerNetworkPresentation>(out var presentation)) {
                _dollSingerInput = presentation.OwnedInput;
                _dollSingerPresentation = presentation;
            }
        }
        return currentLocalPlayer;
    }

    private PlayerInput ReadCharacter(Entity currentLocalPlayer, bool blocked) {
        var playerInput = new PlayerInput { HaloLightingSettings = _dollSingerInput.LightingSettings };
        bool canRead = !blocked && _dollSingerInput.CanReadPlayerInput;
        playerInput.SetFlag(PlayerInput.InputFlag.HaloLightDisabled,
            !_dollSingerPresentation.ReadOwnedLight(canRead));
        playerInput.FreeLooking = _dollSingerPresentation.UpdateOwnedLook(ref _accumulatedLook,
            out playerInput.FreeLookOffset, !canRead);
        playerInput.Lean =
            (canRead || _dollSingerInput.LightAdjusting) && !_dollSingerPresentation.IsThirdPerson
                ? _dollSingerPresentation.OwnedLeanAmount
                : 0f;
        playerInput.MoveInput = canRead ? _dollSingerInput.Move : Vector2.zero;
        playerInput.SetStance(_dollSingerInput.Stance);
        playerInput.SetFlag(PlayerInput.InputFlag.Jump, canRead && _dollSingerInput.JumpPressed);
        playerInput.SetFlag(PlayerInput.InputFlag.Sprint, canRead && _dollSingerInput.SprintHeld);
        playerInput.SetFlag(PlayerInput.InputFlag.Aim,
            !blocked && !RaidHUD.DarkSightRequested && !RaidHUD.QuickSlotToolSelected &&
            (_dollSingerInput.LightAdjusting
                ? _dollSingerInput.LightPreviewAiming
                : canRead && _dollSingerInput.AimHeld) && !playerInput.FreeLooking);
        playerInput.SetFlag(PlayerInput.InputFlag.ThirdPerson, _dollSingerPresentation.IsThirdPerson);
        var equipped = EntityManager.GetComponentData<PredictedPlayerGhost>(currentLocalPlayer);
        var weapon = WeaponManager.Instance.WeaponRegistry.GetWeaponData(equipped.EquippedWeaponID);
        bool canFire = canRead && _dollSingerPresentation.WeaponHandsAvailable && !RaidHUD.DarkSightRequested &&
                       !RaidHUD.QuickSlotToolSelected && !playerInput.FreeLooking;
        playerInput.SetFlag(PlayerInput.InputFlag.FireSuppressed, !canFire);
        playerInput.SetFlag(PlayerInput.InputFlag.FireHeld, canFire && _dollSingerInput.FireHeld);
        playerInput.SetFlag(PlayerInput.InputFlag.Shoot,
            canFire && (weapon != null && !weapon.Automatic && weapon.BurstCount <= 1
                ? _dollSingerInput.FirePressed
                : _dollSingerInput.FireHeld));
        playerInput.SetFlag(PlayerInput.InputFlag.EquipHalo,
            canRead && _dollSingerInput.WeaponSlotPressed == 1);
        playerInput.SetFlag(PlayerInput.InputFlag.EquipRevolver,
            canRead && _dollSingerInput.WeaponSlotPressed == 2);
        playerInput.SetFlag(PlayerInput.InputFlag.EquipPistol,
            canRead && _dollSingerInput.WeaponSlotPressed == 3);
        playerInput.SetFlag(PlayerInput.InputFlag.Reload, canRead && _dollSingerInput.ReloadPressed);
        playerInput.SetFlag(PlayerInput.InputFlag.HaloDefense,
            !RaidHUD.DarkSightRequested &&
            ((canRead && _dollSingerInput.DefenseHeld && !RaidHUD.QuickSlotToolSelected) ||
             (RaidHUD.InventoryDefenseAllowed && DollSingerInput.DefenseButtonHeld)));
        playerInput.LookYawPitchDegrees = _accumulatedLook;
        playerInput.AimPoint =
            _dollSingerPresentation.CaptureAimPoint(_accumulatedLook,
                weapon != null ? weapon.HitscanRange : 100f);
        ReadHaloTransfer(currentLocalPlayer, equipped, canFire, ref playerInput);
        TutorialRuntime.Observe(playerInput);
        InGameHUD.ObserveHaloChargeInput(playerInput, equipped);
        return playerInput;
    }

    private PlayerInput ReadTemplate(bool blocked) {
        var frame = new PlayerInput();
        if (blocked) {
            frame.SetFlag(PlayerInput.InputFlag.FireSuppressed, true);
            return frame;
        }
        var user = InputSystemManager.GetFirstInputUser();
        if (!user.valid || !(user.actions is InputSystem_Actions controls)) return frame;

        frame.SetFlag(PlayerInput.InputFlag.Jump, controls.Player.Jump.triggered);
        bool fire = controls.FPS.ShootSingle.IsPressed();
        frame.SetFlag(PlayerInput.InputFlag.Shoot, fire);
        frame.SetFlag(PlayerInput.InputFlag.FireHeld, fire);
        frame.SetFlag(PlayerInput.InputFlag.Reload, controls.FPS.Reload.triggered);
        frame.MoveInput = controls.Player.Move.ReadValue<Vector2>();
        var look = controls.Player.LookDelta.ReadValue<Vector2>() * 3.7f;
        _accumulatedLook.x += look.x;
        _accumulatedLook.y = math.clamp(_accumulatedLook.y - look.y, -85f, 85f);
        frame.LookYawPitchDegrees = _accumulatedLook;
        TutorialRuntime.Observe(frame);
        return frame;
    }
}
