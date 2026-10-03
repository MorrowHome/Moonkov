using Unity.Entities;
using Unity.MP_FPS;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;
using Unity.MP_FPS.DollSinger;

[UpdateInGroup(typeof(GhostInputSystemGroup))]
public partial class ClientInputReaderSystem : SystemBase
{
    private float2 _accumulatedLook;

    private Entity _lastKnownPlayerEntity = Entity.Null;
    private DollSingerInput _dollSingerInput;
    private DollSingerNetworkPresentation _dollSingerPresentation;

    protected override void OnUpdate()
    {
        Entity currentLocalPlayer = Entity.Null;
        float3 playerForward = new float3(0, 0, 1);

        // 1. Find the local player entity and its server-authored spawn facing.
        foreach (var (transform, ghost, owner, entity) in SystemAPI.Query<
                         RefRO<LocalTransform>,
                         RefRO<PredictedPlayerGhost>,
                         RefRO<GhostOwnerIsLocal>>()
                     .WithEntityAccess())
        {
            currentLocalPlayer = entity;
            playerForward = math.mul(transform.ValueRO.Rotation, new float3(0, 0, 1));
            break; // Found local player, stop searching
        }

        // 2. Check for Respawn (Entity ID changed)
        if (currentLocalPlayer != Entity.Null)
        {
            if (currentLocalPlayer != _lastKnownPlayerEntity)
            {
                // Each map owns spawn facing; the old arena's centre is not a universal look target.
                float yawRadians = math.atan2(playerForward.x, playerForward.z);
                _accumulatedLook = new float2(math.degrees(yawRadians), 0f);

                // Update tracker so we don't reset again while this character is alive
                _lastKnownPlayerEntity = currentLocalPlayer;
                _dollSingerInput = null;
                _dollSingerPresentation = null;
            }
        }
        else
        {
            // Player is dead or not yet spawned.
            // Reset the tracker so the *next* spawn triggers the logic.
            _lastKnownPlayerEntity = Entity.Null;
            _dollSingerInput = null;
            _dollSingerPresentation = null;
        }

        if (_dollSingerInput == null && currentLocalPlayer != Entity.Null &&
            EntityManager.HasComponent<GhostGameObjectLink>(currentLocalPlayer))
        {
            var link = EntityManager.GetComponentObject<GhostGameObjectLink>(currentLocalPlayer);
            if (link.LinkedInstance != null &&
                link.LinkedInstance.TryGetComponent<DollSingerNetworkPresentation>(out var presentation))
            {
                _dollSingerInput = presentation.OwnedInput;
                _dollSingerPresentation = presentation;
            }
        }

        foreach (var (input, movementInput) in SystemAPI.Query<RefRW<ClientInput>, RefRW<ClientMovementInput>>())
        {
            input.ValueRW = new ClientInput();
            movementInput.ValueRW = new ClientMovementInput();

            bool blocked = GameSettings.Instance.IsPauseMenuOpen || RaidHUD.InventoryOpen;
            if (blocked && _dollSingerInput == null) continue;

            if (_dollSingerInput != null)
            {
                var playerInput = new PlayerInput();
                bool canRead = !blocked && _dollSingerInput.CanReadPlayerInput;
                playerInput.FreeLooking = _dollSingerPresentation.UpdateOwnedLook(
                    ref _accumulatedLook, out playerInput.FreeLookOffset, !canRead);
                playerInput.Lean = canRead && !_dollSingerPresentation.IsThirdPerson
                    ? _dollSingerPresentation.OwnedLeanAmount : 0f;
                playerInput.MoveInput = canRead ? _dollSingerInput.Move : Vector2.zero;
                playerInput.SetFlag(PlayerInput.InputFlag.Jump, canRead && _dollSingerInput.JumpPressed);
                playerInput.SetFlag(PlayerInput.InputFlag.Sprint, canRead && _dollSingerInput.SprintHeld);
                playerInput.SetFlag(PlayerInput.InputFlag.Aim, canRead && _dollSingerInput.AimHeld && !playerInput.FreeLooking);
                playerInput.SetFlag(PlayerInput.InputFlag.ThirdPerson, _dollSingerPresentation.IsThirdPerson);
                var equipped = EntityManager.GetComponentData<PredictedPlayerGhost>(currentLocalPlayer);
                var weapon = WeaponManager.Instance.WeaponRegistry.GetWeaponData(equipped.EquippedWeaponID);
                playerInput.SetFlag(PlayerInput.InputFlag.Shoot, playerInput.Aim &&
                    (weapon != null && !weapon.Automatic ? _dollSingerInput.FirePressed : _dollSingerInput.FireHeld));
                playerInput.SetFlag(PlayerInput.InputFlag.EquipHalo, canRead && _dollSingerInput.WeaponSlotPressed == 1);
                playerInput.SetFlag(PlayerInput.InputFlag.EquipRevolver, canRead && _dollSingerInput.WeaponSlotPressed == 2);
                playerInput.SetFlag(PlayerInput.InputFlag.Reload, canRead && _dollSingerInput.ReloadPressed);
                playerInput.LookYawPitchDegrees = _accumulatedLook;
                playerInput.AimPoint = _dollSingerPresentation.CaptureAimPoint(_accumulatedLook, weapon != null ? weapon.HitscanRange : 100f);
                input.ValueRW.SetInput(0, playerInput);
                movementInput.ValueRW.SetInput(0, playerInput);
                continue;
            }

            var user = InputSystemManager.GetFirstInputUser();

            if (user.valid)
            {
                var controls = (InputSystem_Actions)user.actions;

                var playerInput = new PlayerInput();

                ProcessGameplayInput(controls, ref playerInput);

                // movement
                float2 moveVector = controls.Player.Move.ReadValue<Vector2>();
                playerInput.MoveInput = moveVector;

                var addedDelta = (float2)controls.Player.LookDelta.ReadValue<Vector2>();

                const float sensitivity = 3.7f;
                var lookDelta = addedDelta * sensitivity;

                // Accumulate the delta to our persistent rotation value
                _accumulatedLook.x += lookDelta.x;
                _accumulatedLook.y -= lookDelta.y; // Pitch is typically inverted

                // Clamp the vertical angle to prevent looking straight up/down and flipping
                _accumulatedLook.y = math.clamp(_accumulatedLook.y, -85f, 85f);

                // Assign the full, accumulated angle to the input struct
                playerInput.LookYawPitchDegrees = _accumulatedLook;

                input.ValueRW.SetInput(0, playerInput);
                movementInput.ValueRW.SetInput(0, playerInput);
            }
            else
            {
                Debug.LogWarning($"[ClientInputReaderSystem] Input user is invalid");
            }
        }
    }

    private void ProcessGameplayInput(in InputSystem_Actions controls, ref PlayerInput playerInput)
    {
        playerInput.SetFlag(PlayerInput.InputFlag.Jump, controls.Player.Jump.triggered);
        playerInput.SetFlag(PlayerInput.InputFlag.Shoot, controls.FPS.ShootSingle.IsPressed());
        playerInput.SetFlag(PlayerInput.InputFlag.Reload, controls.FPS.Reload.triggered);
    }
}
