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
        var currentLocalPlayer = Entity.Null;
        var playerForward = new float3(0, 0, 1);

        // 找到本地玩家实体，并读取服务器指定的出生朝向。
        // 用 GhostOwnerIsLocal 找这台机器控制的角色。开局或重生时，需要用它同步过来的朝向初始化鼠标累计角度。
        foreach (var (transform, ghost, owner, entity) in SystemAPI
                     .Query<RefRO<LocalTransform>, RefRO<PredictedPlayerGhost>, RefRO<GhostOwnerIsLocal>>()
                     .WithEntityAccess()) {
            currentLocalPlayer = entity;
            playerForward = math.mul(transform.ValueRO.Rotation, new float3(0, 0, 1));
            break; // 找到本地玩家即可结束查找。
        }

        // 第二步，通过实体 ID 变化判断角色是否重生。
        if (currentLocalPlayer != Entity.Null) {
            // 重生换了角色实体，就从服务器给的新出生朝向开始积累视角。顺便清掉上个角色的输入与光环转移引用。
            if (currentLocalPlayer != _lastKnownPlayerEntity) {
                // 每张地图指定自己的出生朝向。旧竞技场中心只适用于原地图。
                float yawRadians = math.atan2(playerForward.x, playerForward.z);
                _accumulatedLook = new float2(math.degrees(yawRadians), 0f);

                // 记录当前角色已初始化。它还活着时就不用再重设朝向。
                _lastKnownPlayerEntity = currentLocalPlayer;
                _dollSingerInput = null;
                _dollSingerPresentation = null;
                _transferGestureHeld = false;
                _transferTarget = HaloTransferCandidate = Entity.Null;
            }
        } else {
            // 玩家已死亡或还没有出生。
            // 清空追踪状态，让下一次出生时重新初始化。
            _lastKnownPlayerEntity = Entity.Null;
            _dollSingerInput = null;
            _dollSingerPresentation = null;
            _transferGestureHeld = false;
            _transferTarget = HaloTransferCandidate = Entity.Null;
        }
        // 实体上有网络字段和所有者，设备操作则由场景里的表现脚本管理。经 GhostGameObjectLink 才能从实体找到持有键鼠输入的那个对象。
        if (_dollSingerInput == null && currentLocalPlayer != Entity.Null &&
            EntityManager.HasComponent<GhostGameObjectLink>(currentLocalPlayer)) {
            var link = EntityManager.GetComponentObject<GhostGameObjectLink>(currentLocalPlayer);
            if (link.LinkedInstance != null &&
                link.LinkedInstance.TryGetComponent<DollSingerNetworkPresentation>(out var presentation)) {
                _dollSingerInput = presentation.OwnedInput;
                _dollSingerPresentation = presentation;
            }
        }
        foreach (var (input, movementInput) in SystemAPI.Query<RefRW<ClientInput>, RefRW<ClientMovementInput>>()) {
            input.ValueRW = new ClientInput();
            movementInput.ValueRW = new ClientMovementInput();
            // 打开背包或暂停时得让服务器知道这一帧的开火被屏蔽。可在界面内保留哪些能力由 HUD 和角色表现一起决定。
            bool blocked = GameSettings.Instance.IsPauseMenuOpen || RaidHUD.GameplayActionsBlocked;
            // 角色表现尚未连上时，也要明确交出停火的输入。否则可能走到简单模板的键位读取，在菜单开着时使用上一段操作。
            if (blocked && _dollSingerInput == null) {
                var suppressed = new PlayerInput();
                suppressed.SetFlag(PlayerInput.InputFlag.FireSuppressed, true);
                input.ValueRW.SetInput(0, suppressed);
                movementInput.ValueRW.SetInput(0, suppressed);
                continue;
            }
            if (_dollSingerInput != null) {
                var playerInput = new PlayerInput { HaloLightingSettings = _dollSingerInput.LightingSettings };
                // 先判断当前镜头与动作能不能接受按键，再分别决定移动、瞄准和开火。调光等特殊界面可以用自己的预览状态。
                bool canRead = !blocked && _dollSingerInput.CanReadPlayerInput;
                playerInput.SetFlag(PlayerInput.InputFlag.HaloLightDisabled,
                    !_dollSingerPresentation.ReadOwnedLight(canRead));
                // 身体转向与自由观察拆开存。只转头查看四周时，身体前进方向和射击方向还有独立的原值可用。
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
                // 武器手被别的动作占用、暗视界面、选中工具或只转头时，都把这一帧开火关掉。服务端仍会检查手里物品和角色是否允许射击。
                bool canFire = canRead && _dollSingerPresentation.WeaponHandsAvailable && !RaidHUD.DarkSightRequested &&
                               !RaidHUD.QuickSlotToolSelected && !playerInput.FreeLooking;
                playerInput.SetFlag(PlayerInput.InputFlag.FireSuppressed, !canFire);
                playerInput.SetFlag(PlayerInput.InputFlag.FireHeld, canFire && _dollSingerInput.FireHeld);
                // 单发看这一帧是否按下，连发和点射看是否按住。最后由武器规则按冷却、弹量和其他动作状态判断能否开火。
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
                // 本机相机提供准星指向的世界坐标。服务端随后从角色自己的发射点算弹道并判定伤害。
                playerInput.AimPoint =
                    _dollSingerPresentation.CaptureAimPoint(_accumulatedLook,
                        weapon != null ? weapon.HitscanRange : 100f);
                ReadHaloTransfer(currentLocalPlayer, equipped, canFire, ref playerInput);
                // 教学和光环充能提示先看到本机这一帧的操作。写入两个 ECS 组件后，发送系统会继续把动作放进网络命令。
                TutorialRuntime.Observe(playerInput);
                InGameHUD.ObserveHaloChargeInput(playerInput, equipped);
                input.ValueRW.SetInput(0, playerInput);
                movementInput.ValueRW.SetInput(0, playerInput);
                continue;
            }
            // 还拿不到正式角色的输入脚本时，走模板 Input System 映射。它只采一些通用键位，正式角色的高级操作在上面的分支。
            var user = InputSystemManager.GetFirstInputUser();
            if (user.valid) {
                var controls = (InputSystem_Actions)user.actions;
                var playerInput = new PlayerInput();
                ProcessGameplayInput(controls, ref playerInput);

                // 移动输入
                float2 moveVector = controls.Player.Move.ReadValue<Vector2>();
                playerInput.MoveInput = moveVector;
                var addedDelta = (float2)controls.Player.LookDelta.ReadValue<Vector2>();
                const float sensitivity = 3.7f;
                var lookDelta = addedDelta * sensitivity;

                // 把本帧的视角增量累加到持续保存的转角上。
                _accumulatedLook.x += lookDelta.x;
                _accumulatedLook.y -= lookDelta.y; // 纵向输入通常要反转符号。

                // 限制上下视角，避免望向正上方或下方时翻转。
                _accumulatedLook.y = math.clamp(_accumulatedLook.y, -85f, 85f);

                // 将累计得到的完整角度放入输入结构体。
                playerInput.LookYawPitchDegrees = _accumulatedLook;
                TutorialRuntime.Observe(playerInput);
                input.ValueRW.SetInput(0, playerInput);
                movementInput.ValueRW.SetInput(0, playerInput);
            } else { Debug.LogWarning($"[ClientInputReaderSystem] Input user is invalid"); }
        }
    }

    private void ProcessGameplayInput(in InputSystem_Actions controls, ref PlayerInput playerInput) {
        playerInput.SetFlag(PlayerInput.InputFlag.Jump, controls.Player.Jump.triggered);
        playerInput.SetFlag(PlayerInput.InputFlag.Shoot, controls.FPS.ShootSingle.IsPressed());
        playerInput.SetFlag(PlayerInput.InputFlag.FireHeld, controls.FPS.ShootSingle.IsPressed());
        playerInput.SetFlag(PlayerInput.InputFlag.Reload, controls.FPS.Reload.triggered);
    }
}