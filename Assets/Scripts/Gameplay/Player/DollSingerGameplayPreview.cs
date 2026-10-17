using Unity.Mathematics;
using Unity.MP_FPS.DollSinger;
using UnityEngine;

namespace Unity.MP_FPS
{
    /// <summary>
    /// Offline owner of the real player motor. DollSingerMovement remains enabled only for
    /// animation/IK, exactly as in network presentation; it must never also simulate movement.
    /// No ghost, health modifiers, prediction replay or server is needed to inspect a map.
    /// </summary>
    [DefaultExecutionOrder(-50), DisallowMultipleComponent]
    [RequireComponent(typeof(FirstPersonController))]
    public sealed class DollSingerGameplayPreview : MonoBehaviour
    {
        [SerializeField] private GameObject gameplayPrefab;
        [SerializeField] private DollSingerMovement model;
        [SerializeField] private DollSingerView view;
        [SerializeField] private DollSingerInput input;
        private FirstPersonController motor;
        private FirstPersonController.ControllerConsts constants;
        private FirstPersonController.ControllerState state;
        private float2 look, freeLookOffset;
        private float lean;
        private bool initialized;

        private void Start()
        {
            if (!gameplayPrefab || !model || !view || !input || GetComponent<PlayerGhost>())
            {
                Debug.LogError("Gameplay preview requires local references and a gameplay prefab; never attach it to a ghost.", this);
                enabled = false;
                return;
            }
            var tuning = gameplayPrefab.GetComponent<PredictedPlayerControllerConstsAuthoring>();
            var sourceMotor = gameplayPrefab.GetComponent<FirstPersonController>();
            if (!tuning || !sourceMotor)
            {
                Debug.LogError("Gameplay preview source has no movement configuration.", this);
                enabled = false;
                return;
            }
            motor = GetComponent<FirstPersonController>();
            motor.ConfigureStandalonePreview(sourceMotor);
            constants = tuning.BuildControllerConsts();
            look = new float2(transform.eulerAngles.y, 0f);
            state = new FirstPersonController.ControllerState
            {
                CurrentPosition = transform.position,
                CurrentRotation = transform.rotation,
                YawDegrees = look.x,
                GroundNormal = math.up(),
                MovementType = FirstPersonController.MovementType.Falling,
                JumpTimeoutDelta = constants.JumpTimeout,
                HaloLightEnabled = true
            };
            view.SetFirstPerson(true);
            model.SetNetworkViewPresentation(true, Quaternion.Euler(0f, look.x, 0f), 0f);
            view.SetNetworkLookRotation(Quaternion.Euler(0f, look.x, 0f));
            motor.GroundedCheck(ref state, constants);
            initialized = true;
        }

        private void Update()
        {
            if (!initialized || Time.deltaTime <= 0f) return;
            bool canRead = input.CanReadPlayerInput;
            bool freeLooking = DollSingerLookController.Update(ref look, ref freeLookOffset,
                ref lean, out var viewRotation, input, model, view, !canRead, Time.deltaTime);
            var command = new PlayerInput
            {
                LookYawPitchDegrees = look,
                FreeLookOffset = freeLookOffset,
                FreeLooking = freeLooking,
                Lean = canRead && view.IsFirstPerson ? lean : 0f,
                MoveInput = canRead ? (float2)input.Move : float2.zero
            };
            command.SetStance(input.Stance);
            command.SetFlag(PlayerInput.InputFlag.Jump, canRead && input.JumpPressed);
            command.SetFlag(PlayerInput.InputFlag.Sprint, canRead && input.SprintHeld);
            command.SetFlag(PlayerInput.InputFlag.Aim, canRead && input.AimHeld && !freeLooking);
            command.SetFlag(PlayerInput.InputFlag.ThirdPerson, !view.IsFirstPerson);

            var displacement = float3.zero;
            FirstPersonController.AccumulateMovement(ref state, ref displacement, command, constants, Time.deltaTime, motor);
            motor.ApplyMovementUpdate(ref state, constants, displacement, Time.deltaTime);
            model.ApplyNetworkPresentation(state.MovementSpeed, state.JumpFallSpeed,
                state.MovementType == FirstPersonController.MovementType.Standing,
                state.Sprinting, state.JumpTriggered, Time.deltaTime,
                state.AnimatorMotion, ((Quaternion)state.CurrentRotation).eulerAngles.y, state.Stance);
            model.SetNetworkViewPresentation(view.IsFirstPerson, viewRotation, lean, freeLooking);
            state.JumpTriggered = state.LandTriggered = false;
        }
    }
}
