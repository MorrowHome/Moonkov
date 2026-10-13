using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Unity.MP_FPS.SpiderLab
{
    /// <summary>Independent five-species lab. Owns input and the single traversal motor;
    /// selects either a trail/sucker body or a branched grip rig. Both solid and liquid
    /// presentations consume the solved guide. Existing spider/chain actors stay intact.</summary>
    [DisallowMultipleComponent]
    public sealed class ProceduralMenagerieExperiment : MonoBehaviour
    {
        public enum Species { Tentacle, Centipede, Leech, Slime, Roots }
        [SerializeField] private Species species;
        [SerializeField] private ProceduralCreatureExperiment.Appearance appearance = ProceduralCreatureExperiment.Appearance.Fluid;
        [Header("五种独立体形与运动")]
        [SerializeField] private BranchCreatureSettings tentacle = new BranchCreatureSettings();
        [SerializeField] private CreatureChainSettings centipede = CentipedeDefaults();
        [SerializeField] private CreatureChainSettings leech = LeechDefaults();
        [SerializeField] private BranchCreatureSettings slime = BranchCreatureSettings.SlimeDefaults();
        [SerializeField] private BranchCreatureSettings roots = BranchCreatureSettings.RootsDefaults();
        [Header("五种独立液态参数")]
        [SerializeField] private SpiderFluidSettings tentacleFluid = BranchFluidPresentation.Defaults(Species.Tentacle);
        [SerializeField] private SpiderFluidSettings centipedeFluid = CentipedeFluidDefaults();
        [SerializeField] private SpiderFluidSettings leechFluid = ChainFluidPresentation.Defaults(false);
        [SerializeField] private SpiderFluidSettings slimeFluid = BranchFluidPresentation.Defaults(Species.Slime);
        [SerializeField] private SpiderFluidSettings rootsFluid = BranchFluidPresentation.Defaults(Species.Roots);
        [Header("控制与观察")]
        [SerializeField] private bool automatic;
        [SerializeField, Min(2)] private float returnDistance = 14;
        [SerializeField] private LayerMask surfaceMask = Physics.DefaultRaycastLayers;
        [SerializeField] private GameObject previewPlayer;
        [SerializeField] private Camera observerCamera;
        [SerializeField, Range(-180, 180)] private float cameraYaw = -55;
        [SerializeField, Range(-65, 75)] private float cameraPitch = 36;
        [SerializeField, Range(2.8f, 8)] private float cameraDistance = 5.5f;
        private SurfaceMotor m_Motor;
        private SpiderCharacterView m_View;
        private CreatureChain m_Chain;
        private CreatureChainPresentation m_ChainBody;
        private ChainFluidPresentation m_ChainFluid;
        private BranchCreatureRig m_Branch;
        private BranchFluidPresentation m_BranchFluid;
        private Vector3 m_Spawn, m_Intent, m_AutoDirection;
        private Quaternion m_SpawnRotation;
        private bool m_PlayerWasActive, m_FreeObserver, m_Hold, m_Slow;
        private float m_Clock, m_GaitClock, m_ActionRemaining, m_Route, m_Reverse = 1, m_Stuck, m_HudClock;
        private Species m_BuiltSpecies;
        private Label m_Status;
        public Species CurrentSpecies => species;
        public SurfaceMotor Motor => m_Motor;
        public CreatureChain Chain => m_Chain;
        public BranchCreatureRig Branch => m_Branch;
        public CreatureChainPresentation ChainBody => m_ChainBody;
        public Camera ObserverCamera => observerCamera;
        public bool IsChain => species == Species.Centipede || species == Species.Leech;
        public CreatureChainSettings ChainSettings => species == Species.Centipede ? centipede : leech;
        public BranchCreatureSettings BranchSettings => species == Species.Roots ? roots : species == Species.Slime ? slime : tentacle;
        public SkeletonFluidPresentation Fluid => IsChain ? m_ChainFluid?.Fluid : m_BranchFluid?.Fluid;
        public SpiderFluidSettings FluidSettings => species switch
        {
            Species.Centipede => centipedeFluid, Species.Leech => leechFluid,
            Species.Slime => slimeFluid, Species.Roots => rootsFluid, _ => tentacleFluid
        };
        public float SuctionPhase => Mathf.Repeat(m_GaitClock * leech.waveFrequency, 1);
        public bool Automatic { get => automatic; set => automatic = value; }

        public static CreatureChainSettings CentipedeDefaults() => new CreatureChainSettings
        {
            length = 4.4f, radius = .16f, verticalScale = .64f, segments = 22,
            legPairs = 20, legReach = .38f, legRadius = .027f, footLift = .13f,
            bristlesPerSegment = 0, speed = 4.8f, turnSpeed = 260, lateralAmplitude = .075f,
            wavelength = 1.25f, waveFrequency = 2.1f, contraction = 0, archHeight = 0,
            headLift = .12f, sprintHeadLift = .32f, segmentGrooveDepth = .18f,
            headScale = new Vector3(.36f, .20f, .45f), smoothness = .80f,
            headFeature = CreatureChainSettings.HeadFeature.Mandibles
        };
        public static SpiderFluidSettings CentipedeFluidDefaults()
        {
            var style = ChainFluidPresentation.Defaults(true);
            style.bodyAttraction = 950; style.footAttraction = 1000;
            style.bodyDamping = 64; style.footDamping = 76;
            style.bodyAccelerationLimit = 1200; style.tipAccelerationLimit = 1600;
            style.bodySpeedLimit = 60; style.tipSpeedLimit = 80;
            style.surfaceThreshold = .36f; style.liquidMass = .60f; style.appendageMass = .55f;
            return style;
        }
        public static CreatureChainSettings LeechDefaults() => new CreatureChainSettings
        {
            length = 2.8f, radius = .24f, verticalScale = .64f, segments = 12,
            legPairs = 0, bristlesPerSegment = 0, speed = 2.7f, turnSpeed = 150,
            lateralAmplitude = .015f, wavelength = 2.8f, waveFrequency = .95f,
            contraction = .42f, archHeight = .24f, headLift = .03f,
            segmentGrooveDepth = .04f, headScale = new Vector3(.34f, .19f, .30f),
            smoothness = .87f, headFeature = CreatureChainSettings.HeadFeature.Sucker
        };

        private void OnEnable()
        {
            m_Spawn = transform.position; m_SpawnRotation = transform.rotation;
            m_PlayerWasActive = previewPlayer && previewPlayer.activeSelf;
            if (previewPlayer) previewPlayer.SetActive(false);
            m_Motor = new SurfaceMotor(m_Spawn, m_SpawnRotation, surfaceMask);
            m_AutoDirection = m_Motor.Current.Forward;
            m_View = new SpiderCharacterView(observerCamera, m_Motor.Current, surfaceMask, cameraYaw, cameraPitch, cameraDistance);
            m_Status = GetComponent<UIDocument>()?.rootVisualElement.Q<Label>("status");
            BuildRig(); Simulate(Vector3.zero, false, .016f); UpdateHud();
        }

        private void BuildRig()
        {
            ReleaseRig(); m_BuiltSpecies = species;
            if (IsChain)
            {
                m_Chain = new CreatureChain(surfaceMask); m_Chain.Reset(m_Motor.Current, ChainSettings.length);
                m_ChainBody = new CreatureChainPresentation(transform, surfaceMask);
            }
            else m_Branch = new BranchCreatureRig(transform, surfaceMask, BranchSettings, species);
        }
        public void SetSpecies(Species value)
        {
            species = value;
            if (m_Motor == null || m_BuiltSpecies == species) return;
            BuildRig(); m_GaitClock = m_ActionRemaining = m_Stuck = 0;
            Simulate(Vector3.zero, false, .016f); UpdateHud();
        }
        private void Update()
        {
            SetSpecies(species);
            if (!m_FreeObserver) m_View.ReadInput();
            var key = Keyboard.current;
            if (key != null)
            {
                if (key.f1Key.wasPressedThisFrame) SetSpecies((Species)(((int)species + 1) % 5));
                if (key.f2Key.wasPressedThisFrame) FluidSettings.heavy = !FluidSettings.heavy;
                if (key.f3Key.wasPressedThisFrame) FluidSettings.attraction = !FluidSettings.attraction;
                if (key.f5Key.wasPressedThisFrame) SetAppearance(appearance == ProceduralCreatureExperiment.Appearance.Fluid ? ProceduralCreatureExperiment.Appearance.Solid : ProceduralCreatureExperiment.Appearance.Fluid);
                if (key.f6Key.wasPressedThisFrame) { automatic = !automatic; m_Route = 0; }
                if (key.f7Key.wasPressedThisFrame) SetObserver(!m_FreeObserver);
                if (key.f9Key.wasPressedThisFrame) m_Slow = !m_Slow;
                if (key.f10Key.wasPressedThisFrame) ResetExperiment();
                if (key.f12Key.wasPressedThisFrame) m_Hold = !m_Hold;
                if (key[Key.Equals].wasPressedThisFrame) Escape();
                if (key.spaceKey.wasPressedThisFrame && !m_FreeObserver && m_View.HasControl) Act();
            }
            Vector3 intent = Vector3.zero;
            if (automatic)
            {
                if (m_Route > returnDistance) m_Reverse = -1;
                else if (m_Route < 0) m_Reverse = 1;
                intent = m_AutoDirection * m_Reverse;
            }
            else if (!m_FreeObserver && m_View.HasControl && key != null)
            {
                Vector2 axes = Vector2.ClampMagnitude(new Vector2((key.dKey.isPressed ? 1 : 0) - (key.aKey.isPressed ? 1 : 0),
                    (key.wKey.isPressed ? 1 : 0) - (key.sKey.isPressed ? 1 : 0)), 1);
                Vector3 forward = m_View.TangentForward(m_Motor.Current);
                intent = forward * axes.y + Vector3.Cross(m_Motor.Current.Up, forward) * axes.x;
            }
            if (m_Hold) intent = Vector3.zero;
            Simulate(intent, !m_FreeObserver && key != null && key.leftShiftKey.isPressed,
                Mathf.Min(Time.deltaTime, .08f) * (m_Slow ? .2f : 1));
            m_HudClock += Time.unscaledDeltaTime;
            if (m_HudClock > .15f) { m_HudClock = 0; UpdateHud(); }
        }

        public void Simulate(Vector3 intent, bool burst, float dt)
        {
            if (!IsChain && !m_Branch.Matches(BranchSettings, species)) BuildRig();
            if (species == Species.Leech && m_ActionRemaining > 0 && intent.sqrMagnitude < .001f)
                intent = m_Motor.Current.Forward;
            bool requested = intent.sqrMagnitude > .001f;
            m_Clock += dt; if (requested) m_GaitClock += dt * (burst ? 1.5f : 1);
            float duration = IsChain ? .75f : BranchSettings.actionDuration;
            float action = m_ActionRemaining > 0 ? Mathf.Sin(Mathf.Clamp01(m_ActionRemaining / duration) * Mathf.PI) : 0;
            m_ActionRemaining = Mathf.Max(0, m_ActionRemaining - dt);
            float speed = IsChain ? ChainSettings.speed : BranchSettings.speed;
            speed *= burst ? (IsChain ? ChainSettings.burstMultiplier : BranchSettings.burstMultiplier) : 1;
            if (species == Species.Leech) speed *= SuctionPhase < .58f && m_Chain.SuckerSpan < leech.length * 1.25f ? .8f : 0;
            if (species == Species.Tentacle) speed *= .60f + .40f * Mathf.Pow(Mathf.Sin(m_GaitClock * BranchSettings.frequency * Mathf.PI), 2);
            if (species == Species.Roots) speed *= .65f + .35f * Mathf.Sin(m_GaitClock * BranchSettings.frequency * 6.28f) * Mathf.Sin(m_GaitClock * BranchSettings.frequency * 6.28f);
            speed *= 1 + action * (species == Species.Leech ? 1.8f : .35f);
            float turn = IsChain ? ChainSettings.turnSpeed : BranchSettings.turnSpeed;
            Vector3 firstUp = m_Motor.Current.Up, previousUp = firstUp;
            float previousTravel = m_Motor.Travel;
            int steps = Mathf.Max(1, Mathf.CeilToInt(dt / .012f));
            for (int i = 0; i < steps; i++)
            {
                var pose = m_Motor.Current;
                intent = Quaternion.FromToRotation(previousUp, pose.Up) * intent; previousUp = pose.Up;
                float yaw = requested ? Mathf.Clamp(Vector3.SignedAngle(pose.Forward, intent, pose.Up) / Mathf.Max(.001f, dt), -turn, turn) : 0;
                Quaternion basis = pose.Rotation * Quaternion.AngleAxis(m_Motor.Transitioning ? 0 : yaw * dt / steps, Vector3.up);
                Vector2 axes = new Vector2(Vector3.Dot(intent, basis * Vector3.right), Vector3.Dot(intent, basis * Vector3.forward));
                m_Motor.Tick(axes, speed, yaw, dt / steps);
            }
            if (requested) m_Intent = intent;
            m_AutoDirection = Quaternion.FromToRotation(firstUp, m_Motor.Current.Up) * m_AutoDirection;
            float travel = m_Motor.Travel - previousTravel;
            if (automatic) m_Route += travel * m_Reverse;
            // The deliberate rear-recovery half of the leech gait must not count as a jam.
            bool recoveringRear = species == Species.Leech && SuctionPhase >= .58f;
            m_Stuck = requested && !recoveringRear && travel < .002f ? m_Stuck + dt : 0;
            if (m_Stuck > (IsChain ? ChainSettings.stuckDelay : BranchSettings.stuckDelay) && !m_Motor.Detached) Escape();
            transform.SetPositionAndRotation(m_Motor.Current.Position, m_Motor.Current.Rotation);
            float drive = Mathf.Clamp01(m_Motor.Velocity.magnitude / Mathf.Max(.1f, IsChain ? ChainSettings.speed : BranchSettings.speed));
            if (IsChain)
            {
                if (species == Species.Leech) m_Chain.TickSuction(m_Motor.Current, ChainSettings, SuctionPhase, requested ? 1 : 0, dt, m_Motor.Detached);
                else m_Chain.Tick(m_Motor.Current, ChainSettings, true, drive, dt, burst || action > .1f ? 1 : 0, m_Motor.Detached);
                m_ChainBody.Draw(m_Chain, ChainSettings, true, m_Clock, dt, m_Motor.Detached);
                m_ChainBody.SetVisible(appearance == ProceduralCreatureExperiment.Appearance.Solid);
                if (appearance == ProceduralCreatureExperiment.Appearance.Fluid)
                {
                    if (m_ChainFluid == null || !m_ChainFluid.Matches(ChainSettings, true))
                    { m_ChainFluid?.Release(); m_ChainFluid = new ChainFluidPresentation(transform, surfaceMask, ChainSettings, true); }
                    m_ChainFluid.Draw(m_Chain, m_ChainBody, ChainSettings, m_Motor, FluidSettings, dt);
                }
                else m_ChainFluid?.Fluid.SetVisible(false);
            }
            else
            {
                m_Branch.Tick(m_Motor, BranchSettings, m_Clock, drive, action, dt);
                m_Branch.SetVisible(appearance == ProceduralCreatureExperiment.Appearance.Solid);
                if (appearance == ProceduralCreatureExperiment.Appearance.Fluid)
                {
                    if (m_BranchFluid == null) m_BranchFluid = new BranchFluidPresentation(transform, surfaceMask, m_Branch, BranchSettings, species);
                    m_BranchFluid.Draw(m_Motor, FluidSettings, dt);
                }
                else m_BranchFluid?.Fluid.SetVisible(false);
            }
            if (!m_FreeObserver)
            {
                var framing = m_Motor.Current;
                framing.Position = IsChain ? m_Chain.Centres[CreatureChain.Rings / 2] : m_Branch.Points[0];
                m_View.Draw(framing, automatic, 1.5f);
            }
        }

        public void Act() { m_ActionRemaining = IsChain ? .75f : BranchSettings.actionDuration; }
        public void SetAppearance(ProceduralCreatureExperiment.Appearance value)
        {
            appearance = value;
            m_ChainBody?.SetVisible(value == ProceduralCreatureExperiment.Appearance.Solid);
            m_Branch?.SetVisible(value == ProceduralCreatureExperiment.Appearance.Solid);
            Fluid?.SetVisible(value == ProceduralCreatureExperiment.Appearance.Fluid);
        }
        public void Escape()
        {
            if (m_Motor == null || m_Motor.Detached) return;
            m_Motor.Detach(m_Intent); m_Chain?.ReleaseContacts(); m_ChainBody?.ResetContacts(); m_Branch?.ResetContacts();
            m_Stuck = 0; m_Hold = false;
        }
        public void ResetExperiment()
        {
            m_Motor.Reset(m_Spawn, m_SpawnRotation); m_AutoDirection = m_Motor.Current.Forward;
            m_Clock = m_GaitClock = m_ActionRemaining = m_Route = m_Stuck = 0; m_Reverse = 1; m_Hold = false;
            BuildRig(); Simulate(Vector3.zero, false, .016f);
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
            string contacts = IsChain ? species == Species.Leech ? $"Sucker {(m_Chain.RearSuckerHolding ? "REAR HOLD / EXTEND" : "FRONT HOLD / CONTRACT")}" :
                $"Planted legs {m_ChainBody.PlantedFeet} / Slip {m_ChainBody.MaximumAnchorSlip:F3} m" :
                $"Grips {m_Branch.Grips}/{m_Branch.Arms} / Slip {m_Branch.MaximumAnchorSlip:F3} m";
            m_Status.text = $"MC00 / {species.ToString().ToUpperInvariant()}\n{appearance} / {(automatic ? "AUTO" : "MANUAL")} / {m_Motor.Mode}\n" +
                $"Speed {m_Motor.Velocity.magnitude:F2} / Travel {m_Motor.Travel:F1} m\n{contacts}\nFluid {Fluid?.ParticleCount ?? 0} particles\n\n" +
                "F1 Next species   F5 Solid / Fluid\nF2 Heavy / Original   F3 Attraction\nF6 Auto   F7 Existing player\nSpace Species action / Shift Burst\nF9 Slow   F10 Reset   F12 Stop\n= Release / fold / land\n\nClick to control / Esc release\nMouse orbit / Wheel zoom\nWASD camera-relative\n\nInspector: five shape and fluid presets";
        }
        private void ReleaseRig()
        {
            m_ChainFluid?.Release(); m_ChainFluid = null; m_BranchFluid?.Release(); m_BranchFluid = null;
            m_ChainBody?.Release(); m_ChainBody = null; m_Branch?.Release(); m_Branch = null; m_Chain = null;
        }
        private void OnDisable()
        {
            ReleaseRig();
            if (previewPlayer) previewPlayer.SetActive(m_PlayerWasActive);
            if (observerCamera) observerCamera.gameObject.SetActive(false);
            SpiderCharacterView.Unlock();
        }
    }
}
