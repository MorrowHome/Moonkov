using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Unity.MP_FPS
{
    // Local visual lab only. No inventory, networking, player profile or AI world is started.
    public sealed class SmokeGrenadeDemo : MonoBehaviour
    {
        [SerializeField] private Camera m_Camera;
        [SerializeField] private TextMesh m_Status;
        [SerializeField] private bool m_Loop = true;
        private TacticalItemsFeedback m_Feedback;
        private Vector3 m_Position, m_Velocity;
        private RaycastHit[] m_Hits = new RaycastHit[32];
        private bool m_Resting, m_Active;
        private float m_Age, m_NextSnapshot, m_RestartAt;
        private uint m_Sequence;
        public float CycleAge => m_Age;
        public bool Active => m_Active;

        public void Configure(Camera camera, TextMesh status) { m_Camera = camera; m_Status = status; }
        private void Start()
        {
            var root = new GameObject("Local smoke presentation");
            root.transform.SetParent(transform, false);
            m_Feedback = root.AddComponent<TacticalItemsFeedback>();
            RestartSmoke();
        }
        [ContextMenu("Restart smoke at centre")]
        public void RestartSmoke() => Begin(new Vector3(0, .2f, 0), Vector3.zero);
        [ContextMenu("Throw from camera")]
        public void ThrowFromCamera()
        {
            if (m_Camera) Begin(m_Camera.transform.position + m_Camera.transform.forward * .6f,
                m_Camera.transform.forward * 14 + Vector3.up * 2);
        }
        private void Begin(Vector3 position, Vector3 velocity)
        {
            if (!Application.isPlaying || !m_Feedback) return;
            ClearSmoke();
            m_Position = position; m_Velocity = velocity; m_Resting = false;
            m_Active = true; m_Age = 0; m_NextSnapshot = 0;
            Present();
        }
        [ContextMenu("Clear smoke")]
        public void ClearSmoke()
        {
            if (!m_Feedback) return;
            m_Feedback.Present(new TacticalWorldStateRpc { Epoch = 1, Id = 1, Sequence = ++m_Sequence, Phase = TacticalWorldPhase.Removed });
            m_Active = false; m_RestartAt = Time.time + 3;
        }
        public void SetView(int index)
        {
            if (!m_Camera) return;
            var pose = index == 1 ? new Vector3(0, 1.7f, -11) : index == 2 ? new Vector3(0, 1.7f, 0) :
                index == 3 ? new Vector3(11, 2.5f, 0) : new Vector3(10, 6, -13);
            m_Camera.transform.position = pose;
            m_Camera.transform.LookAt(index == 2 ? new Vector3(0, 1.7f, 7) : new Vector3(0, 1.4f, 0));
        }
        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.rKey.wasPressedThisFrame) RestartSmoke();
                if (keyboard.gKey.wasPressedThisFrame) ThrowFromCamera();
                if (keyboard.cKey.wasPressedThisFrame) { ClearSmoke(); m_Loop = false; }
                if (keyboard.lKey.wasPressedThisFrame) m_Loop = !m_Loop;
                if (keyboard.digit1Key.wasPressedThisFrame) SetView(0);
                if (keyboard.digit2Key.wasPressedThisFrame) SetView(1);
                if (keyboard.digit3Key.wasPressedThisFrame) SetView(2);
                if (keyboard.digit4Key.wasPressedThisFrame) SetView(3);
            }
            MoveCamera(keyboard);
            if (!m_Active) { if (m_Loop && Time.time >= m_RestartAt) RestartSmoke(); }
            else
            {
                float previous = m_Age; m_Age += Time.deltaTime;
                if (previous < 3)
                    EnergyGrenadePhysics.Step(gameObject.scene.GetPhysicsScene(), ref m_Position, ref m_Velocity,
                        ref m_Resting, Mathf.Min(Time.deltaTime, 3 - previous), null, ref m_Hits);
                if (m_Age >= 23) ClearSmoke();
                else if (m_Age >= m_NextSnapshot || previous < 3 && m_Age >= 3)
                { Present(); m_NextSnapshot = m_Age + .1f; }
            }
            if (m_Status) m_Status.text = $"SMOKE LAB / {(m_Loop ? "LOOP" : "MANUAL")}\n" +
                (!m_Active ? "CLEAR" : m_Age < 3 ? $"FUSE  {3 - m_Age:0.0}s" : $"SMOKE  {23 - m_Age:0.0}s");
        }
        private void Present()
        {
            m_Feedback.Present(new TacticalWorldStateRpc { Epoch = 1, Id = 1, Sequence = ++m_Sequence,
                Kind = TacticalItemKind.Smoke, Phase = m_Age < 3 ? TacticalWorldPhase.Flying : TacticalWorldPhase.Smoke,
                Position = m_Position, Velocity = m_Age < 3 ? m_Velocity : Vector3.zero, Radius = 5,
                Remaining = m_Age < 3 ? 3 - m_Age : 23 - m_Age });
        }
        private void MoveCamera(Keyboard keyboard)
        {
            var mouse = Mouse.current;
            if (!m_Camera || mouse == null || !mouse.rightButton.isPressed) return;
            var angles = m_Camera.transform.eulerAngles;
            float pitch = Mathf.DeltaAngle(0, angles.x);
            var delta = mouse.delta.ReadValue() * .12f;
            m_Camera.transform.rotation = Quaternion.Euler(Mathf.Clamp(pitch - delta.y, -85, 85), angles.y + delta.x, 0);
            if (keyboard == null) return;
            Vector3 move = new Vector3((keyboard.dKey.isPressed ? 1 : 0) - (keyboard.aKey.isPressed ? 1 : 0),
                (keyboard.eKey.isPressed ? 1 : 0) - (keyboard.qKey.isPressed ? 1 : 0),
                (keyboard.wKey.isPressed ? 1 : 0) - (keyboard.sKey.isPressed ? 1 : 0));
            m_Camera.transform.position += m_Camera.transform.TransformDirection(Vector3.ClampMagnitude(move, 1)) *
                ((keyboard.leftShiftKey.isPressed ? 10 : 4) * Time.deltaTime);
        }
    }
}
