using Unity.Collections;
using Unity.Entities;
using Unity.NetCode;

namespace Unity.MP_FPS
{
    public struct ExpeditionClockState : IComponentData
    {
        public double AnchorHours, HoursPerSecond;
        public uint AnchorTick, Sequence;
        public ulong SessionId;
        public int TickRate;
        public bool Ready;
    }

    public struct ExpeditionClockRpc : IRpcCommand
    {
        public double AnchorHours, HoursPerSecond;
        public uint AnchorTick, Sequence;
        public ulong SessionId;
        public int TickRate;
    }

    public struct ExpeditionClockSynchronized : IComponentData { }

    public static class ExpeditionClock
    {
        // A fractional tick denotes progress toward that tick, not time after its end.
        public static double HoursAt(in ExpeditionClockState state, NetworkTick tick, float fraction = 1)
        {
            if (!state.Ready || !tick.IsValid || state.TickRate <= 0) return state.AnchorHours;
            double seconds = (tick.TicksSince(new NetworkTick(state.AnchorTick)) + fraction - 1) / state.TickRate;
            return state.AnchorHours + seconds * state.HoursPerSecond;
        }

        public static double HourOfDay(double totalHours) => (totalHours % 24 + 24) % 24;
        public static string Display(double totalHours)
        {
            int minutes = (int)System.Math.Floor(HourOfDay(totalHours) * 60);
            int day = (int)System.Math.Floor(totalHours / 24) + 1;
            return $"DAY {day:00} / {minutes / 60:00}:{minutes % 60:00}";
        }
    }

    [WorldSystemFilter(WorldSystemFilterFlags.ServerSimulation)]
    [UpdateAfter(typeof(ServerGameSystem))]
    public partial class ServerExpeditionClockSystem : SystemBase
    {
        private Entity m_State;
        private MoonRaidMap m_Map;
        private double m_NextSync;
        private ulong m_SessionId;

        protected override void OnCreate()
        {
            m_State = EntityManager.CreateEntity(typeof(ExpeditionClockState));
            m_SessionId = System.BitConverter.ToUInt64(System.Guid.NewGuid().ToByteArray(), 0);
            RequireForUpdate<NetworkTime>();
        }

        protected override void OnUpdate()
        {
            var map = MoonRaidMap.Active;
            var clock = EntityManager.GetComponentData<ExpeditionClockState>(m_State);
            if (map == null)
            {
                clock.Ready = false;
                EntityManager.SetComponentData(m_State, clock);
                m_Map = null;
                return;
            }
            var tick = SystemAPI.GetSingleton<NetworkTime>().ServerTick;
            if (!tick.IsValid) return;
            int tickRate = 60;
            if (SystemAPI.TryGetSingleton<ClientServerTickRate>(out var rates))
            {
                rates.ResolveDefaults();
                tickRate = rates.SimulationTickRate;
            }
            double rate = map.TimeRunning ? 24d / (map.DayLengthMinutes * 60d) : 0;
            double now = SystemAPI.Time.ElapsedTime;
            bool fresh = !object.ReferenceEquals(m_Map, map) || !clock.Ready;
            bool changed = fresh || clock.HoursPerSecond != rate || clock.TickRate != tickRate;
            bool broadcast = changed || now >= m_NextSync;
            if (broadcast)
            {
                clock.AnchorHours = fresh ? map.StartHour : ExpeditionClock.HoursAt(clock, tick);
                clock.AnchorTick = tick.TickIndexForValidTick;
                clock.TickRate = tickRate;
                clock.HoursPerSecond = rate;
                clock.Ready = true;
                clock.Sequence++;
                clock.SessionId = m_SessionId;
                m_Map = map;
                m_NextSync = now + 2;
                EntityManager.SetComponentData(m_State, clock);
            }

            using var ecb = new EntityCommandBuffer(Allocator.Temp);
            foreach (var (id, connection) in SystemAPI.Query<RefRO<NetworkId>>()
                         .WithAll<NetworkStreamInGame>().WithEntityAccess())
            {
                bool joined = !EntityManager.HasComponent<ExpeditionClockSynchronized>(connection);
                if (!broadcast && !joined) continue;
                var rpc = ecb.CreateEntity();
                ecb.AddComponent(rpc, new ExpeditionClockRpc { AnchorHours = clock.AnchorHours,
                    HoursPerSecond = clock.HoursPerSecond, AnchorTick = clock.AnchorTick,
                    TickRate = clock.TickRate, Sequence = clock.Sequence, SessionId = clock.SessionId });
                ecb.AddComponent(rpc, new SendRpcCommandRequest { TargetConnection = connection });
                if (joined) ecb.AddComponent<ExpeditionClockSynchronized>(connection);
            }
            ecb.Playback(EntityManager);
        }
    }

    [WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation | WorldSystemFilterFlags.ThinClientSimulation)]
    public partial class ClientExpeditionClockSystem : SystemBase
    {
        private Entity m_State;
        private Entity m_Connection;
        protected override void OnCreate() => m_State = EntityManager.CreateEntity(typeof(ExpeditionClockState));
        protected override void OnUpdate()
        {
            using var ecb = new EntityCommandBuffer(Allocator.Temp);
            var clock = EntityManager.GetComponentData<ExpeditionClockState>(m_State);
            SystemAPI.TryGetSingletonEntity<NetworkId>(out var connection);
            if (connection != m_Connection)
            {
                clock = default;
                m_Connection = connection;
            }
            foreach (var (rpc, received, entity) in SystemAPI.Query<RefRO<ExpeditionClockRpc>, RefRW<ReceiveRpcCommandRequest>>()
                         .WithEntityAccess())
            {
                received.ValueRW.Consume();
                var update = rpc.ValueRO;
                if (connection != Entity.Null && received.ValueRO.SourceConnection == connection &&
                    (!clock.Ready || update.SessionId != clock.SessionId || update.Sequence > clock.Sequence) && update.TickRate > 0 &&
                    !double.IsNaN(update.AnchorHours) && !double.IsInfinity(update.AnchorHours) &&
                    !double.IsNaN(update.HoursPerSecond) && !double.IsInfinity(update.HoursPerSecond) && update.HoursPerSecond >= 0)
                    clock = new ExpeditionClockState { AnchorHours = update.AnchorHours, AnchorTick = update.AnchorTick,
                        HoursPerSecond = update.HoursPerSecond, TickRate = update.TickRate, Sequence = update.Sequence,
                        SessionId = update.SessionId, Ready = true };
                ecb.DestroyEntity(entity);
            }
            EntityManager.SetComponentData(m_State, clock);
            ecb.Playback(EntityManager);
        }
    }
}
