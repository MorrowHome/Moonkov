// 将升降梯的呼叫、夹人保护、运行进度和画面动作连起来。TickAuthoritative 推动门和轿厢状态，联网时客户端用收到的状态做平滑显示。
// CarryHeight 给移动系统查询脚下楼板升降量，让站在梯里的人跟着轿厢走。
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Unity.MP_FPS {
    public enum RaidLiftPhase : byte {
        Docked,
        Closing,
        Travelling,
        Opening
    }

    public struct RaidLiftState {
        public RaidLiftPhase Phase;
        public byte Dock, Destination;
        public float Travel, Gate;
        public uint Revision;
    }

    [DisallowMultipleComponent]
    public sealed class RaidLift : MonoBehaviour {
        private static readonly List<RaidLift> s_Active = new();
        [SerializeField] private string m_LiftId;
        [SerializeField] private Transform m_Cabin;
        [SerializeField] private Vector3 m_LowerPosition;
        [SerializeField] private float m_Rise, m_TravelSeconds, m_GateSeconds;
        [SerializeField] private BoxCollider m_Floor, m_LowerAperture, m_UpperAperture;
        [SerializeField] private AirlockPanelPose[] m_CabinLeaves, m_LowerLeaves, m_UpperLeaves;
        [SerializeField] private AirlockPanelPose[] m_OppositeCabinLeaves;
        [SerializeField] private Transform[] m_Cables;
        [SerializeField] private Transform[] m_Screws;
        [SerializeField] private Transform m_DriveShaft;
        private readonly Collider[] m_Obstructions = new Collider[32];
        private float m_PreviousFloorY;
        private float m_FloorY;
        private bool m_Initialized;
        private bool m_Remote;
        private float m_PoseTravel, m_PoseGate;
        private readonly MoonkovLiftAudio m_Audio = new();
        public string LiftId => m_LiftId;
        public RaidLiftState State { get; private set; }

        public bool Ready =>
            m_Initialized && m_Cabin && m_Floor && m_Rise > 0 && m_TravelSeconds > 0 && m_GateSeconds > 0;

        public static IReadOnlyList<RaidLift> Active => s_Active;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRegistry() { s_Active.Clear(); }

        private void OnEnable() {
            if (!s_Active.Contains(this)) s_Active.Add(this);
            if (!m_Initialized && m_Cabin && m_Floor) ResetToLower();
        }

        private void OnDisable() {
            s_Active.Remove(this);
            m_Audio.Stop();
        }

        private void LateUpdate() {
            if (!Ready || !GameManager.Instance || GameManager.Instance.IsHeadless || Time.deltaTime <= 0) {
                m_Audio.Stop();
                return;
            }
            m_Audio.Update(m_Cabin, m_PoseTravel, m_PoseGate, Time.unscaledTime);
        }

        public void Configure(
            string id,
            Transform cabin,
            BoxCollider floor,
            float rise,
            float travelSeconds,
            float gateSeconds,
            AirlockPanelPose[] cabinLeaves,
            AirlockPanelPose[] lowerLeaves,
            AirlockPanelPose[] upperLeaves,
            BoxCollider lowerAperture,
            BoxCollider upperAperture,
            Transform[] cables,
            AirlockPanelPose[] oppositeCabinLeaves = null,
            Transform[] screws = null,
            Transform driveShaft = null) {
            m_LiftId = id;
            m_Cabin = cabin;
            m_LowerPosition = cabin.localPosition;
            m_Floor = floor;
            m_Rise = rise;
            m_TravelSeconds = travelSeconds;
            m_GateSeconds = gateSeconds;
            m_CabinLeaves = cabinLeaves;
            m_LowerLeaves = lowerLeaves;
            m_UpperLeaves = upperLeaves;
            m_OppositeCabinLeaves = oppositeCabinLeaves ?? Array.Empty<AirlockPanelPose>();
            m_LowerAperture = lowerAperture;
            m_UpperAperture = upperAperture;
            m_Cables = cables;
            m_Screws = screws ?? Array.Empty<Transform>();
            m_DriveShaft = driveShaft;
            ResetToLower();
        }

        public void ResetToLower() {
            State = new RaidLiftState { Gate = 1 };
            m_Initialized = true;
            m_Remote = false;
            ApplyPose(0, 1, 0);
            m_PreviousFloorY = m_FloorY;
        }

        private void Update() {
            if (!AirlockNetworkContext.HasNetworkWorlds) {
                if (m_Remote) ResetToLower();
                TickAuthoritative(Time.deltaTime);
            } else if (m_Remote && !AirlockNetworkContext.HasServer) { TickRemote(Time.deltaTime); }
        }

        public AirlockFeedback Request(int stop) {
            if (!Ready || stop < -1 || stop > 1) return AirlockFeedback.Unavailable;
            var state = State;
            if (state.Phase != RaidLiftPhase.Docked) return AirlockFeedback.Equalizing;
            // 层站的按钮指定固定楼层。轿厢内的按钮用 -1 表示去当前楼层的对面。
            byte destination = (byte)(stop < 0 ? 1 - state.Dock : stop);
            if (state.Dock == destination) return AirlockFeedback.Accepted;
            if (Obstructed(state.Dock)) return AirlockFeedback.Obstructed;
            state.Phase = RaidLiftPhase.Closing;
            state.Destination = destination;
            state.Revision++;
            State = state;
            return AirlockFeedback.Accepted;
        }

        public void TickAuthoritative(float dt) {
            if (!Ready || !AirlockSettings.Finite(dt) || dt <= 0) return;
            var state = State;
            switch (state.Phase) {
                // 门碰到人就中止本次出发，重新开门。等门完全关上后才进入移动阶段。
                case RaidLiftPhase.Closing:
                    if (Obstructed(state.Dock)) {
                        state.Destination = state.Dock;
                        state.Phase = RaidLiftPhase.Opening;
                        break;
                    }
                    state.Gate = Mathf.MoveTowards(state.Gate, 0, dt / m_GateSeconds);
                    if (state.Gate == 0) state.Phase = RaidLiftPhase.Travelling;
                    break;
                // 门完全闭合后才允许升降。抵达后先记新楼层，再进入开门状态。
                case RaidLiftPhase.Travelling:
                    state.Travel = Mathf.MoveTowards(state.Travel, state.Destination, dt / m_TravelSeconds);
                    if (state.Travel == state.Destination) {
                        state.Dock = state.Destination;
                        state.Phase = RaidLiftPhase.Opening;
                    }
                    break;
                // 开门完成才回到可接收下一次呼叫的停靠状态。动作没完不接受新楼层。
                case RaidLiftPhase.Opening:
                    state.Gate = Mathf.MoveTowards(state.Gate, 1, dt / m_GateSeconds);
                    if (state.Gate == 1) state.Phase = RaidLiftPhase.Docked;
                    break;
                default:
                    m_PreviousFloorY = m_FloorY;
                    return;
            }
            state.Revision++;
            State = state;
            ApplyPose(state.Travel, state.Gate, state.Dock);
        }

        // 版本号挡住旧包。换战局时允许从新快照开始，并把轿厢直接放到快照位置。
        public bool ApplyNetworkState(RaidLiftState state, bool fresh) {
            if (!Ready || (!fresh && state.Revision <= State.Revision) || state.Dock > 1 || state.Destination > 1 ||
                !AirlockSettings.Finite(state.Travel) || !AirlockSettings.Finite(state.Gate) || state.Travel < 0 ||
                state.Travel > 1 || state.Gate < 0 || state.Gate > 1 ||
                (byte)state.Phase > (byte)RaidLiftPhase.Opening ||
                (state.Phase == RaidLiftPhase.Travelling ? state.Gate != 0 : state.Travel != state.Dock))
                return false;
            State = state;
            m_Remote = true;
            if (fresh) {
                ApplyPose(state.Travel, state.Gate, state.Dock);
                m_PreviousFloorY = m_FloorY;
            }
            return true;
        }

        private void TickRemote(float dt) {
            float travel = m_PoseTravel;
            float gate = m_PoseGate;
            byte dock = travel == 1 ? (byte)1 : (byte)0;
            // 客户端跟随收到的进度，仍先把自己画面的门关好再挪轿厢。延迟下也能保持门和轿厢的先后关系。
            if (travel != State.Travel) {
                gate = Mathf.MoveTowards(gate, 0, dt / m_GateSeconds * 1.35f);
                if (gate == 0) travel = Mathf.MoveTowards(travel, State.Travel, dt / m_TravelSeconds * 1.35f);
            } else if (travel == State.Dock) { gate = Mathf.MoveTowards(gate, State.Gate, dt / m_GateSeconds * 1.35f); }
            if (travel == m_PoseTravel && gate == m_PoseGate)
                m_PreviousFloorY = m_FloorY;
            else
                ApplyPose(travel, gate, dock);
        }

        private void ApplyPose(float travel, float gate, byte dock) {
            m_PoseTravel = travel;
            m_PoseGate = gate;
            // 网络进度是标准化的 0 到 1。显示层把它换算为平滑的真实高度，门、缆绳和传动轴跟着更新。
            float rise = Mathf.SmoothStep(0, m_Rise, travel);
            m_Cabin.localPosition = m_LowerPosition + Vector3.up * rise;
            foreach (var leaf in m_CabinLeaves) leaf.Apply(dock == 0 || m_OppositeCabinLeaves.Length == 0 ? gate : 0);
            foreach (var leaf in m_OppositeCabinLeaves) leaf.Apply(dock == 1 ? gate : 0);
            foreach (var leaf in m_LowerLeaves) leaf.Apply(dock == 0 ? gate : 0);
            foreach (var leaf in m_UpperLeaves) leaf.Apply(dock == 1 ? gate : 0);
            foreach (var cable in m_Cables)
                cable.localScale = new Vector3(1, Mathf.Max(.002f, 1 - rise / (m_Rise + .12f)), 1);
            float driveRotation = 29250f * (rise / m_Rise);
            foreach (var screw in m_Screws) screw.localRotation = Quaternion.AngleAxis(driveRotation, Vector3.up);
            if (m_DriveShaft) m_DriveShaft.localRotation = Quaternion.AngleAxis(-driveRotation, Vector3.forward);
            m_PreviousFloorY = m_FloorY;
            // 保存上一拍与当前轿厢地板的世界高度，角色无需挂成轿厢子物体也能通过 CarryHeight 随行。
            m_FloorY = m_Floor.transform.TransformPoint(m_Floor.center + Vector3.up * (m_Floor.size.y * .5f)).y;
            if (Application.isPlaying) UnityEngine.Physics.SyncTransforms();
        }

        // 防夹只看当前楼层门口感应区，忽略电梯自己的部件。感应区缺失或结果太多时保守不关。
        private bool Obstructed(byte dock) {
            var sensor = dock == 0 ? m_LowerAperture : m_UpperAperture;
            if (!sensor || !sensor.enabled) return true;
            int count = UnityEngine.Physics.OverlapBoxNonAlloc(sensor.transform.TransformPoint(sensor.center),
                sensor.size * .5f, m_Obstructions, sensor.transform.rotation, UnityEngine.Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);
            if (count == m_Obstructions.Length) return true;
            for (int i = 0; i < count; i++) {
                var hit = m_Obstructions[i];
                if (hit && !hit.transform.IsChildOf(transform) && (hit.attachedRigidbody ||
                                                                   hit.GetComponentInParent<
                                                                       UnityEngine.CharacterController>()))
                    return true;
            }
            return false;
        }

        public static float CarryHeight(Vector3 feet) {
            foreach (var lift in s_Active) {
                if (!lift || !lift.Ready) continue;
                float movement = lift.m_FloorY - lift.m_PreviousFloorY;
                // 必须靠近电梯原来的地板高度，并且脚踩在轿厢范围内，才会跟它移动。路过门口的人不受影响。
                if (movement == 0 || Mathf.Abs(feet.y - lift.m_PreviousFloorY) > .18f) continue;
                var floor = lift.m_Floor;
                var local = floor.transform.InverseTransformPoint(feet) - floor.center;
                if (Mathf.Abs(local.x) > floor.size.x * .5f - .06f || Mathf.Abs(local.z) > floor.size.z * .5f - .06f)
                    continue;
                float adjustment = lift.m_FloorY + .025f - feet.y;
                if (adjustment * movement > 0) return adjustment;
            }
            return 0;
        }

        public static RaidLift Find(string id) {
            RaidLift match = null;
            foreach (var lift in s_Active)
                if (lift && lift.LiftId == id) {
                    if (match) return null;
                    match = lift;
                }
            return match;
        }
    }
}