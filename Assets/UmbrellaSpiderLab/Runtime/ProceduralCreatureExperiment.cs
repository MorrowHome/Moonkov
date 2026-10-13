using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Unity.MP_FPS.SpiderLab
{
    /// <summary>Offline character controller for two chain-bodied creatures. Owns intent,
    /// head traversal, trail and rendering in that order; no combat/network authority.</summary>
    [DisallowMultipleComponent]
    public sealed class ProceduralCreatureExperiment : MonoBehaviour
    {
        public enum Species { Snake, Caterpillar }
        public enum Appearance { Solid, Fluid }
        [SerializeField] private Species species;
        [SerializeField] private Appearance appearance = Appearance.Solid;
        [SerializeField] private CreatureChainSettings snake = new CreatureChainSettings();
        [SerializeField] private CreatureChainSettings caterpillar = CreatureChainSettings.CaterpillarDefaults();
        [Header("液态 / 两个物种独立调参")]
        [SerializeField] private SpiderFluidSettings snakeFluid = ChainFluidPresentation.Defaults(false);
        [SerializeField] private SpiderFluidSettings caterpillarFluid = ChainFluidPresentation.Defaults(true);
        [Header("实验控制")]
        [SerializeField] private bool automatic;
        [SerializeField, Min(1)] private float returnDistance = 18;
        [SerializeField] private LayerMask surfaceMask = Physics.DefaultRaycastLayers;
        [SerializeField] private GameObject previewPlayer;
        [SerializeField] private Camera observerCamera;
        [SerializeField, Range(-180, 180)] private float cameraYaw = -60;
        [SerializeField, Range(-65, 75)] private float cameraPitch = 40;
        [SerializeField, Range(2.8f, 8)] private float cameraDistance = 4.5f;
        private SurfaceMotor m_Motor;
        private CreatureChain m_Chain;
        private CreatureChainPresentation m_Presentation;
        private ChainFluidPresentation m_Fluid;
        private SpiderCharacterView m_View;
        private Species m_CurrentSpecies;
        private Vector3 m_Spawn, m_Intent, m_AutoDirection;
        private Quaternion m_SpawnRotation;
        private bool m_PlayerWasActive, m_FreeObserver, m_Hold, m_Slow;
        private float m_Clock, m_Route, m_Stuck, m_Reverse = 1, m_HudClock;
        private Label m_Status;
        public Species CurrentSpecies => species;
        public CreatureChainSettings Settings => species == Species.Snake ? snake : caterpillar;
        public SurfaceMotor Motor => m_Motor;
        public CreatureChain Chain => m_Chain;
        public CreatureChainPresentation Presentation => m_Presentation;
        public Appearance CurrentAppearance => appearance;
        public SpiderFluidSettings FluidSettings => species == Species.Snake ? snakeFluid : caterpillarFluid;
        public SkeletonFluidPresentation Fluid => m_Fluid?.Fluid;

        private void OnEnable()
        {
            m_Spawn = transform.position; m_SpawnRotation = transform.rotation;
            m_PlayerWasActive = previewPlayer && previewPlayer.activeSelf;
            if (previewPlayer) previewPlayer.SetActive(false);
            m_Motor = new SurfaceMotor(m_Spawn, m_SpawnRotation, surfaceMask);
            m_AutoDirection = m_Motor.Current.Forward;
            m_Chain = new CreatureChain(surfaceMask);
            m_Chain.Reset(m_Motor.Current, Settings.length);
            m_CurrentSpecies = species;
            m_Presentation = new CreatureChainPresentation(transform, surfaceMask);
            m_View = new SpiderCharacterView(observerCamera, m_Motor.Current, surfaceMask, cameraYaw, cameraPitch, cameraDistance);
            m_Status = GetComponent<UIDocument>()?.rootVisualElement.Q<Label>("status");
            Simulate(Vector3.zero, false, .016f);
        }

        public void SetSpecies(Species value)
        {
            species = value;
            if (m_CurrentSpecies == species || m_Chain == null) return;
            m_CurrentSpecies = species;
            m_Chain.ReleaseContacts();
            m_Presentation.ResetContacts();
        }

        private void Update()
        {
            SetSpecies(species);
            if (!m_FreeObserver) m_View.ReadInput();
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.f1Key.wasPressedThisFrame) SetSpecies(species == Species.Snake ? Species.Caterpillar : Species.Snake);
                if (keyboard.f2Key.wasPressedThisFrame) FluidSettings.heavy = !FluidSettings.heavy;
                if (keyboard.f3Key.wasPressedThisFrame) FluidSettings.attraction = !FluidSettings.attraction;
                if (keyboard.f5Key.wasPressedThisFrame) SetAppearance(appearance == Appearance.Fluid ? Appearance.Solid : Appearance.Fluid);
                if (keyboard.f6Key.wasPressedThisFrame) { automatic = !automatic; m_Route = 0; }
                if (keyboard.f7Key.wasPressedThisFrame) SetObserver(!m_FreeObserver);
                if (keyboard.f9Key.wasPressedThisFrame) m_Slow = !m_Slow;
                if (keyboard.f10Key.wasPressedThisFrame) ResetExperiment();
                if (keyboard.f12Key.wasPressedThisFrame) m_Hold = !m_Hold;
                if (keyboard[Key.Equals].wasPressedThisFrame) Escape();
            }
            Vector3 desired = Vector3.zero;
            if (automatic)
            {
                if (m_Route > returnDistance) m_Reverse = -1;
                else if (m_Route < 0) m_Reverse = 1;
                desired = m_AutoDirection * m_Reverse;
            }
            else if (!m_FreeObserver && m_View.HasControl && keyboard != null)
            {
                Vector2 axes = Vector2.ClampMagnitude(new Vector2((keyboard.dKey.isPressed ? 1 : 0) - (keyboard.aKey.isPressed ? 1 : 0),
                    (keyboard.wKey.isPressed ? 1 : 0) - (keyboard.sKey.isPressed ? 1 : 0)), 1);
                Vector3 forward = m_View.TangentForward(m_Motor.Current);
                desired = forward * axes.y + Vector3.Cross(m_Motor.Current.Up, forward) * axes.x;
            }
            if (m_Hold) desired = Vector3.zero;
            bool burst = keyboard != null && keyboard.leftShiftKey.isPressed && !m_FreeObserver;
            Simulate(desired, burst, Mathf.Min(Time.deltaTime, .08f) * (m_Slow ? .2f : 1));
            m_HudClock += Time.unscaledDeltaTime;
            if (m_HudClock >= .15f) { m_HudClock = 0; UpdateHud(); }
        }

        public void Simulate(Vector3 intent, bool burst, float dt)
        {
            var settings = Settings;
            float previousTravel = m_Motor.Travel;
            m_Clock += dt;
            bool larva = species == Species.Caterpillar;
            float speed = settings.speed * (burst ? settings.burstMultiplier : 1);
            if (larva) speed *= .35f + .65f * (.5f + .5f * Mathf.Sin(m_Chain.Phase));
            int steps = Mathf.Max(1, Mathf.CeilToInt(dt / .012f));
            Vector3 previousUp = m_Motor.Current.Up;
            Vector3 initialUp = previousUp;
            for (int i = 0; i < steps; i++)
            {
                var pose = m_Motor.Current;
                intent = Quaternion.FromToRotation(previousUp, pose.Up) * intent;
                previousUp = pose.Up;
                float yaw = intent.sqrMagnitude > .001f ? Mathf.Clamp(Vector3.SignedAngle(pose.Forward, intent, pose.Up) / Mathf.Max(dt, .001f), -settings.turnSpeed, settings.turnSpeed) : 0;
                var basis = pose.Rotation * Quaternion.AngleAxis(m_Motor.Transitioning ? 0 : yaw * dt / steps, Vector3.up);
                Vector2 input = new Vector2(Vector3.Dot(intent, basis * Vector3.right), Vector3.Dot(intent, basis * Vector3.forward));
                if (intent.sqrMagnitude > .001f) m_Intent = intent;
                m_Motor.Tick(input, speed, yaw, dt / steps);
            }
            m_AutoDirection = Quaternion.FromToRotation(initialUp, m_Motor.Current.Up) * m_AutoDirection;
            float travelled = m_Motor.Travel - previousTravel;
            if (automatic) m_Route += travelled * m_Reverse;
            m_Stuck = intent.sqrMagnitude > .01f && (travelled < .003f || m_Chain.BodySweepsBlocked > CreatureChain.Rings / 2) ? m_Stuck + dt : 0;
            if (m_Stuck > settings.stuckDelay && !m_Motor.Detached) Escape();
            transform.SetPositionAndRotation(m_Motor.Current.Position, m_Motor.Current.Rotation);
            float drive = Mathf.Clamp01(m_Motor.Velocity.magnitude / Mathf.Max(.1f, settings.speed));
            m_Chain.Tick(m_Motor.Current, settings, larva, drive, dt, burst ? 1 : 0, m_Motor.Detached);
            m_Presentation.Draw(m_Chain, settings, larva, m_Clock, dt, m_Motor.Detached);
            m_Presentation.SetVisible(appearance == Appearance.Solid);
            if (appearance == Appearance.Fluid)
            {
                if (m_Fluid == null || !m_Fluid.Matches(settings, larva))
                {
                    m_Fluid?.Release(); m_Fluid = new ChainFluidPresentation(transform, surfaceMask, settings, larva);
                }
                m_Fluid.Draw(m_Chain, m_Presentation, settings, m_Motor, FluidSettings, dt);
            }
            else m_Fluid?.Fluid.SetVisible(false);
            if (!m_FreeObserver)
            {
                var framing = m_Motor.Current;
                framing.Position = m_Chain.Centres[CreatureChain.Rings / 2];
                framing.Rotation = Quaternion.LookRotation(m_Chain.Tangents[CreatureChain.Rings / 2], m_Chain.Ups[CreatureChain.Rings / 2]);
                m_View.Draw(framing, automatic, 1.4f);
            }
        }

        public void ResetExperiment()
        {
            m_Motor.Reset(m_Spawn, m_SpawnRotation); m_Chain.Reset(m_Motor.Current, Settings.length);
            m_AutoDirection = m_Motor.Current.Forward;
            m_Presentation.ResetContacts(); m_Reverse = 1; m_Route = m_Stuck = m_Clock = 0; m_Hold = false;
            // Reinitialize fluid at the new guide instead of transporting a stale cloud across
            // a reset's arbitrary world-space distance.
            m_Fluid?.Release(); m_Fluid = null;
        }
        public void SetAppearance(Appearance value)
        {
            appearance = value;
            m_Presentation?.SetVisible(value == Appearance.Solid);
            m_Fluid?.Fluid.SetVisible(value == Appearance.Fluid);
        }
        public void Escape()
        {
            if (m_Motor == null || m_Motor.Detached) return;
            m_Motor.Detach(m_Intent); m_Chain.ReleaseContacts(); m_Presentation.ResetContacts();
            m_Stuck = 0; m_Hold = false;
        }

        private void SetObserver(bool free)
        {
            m_FreeObserver = free; SpiderCharacterView.Unlock();
            if (free) automatic = true;
            if (previewPlayer) previewPlayer.SetActive(free);
            if (observerCamera) observerCamera.gameObject.SetActive(!free);
        }

        private void UpdateHud()
        {
            if (m_Status == null) m_Status = GetComponent<UIDocument>()?.rootVisualElement.Q<Label>("status");
            if (m_Status == null) return;
            m_Status.text = $"MC00 / PROCEDURAL {(species == Species.Snake ? "SNAKE" : "CATERPILLAR")}\n" +
                $"{appearance} / {(automatic ? "AUTO" : "MANUAL")} / {m_Motor.Mode}\n" +
                $"Speed {m_Motor.Velocity.magnitude:F2} m/s / Travel {m_Motor.Travel:F1} m\n" +
                $"Grounded body {CreatureChain.Rings - m_Chain.UnsupportedRings}/{CreatureChain.Rings}\n" +
                $"Planted prolegs {m_Presentation.PlantedFeet} / Anchor slip {m_Presentation.MaximumAnchorSlip:F3} m\n\n" +
                $"Body sweeps {m_Chain.BodySweepsBlocked} / Self contacts {m_Chain.SelfSeparations}\n" +
                $"Fluid {Fluid?.ParticleCount ?? 0} particles / F2 {(FluidSettings.heavy ? "HEAVY" : "ORIGINAL")}\n" +
                "F1 Snake / Caterpillar   F5 Solid / Fluid\nF3 Attraction on/off\nF6 Auto / Manual   F7 Existing player\nF9 Slow   F10 Reset   F12 Stop\n= Fold / detach / land\n\n" +
                "Click to control / Esc release\nMouse orbit / Wheel zoom\nWASD camera-relative / Shift burst\n\nInspector: separate Snake / Caterpillar settings";
        }

        private void OnDisable()
        {
            m_Presentation?.Release(); m_Presentation = null;
            m_Fluid?.Release(); m_Fluid = null;
            if (previewPlayer) previewPlayer.SetActive(m_PlayerWasActive);
            if (observerCamera) observerCamera.gameObject.SetActive(false);
            SpiderCharacterView.Unlock();
        }
    }
}
