// 把战局里的月面时间同步到天空和光照。房主直接读服务端时间基准，远程客户端读收到的时间基准。
// ExpeditionClock 用服务器 tick 推算当前时刻，MoonSceneLighting 用这个时刻更新画面。
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;
using Unity.MP_FPS.Moon;

namespace Unity.MP_FPS {
    [RequireComponent(typeof(MoonSceneLighting))]
    public sealed class ExpeditionClockPresentation : MonoBehaviour {
        public static ExpeditionClockPresentation Active { get; private set; }
        public double TotalHours { get; private set; }
        public bool IsSynchronized { get; private set; }
        private MoonSceneLighting m_Lighting;
        private World m_World;
        private EntityQuery m_Clock, m_Time;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetPresentation() { Active = null; }

        private void OnEnable() {
            Active = this;
            m_Lighting = GetComponent<MoonSceneLighting>();
        }

        // 房主同一进程有服务器和客户端两套世界，先从服务器取时钟。远程玩家才找客户端转发过来的基准。
        private void FindWorld() {
            // Host renders its authoritative clock. Remote players render the server's replicated anchor.
            foreach (var world in World.All) {
                if (!world.IsCreated || !world.IsServer()) continue;
                if (Bind(world)) return;
            }
            foreach (var world in World.All)
                if (world.IsCreated && world.IsClient() && !world.IsThinClient() && Bind(world))
                    return;
        }

        // 仅在某个 World 同时拥有战局时钟和网络时间时挂接。缺一项就释放临时查询并继续寻找。
        private bool Bind(World world) {
            var clock = world.EntityManager.CreateEntityQuery(typeof(ExpeditionClockState));
            var time = world.EntityManager.CreateEntityQuery(typeof(NetworkTime));
            if (clock.IsEmptyIgnoreFilter || time.IsEmptyIgnoreFilter) {
                clock.Dispose();
                time.Dispose();
                return false;
            }
            m_World = world;
            m_Clock = clock;
            m_Time = time;
            return true;
        }

        private void LateUpdate() {
            if (m_World == null || !m_World.IsCreated) FindWorld();
            IsSynchronized = m_World != null && m_World.IsCreated && !m_Clock.IsEmptyIgnoreFilter &&
                             !m_Time.IsEmptyIgnoreFilter;
            if (!IsSynchronized) return;
            var state = m_Clock.GetSingleton<ExpeditionClockState>();
            var time = m_Time.GetSingleton<NetworkTime>();
            // 时钟就绪且收到有效 tick 后再改变光照。开局网络数据未到时保留场景原来的光。
            IsSynchronized = state.Ready && time.ServerTick.IsValid;
            if (!IsSynchronized) return;
            // 网络包给的是可追溯的时间基准。客户端逐帧按服务器当前 tick 推算，光照就不用等每次收到包才跳一下。
            TotalHours = ExpeditionClock.HoursAt(state, time.ServerTick, time.ServerTickFraction);
            m_Lighting.ApplyTimeOfDay(TotalHours);
        }

        // 离开地图时解除对 World 的查询引用。世界可能已经销毁，因此只清理仍有效的查询。
        private void OnDisable() {
            if (m_World != null && m_World.IsCreated) {
                m_Clock.Dispose();
                m_Time.Dispose();
            }
            m_World = null;
            IsSynchronized = false;
            if (Active == this) Active = null;
        }
    }
}