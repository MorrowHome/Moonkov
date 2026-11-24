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
        var tick = networkTime.InputTargetTick;
        if (!tick.IsValid) return;

        bool blocked = GameSettings.Instance.IsPauseMenuOpen || RaidHUD.GameplayActionsBlocked;
        if (blocked) m_InProgressCommandInput = default;
        var buffers = SystemAPI.GetBufferLookup<ClientCommandInput>();
        foreach (var (movement, entity) in SystemAPI.Query<RefRO<ClientMovementInput>>()
                     .WithAll<ClientCommandInput>().WithEntityAccess()) {
            var buffer = buffers[entity];
            var command = new ClientCommandInput { Tick = tick, ClientInterpolationTick = networkTime.InterpolationTick };
            if (!m_PreviouslySentTick.IsValid || tick.IsNewerThan(m_PreviouslySentTick)) {
                // 新的一拍取最新的移动状态，带上两拍之间的短按。
                command.SetFrom(in movement.ValueRO);
                command.UpdateFrom(m_InProgressCommandInput, false);
                m_InProgressCommandInput = default;
                m_PreviouslySentTick = tick;
            } else {
                m_InProgressCommandInput.UpdateFrom(in movement.ValueRO);
                if (buffer.GetDataAtTick(tick, out var previous) && previous.Tick == tick) {
                    command = previous;
                    if (blocked) command.SetFrom(in movement.ValueRO);
                    else command.UpdateFrom(in movement.ValueRO);
                } else {
                    command.SetFrom(in movement.ValueRO);
                }
            }
            buffer.AddCommandData(command);
        }
    }
}
