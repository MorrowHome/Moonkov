using Unity.Entities;
using Unity.NetCode;
using UnityEngine;
using Unity.MP_FPS.Moon;

namespace Unity.MP_FPS
{
    [RequireComponent(typeof(MoonSceneLighting))]
    public sealed class ExpeditionClockPresentation : MonoBehaviour
    {
        public static ExpeditionClockPresentation Active { get; private set; }
        public double TotalHours { get; private set; }
        public bool IsSynchronized { get; private set; }
        private MoonSceneLighting m_Lighting;
        private World m_World;
        private EntityQuery m_Clock, m_Time;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() => Active = null;

        private void OnEnable()
        {
            Active = this;
            m_Lighting = GetComponent<MoonSceneLighting>();
        }

        private void FindWorld()
        {
            // Host renders its authoritative clock. Remote players render the server's replicated anchor.
            foreach (var world in World.All)
            {
                if (!world.IsCreated || !world.IsServer()) continue;
                if (Bind(world)) return;
            }
            foreach (var world in World.All)
                if (world.IsCreated && world.IsClient() && !world.IsThinClient() && Bind(world)) return;
        }

        private bool Bind(World world)
        {
            var clock = world.EntityManager.CreateEntityQuery(typeof(ExpeditionClockState));
            var time = world.EntityManager.CreateEntityQuery(typeof(NetworkTime));
            if (clock.IsEmptyIgnoreFilter || time.IsEmptyIgnoreFilter)
            {
                clock.Dispose();
                time.Dispose();
                return false;
            }
            m_World = world;
            m_Clock = clock;
            m_Time = time;
            return true;
        }

        private void LateUpdate()
        {
            if (m_World == null || !m_World.IsCreated) FindWorld();
            IsSynchronized = m_World != null && m_World.IsCreated && !m_Clock.IsEmptyIgnoreFilter && !m_Time.IsEmptyIgnoreFilter;
            if (!IsSynchronized) return;
            var state = m_Clock.GetSingleton<ExpeditionClockState>();
            var time = m_Time.GetSingleton<NetworkTime>();
            IsSynchronized = state.Ready && time.ServerTick.IsValid;
            if (!IsSynchronized) return;
            TotalHours = ExpeditionClock.HoursAt(state, time.ServerTick, time.ServerTickFraction);
            m_Lighting.ApplyTimeOfDay(TotalHours);
        }

        private void OnDisable()
        {
            if (m_World != null && m_World.IsCreated) { m_Clock.Dispose(); m_Time.Dispose(); }
            m_World = null;
            IsSynchronized = false;
            if (Active == this) Active = null;
        }
    }
}
