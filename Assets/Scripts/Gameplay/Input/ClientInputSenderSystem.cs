// 这里把每帧采到的操作写进按 tick 编号的指令缓冲。NetCode 从缓冲发送指令，客户端预测也从同一缓冲取操作。
using Unity.MP_FPS;
using Unity.Entities;
using Unity.NetCode;

[UpdateInGroup(typeof(GhostInputSystemGroup))]
[UpdateAfter(typeof(ClientInputReaderSystem))]
public partial class ClientInputSenderSystem : SystemBase {
    private NetworkTick m_PreviouslySentTick;
    private ClientMovementInput m_InProgressCommandInput;
    protected override void OnCreate() { RequireForUpdate<NetworkTime>(); }

    protected override void OnUpdate() {
        var networkTime = SystemAPI.GetSingleton<NetworkTime>();
        // Above the prediction window, NetCode delays local playback but still
        // needs commands for the future input tick so they reach the server on time.
        // 输入要送到服务器将来会处理的那一拍。网络延迟高时，本机正在预测的 ServerTick 可能落在它后面，此处按 InputTargetTick 排队。
        var tick = networkTime.InputTargetTick;
        if (tick.IsValid) {
            var previousTick = m_PreviouslySentTick;
            bool blocked = GameSettings.Instance.IsPauseMenuOpen || RaidHUD.GameplayActionsBlocked;
            var inProgressCommandInput = blocked ? default : m_InProgressCommandInput;
            var bufferLookup = SystemAPI.GetBufferLookup<ClientCommandInput>();
            foreach (var (input, entity) in SystemAPI.Query<RefRO<ClientMovementInput>>().WithAll<ClientCommandInput>()
                         .WithEntityAccess()) {
                var buffer = bufferLookup[entity];
                if (!previousTick.IsValid || tick.IsNewerThan(previousTick)) {
                    // 开启一条新指令，用这帧的移动等连续状态，再合并上一段积下来的跳跃、单发开火等瞬时操作。
                    // this is a new tick.
                    var commandInput = new ClientCommandInput
                        { Tick = tick, ClientInterpolationTick = networkTime.InterpolationTick };
                    commandInput.SetFrom(in input.ValueRO);
                    commandInput.UpdateFrom(inProgressCommandInput, false);
                    buffer.AddCommandData(commandInput);

                    // we can now start a new inprogress input
                    inProgressCommandInput = default;
                    previousTick = tick;
                } else {
                    // 一拍内可能有多帧。多出来的操作先累积给下一拍，同时更新本拍的本机缓冲，让重跑预测也能用到这一拍最新的输入。
                    // we've already sent this tick
                    // but we've still got client frames happening
                    // so let's record our input for sending when the tick next changes
                    inProgressCommandInput.UpdateFrom(in input.ValueRO);

                    // even though it's been sent, we add it again, so that the prediction
                    // system has the very latest data for this tick
                    if (buffer.GetDataAtTick(tick, out var existingCommandData) && existingCommandData.Tick == tick) {
                        if (blocked)
                            existingCommandData.SetFrom(in input.ValueRO);
                        else
                            existingCommandData.UpdateFrom(in input.ValueRO);
                        buffer.AddCommandData(existingCommandData);
                    } else {
                        // Recover a pruned command so prediction always has this tick.
                        var command = new ClientCommandInput
                            { Tick = tick, ClientInterpolationTick = networkTime.InterpolationTick };
                        command.SetFrom(input.ValueRO);
                        buffer.AddCommandData(command);
                    }
                }
            }
            m_PreviouslySentTick = previousTick;
            m_InProgressCommandInput = inProgressCommandInput;
        }
    }
}