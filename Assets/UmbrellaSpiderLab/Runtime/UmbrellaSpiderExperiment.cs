using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Unity.MP_FPS.SpiderLab
{
    /// <summary>
    /// Explicit frame owner: intent -> motor -> prediction/feet -> presentation. Offline experiment,
    /// not a network enemy. F7 releases the existing preview player for close observation in MC00.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UmbrellaSpiderExperiment : MonoBehaviour
    {
        [SerializeField, Range(.5f, 20)] private float speed = 10.5f;
        [SerializeField, Range(1, 2.5f)] private float burstMultiplier = 1.6f;
        [SerializeField, Min(0)] private float automaticReturnDistance = 18f;
        [SerializeField] private LayerMask surfaceMask = UnityEngine.Physics.DefaultRaycastLayers;
        [SerializeField] private bool automatic;
        [SerializeField] private bool automaticUnstick = true;
        [SerializeField] private SpiderPresentation.Stage stage = SpiderPresentation.Stage.Umbrella;
        [SerializeField] private SpiderPresentation.Look appearance = SpiderPresentation.Look.Carapace;
        [SerializeField] private bool showContactMarkers;
        [SerializeField] private GameObject previewPlayer;
        [SerializeField] private Camera observerCamera;
        [SerializeField] private float cameraYawOffset;
        [SerializeField, Range(-65, 75)] private float cameraPitch = 18;
        [SerializeField, Range(2.8f, 8)] private float cameraDistance = 5;
        private SurfaceMotor m_Motor;
        private PredictiveFeet m_Feet;
        private SpiderPresentation m_Presentation;
        private SpiderCharacterView m_View;
        private Vector3 m_Spawn;
        private Quaternion m_SpawnRotation;
        private bool m_PlayerWasActive, m_FreeObserver, m_Hold, m_Slow;
        private float m_HudTime;
        private Label m_Status;
        private float m_Reverse = 1;
        private float m_RunClock, m_RouteProgress;
        private float m_StuckTime, m_EscapeCooldown;
        private Vector3 m_StuckOrigin, m_LastWorldIntent;
        public SurfaceMotor Motor => m_Motor;
        public PredictiveFeet Feet => m_Feet;
        public SpiderPresentation Presentation => m_Presentation;
        public SpiderPresentation.Look Appearance => appearance;
        public bool ShowContactMarkers => showContactMarkers;

        private void OnEnable()
        {
            m_Spawn = transform.position; m_SpawnRotation = transform.rotation;
            m_PlayerWasActive = previewPlayer && previewPlayer.activeSelf;
            if (previewPlayer) previewPlayer.SetActive(false);
            m_Motor = new SurfaceMotor(m_Spawn, m_SpawnRotation, surfaceMask);
            m_StuckOrigin = m_Spawn;
            m_Feet = new PredictiveFeet(surfaceMask); m_Feet.Reset(m_Motor.Current);
            m_View = new SpiderCharacterView(observerCamera, m_Motor.Current, surfaceMask, cameraYawOffset, cameraPitch, cameraDistance);
            m_Presentation = new SpiderPresentation(transform, surfaceMask);
            m_Presentation.SetAppearance(appearance, showContactMarkers);
            m_Status = GetComponent<UIDocument>()?.rootVisualElement.Q<Label>("status");
            Draw(.016f);
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (!m_FreeObserver) m_View.ReadInput();
            float dt = Mathf.Min(Time.deltaTime, .1f) * (m_Slow ? .2f : 1);
            m_RunClock += dt;
            if (keyboard != null)
            {
                if (keyboard.f3Key.wasPressedThisFrame && m_Presentation.Fluid != null)
                    m_Presentation.Fluid.AttractionEnabled = !m_Presentation.Fluid.AttractionEnabled;
                if (keyboard.f4Key.wasPressedThisFrame) showContactMarkers = !showContactMarkers;
                if (keyboard.f5Key.wasPressedThisFrame) appearance = (SpiderPresentation.Look)(((int)appearance + 1) % 4);
                if (keyboard.f6Key.wasPressedThisFrame) automatic = !automatic;
                if (keyboard.f7Key.wasPressedThisFrame) SetObserver(!m_FreeObserver);
                if (keyboard.f8Key.wasPressedThisFrame) stage = (SpiderPresentation.Stage)(((int)stage + 1) % 4);
                if (keyboard.f9Key.wasPressedThisFrame) m_Slow = !m_Slow;
                if (keyboard.f10Key.wasPressedThisFrame) ResetExperiment();
                if (keyboard.f11Key.wasPressedThisFrame) m_Reverse = -m_Reverse;
                if (keyboard.f12Key.wasPressedThisFrame) m_Hold = !m_Hold;
            }
            if (automatic && automaticReturnDistance > 0)
            {
                if (m_RouteProgress >= automaticReturnDistance) m_Reverse = -1;
                else if (m_RouteProgress < 0) m_Reverse = 1;
            }
            Vector2 input = automatic ? new Vector2(0, m_Reverse) : Vector2.zero;
            Vector3? manualDirection = null;
            float yaw = 0;
            if (!automatic && !m_FreeObserver && m_View.HasControl && keyboard != null)
            {
                Vector2 axes = Vector2.ClampMagnitude(new Vector2((keyboard.dKey.isPressed ? 1 : 0) - (keyboard.aKey.isPressed ? 1 : 0),
                    (keyboard.wKey.isPressed ? 1 : 0) - (keyboard.sKey.isPressed ? 1 : 0)), 1);
                Vector3 forward = m_View.TangentForward(m_Motor.Current);
                Vector3 desired = forward * axes.y + Vector3.Cross(m_Motor.Current.Up, forward) * axes.x;
                manualDirection = desired;
                input = axes;
            }
            if (m_Hold) { input = Vector2.zero; manualDirection = null; }
            if (input.sqrMagnitude > .01f)
                m_LastWorldIntent = manualDirection ?? (m_Motor.Current.Right * input.x + m_Motor.Current.Forward * input.y);
            if (keyboard != null && keyboard[Key.Equals].wasPressedThisFrame) Escape();
            bool burst = automatic ? Mathf.Repeat(m_RunClock, 1.6f) < .22f :
                keyboard != null && !m_FreeObserver && keyboard.leftShiftKey.isPressed;
            float actualSpeed = speed * (burst ? burstMultiplier : 1);
            float travelBefore = m_Motor.Travel;
            bool wasDetached = m_Motor.Detached;
            Simulate(input, actualSpeed, yaw, dt, manualDirection);
            if (automatic && !wasDetached && !m_Motor.Detached) m_RouteProgress += (m_Motor.Travel - travelBefore) * m_Reverse;
            m_HudTime += Time.unscaledDeltaTime;
            if (m_HudTime >= .1f) { m_HudTime = 0; UpdateHud(); }
        }

        public void Simulate(Vector2 input, float requestedSpeed, float yaw, float dt, Vector3? worldIntent = null)
        {
            // Limit distance per gait tick as well as time: high speed must not carry the hips
            // past a foot's reach before the scheduler gets its next opportunity to plant.
            float maxDt = Mathf.Min(.016f, .055f / Mathf.Max(1, requestedSpeed));
            int steps = Mathf.Max(1, Mathf.CeilToInt(dt / maxDt));
            Vector3 previousUp = m_Motor.Current.Up;
            for (int i = 0; i < steps; i++)
            {
                if (worldIntent.HasValue)
                {
                    // Character intent turns the body toward travel while preserving immediate
                    // camera-relative movement. Transport it through the corner inside this frame.
                    var pose = m_Motor.Current;
                    worldIntent = Quaternion.FromToRotation(previousUp, pose.Up) * worldIntent.Value;
                    previousUp = pose.Up;
                    yaw = worldIntent.Value.sqrMagnitude > .001f ? Mathf.Clamp(Vector3.SignedAngle(pose.Forward,
                        worldIntent.Value, pose.Up) / (dt / steps), -540, 540) : 0;
                    var basis = pose.Rotation * Quaternion.AngleAxis(m_Motor.Transitioning ? 0 : yaw * dt / steps, Vector3.up);
                    input = new Vector2(Vector3.Dot(worldIntent.Value, basis * Vector3.right),
                        Vector3.Dot(worldIntent.Value, basis * Vector3.forward));
                }
                if (input.sqrMagnitude > .01f)
                    m_LastWorldIntent = m_Motor.Current.Right * input.x + m_Motor.Current.Forward * input.y;
                float constrainedSpeed = requestedSpeed * (m_Motor.Detached ? 1 : m_Feet.SpeedScale);
                bool wasDetached = m_Motor.Detached;
                m_Motor.Tick(input, constrainedSpeed, yaw, dt / steps);
                if (m_Motor.Detached)
                {
                    if (!wasDetached) m_Feet.BeginFold();
                    m_Feet.TickFolded(m_Motor.Current, dt / steps);
                }
                else
                {
                    if (wasDetached) { m_Feet.EndFold(m_Motor.Current); m_EscapeCooldown = 1.2f; }
                    m_Feet.Tick(m_Motor, input, constrainedSpeed, yaw, dt / steps);
                }
            }
            DetectStuck(input, dt);
            Draw(dt);
        }

        public void Escape()
        {
            if (m_Motor == null || m_Motor.Detached) return;
            m_Motor.Detach(m_LastWorldIntent);
            m_Feet.BeginFold(); m_Hold = false;
            m_StuckTime = 0; m_EscapeCooldown = 1.2f; m_StuckOrigin = m_Motor.Current.Position;
        }

        private void DetectStuck(Vector2 input, float dt)
        {
            m_EscapeCooldown = Mathf.Max(0, m_EscapeCooldown - dt);
            if (!automaticUnstick || m_Motor.Detached || m_EscapeCooldown > 0 || input.sqrMagnitude < .04f)
            { m_StuckTime = 0; m_StuckOrigin = m_Motor.Current.Position; return; }
            if (Vector3.Distance(m_StuckOrigin, m_Motor.Current.Position) > .18f)
            { m_StuckTime = 0; m_StuckOrigin = m_Motor.Current.Position; return; }
            m_StuckTime += dt;
            if (m_StuckTime >= .75f) Escape();
        }

        private void Draw(float dt)
        {
            transform.SetPositionAndRotation(m_Motor.Current.Position, m_Motor.Current.Rotation);
            m_Presentation.SetAppearance(appearance, showContactMarkers);
            m_Presentation.Draw(m_Motor, m_Feet, stage, dt);
            if (observerCamera && !m_FreeObserver)
            {
                m_View.Draw(m_Motor.Current, automatic && (appearance == SpiderPresentation.Look.Liquid || appearance == SpiderPresentation.Look.Fluid));
            }
        }

        public void SetAppearance(SpiderPresentation.Look look, bool contacts)
        {
            appearance = look; showContactMarkers = contacts;
            m_Presentation?.SetAppearance(look, contacts);
        }

        private void SetObserver(bool free)
        {
            m_FreeObserver = free;
            SpiderCharacterView.Unlock();
            if (free) automatic = true;
            if (previewPlayer) previewPlayer.SetActive(free);
            if (observerCamera) observerCamera.gameObject.SetActive(!free);
        }

        public void ResetExperiment()
        {
            m_Motor.Reset(m_Spawn, m_SpawnRotation); m_Feet.Reset(m_Motor.Current);
            m_Reverse = 1; m_Hold = false;
            m_RunClock = m_RouteProgress = 0;
            m_StuckTime = m_EscapeCooldown = 0; m_StuckOrigin = m_Spawn; m_LastWorldIntent = Vector3.zero;
            Draw(.016f);
        }

        private void UpdateHud()
        {
            if (m_Status == null) m_Status = GetComponent<UIDocument>()?.rootVisualElement.Q<Label>("status");
            if (m_Status == null) return;
            m_Status.text = $"MC00 / UMBRELLA SPIDER / {appearance} / {stage}\n" +
                $"{(automatic ? "AUTO" : "MANUAL")}  {(m_Hold ? "HOLD" : m_Slow ? "0.2x" : "1.0x")}  {m_Motor.Mode}\n" +
                $"Speed {m_Motor.Velocity.magnitude:F2} m/s    Corners {m_Motor.Transitions}\n" +
                $"Stride avg/max {m_Feet.AverageStride:F2}/{m_Feet.MaxCompletedStride:F2} m\n" +
                $"Swing {m_Feet.SwingCount}/8    Recovery {m_Feet.RecoveryCount}    Reach misses {m_Feet.UnreachableCount}\n" +
                $"IK contact error {m_Presentation.MaxContactError:F3} m    Leg obstacles {m_Presentation.LegObstructions}\n" +
                $"Body blocks {m_Motor.Blocked}    Toe blocks {m_Feet.ObstructedSwings}\n\n" +
                $"Emergency escapes {m_Motor.Escapes}   = Fold / detach / land\n" +
                $"F3 Fluid attraction {(m_Presentation.Fluid?.AttractionEnabled == false ? "OFF" : "ON")}   F4 Contact markers\n" +
                "F5 Legacy/Carapace/Liquid/Fluid\n" +
                "F6 Auto/Manual   F7 Player/Follow   F8 Body/Feet/Ribs/Canopy\n" +
                "F9 Slow   F10 Reset   F11 Reverse   F12 Stop\n" +
                "Click to control / Esc release   Mouse orbit / Wheel zoom\n" +
                "Manual: camera-relative WASD   Shift burst";
        }

        private void OnDisable()
        {
            m_Presentation?.Release(); m_Presentation = null;
            if (previewPlayer) previewPlayer.SetActive(m_PlayerWasActive);
            if (observerCamera) observerCamera.gameObject.SetActive(false);
            m_FreeObserver = false;
            SpiderCharacterView.Unlock();
        }
    }
}
