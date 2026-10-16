using Unity.Mathematics;
using Unity.MP_FPS.DollSinger;
using UnityEngine;

namespace Unity.MP_FPS
{
    // Local owner for an instance of the current formal player prefab. The asset and
    // network components remain intact; this walkthrough does not create a network world.
    [DefaultExecutionOrder(-50)]
    public sealed class AirlockGameplayPlayer : MonoBehaviour
    {
        private FirstPersonController m_Motor;
        private FirstPersonController.ControllerConsts m_Constants;
        private FirstPersonController.ControllerState m_State;
        private DollSingerMovement m_Model;
        private DollSingerView m_View;
        private DollSingerInput m_Input;
        private float2 m_Look, m_FreeLook;
        private float m_Lean;

        public void Configure(GameObject formalPrefab)
        {
            m_Motor = GetComponent<FirstPersonController>();
            m_Motor.ConfigureStandalonePreview(formalPrefab.GetComponent<FirstPersonController>());
            m_Constants = formalPrefab.GetComponent<PredictedPlayerControllerConstsAuthoring>().BuildControllerConsts();
            m_Model = GetComponentInChildren<DollSingerMovement>(true);
            m_View = GetComponentInChildren<DollSingerView>(true);
            m_Input = GetComponentInChildren<DollSingerInput>(true);
            m_Model.gameObject.SetActive(true); m_Model.enabled = true; m_Input.enabled = true;
            foreach (var capsule in GetComponentsInChildren<UnityEngine.CharacterController>(true))
                capsule.enabled = capsule.gameObject == gameObject;
            var remote = transform.Find("RemotePresentation"); if (remote) remote.gameObject.SetActive(false);
            m_View.gameObject.SetActive(true); m_View.BindPlayer(m_Model.gameObject, true); m_View.SetFirstPerson(true);
            m_View.camera.enabled = true; m_View.camera.tag = "MainCamera";
            var listener = m_View.camera.GetComponent<AudioListener>(); if (listener) listener.enabled = true;
            m_Look = new float2(transform.eulerAngles.y, 0);
            m_State = new FirstPersonController.ControllerState {
                CurrentPosition = transform.position, CurrentRotation = transform.rotation,
                YawDegrees = m_Look.x, GroundNormal = math.up(),
                MovementType = FirstPersonController.MovementType.Falling,
                JumpTimeoutDelta = m_Constants.JumpTimeout, HaloLightEnabled = true
            };
            m_Model.ApplyNetworkPresentation(0, 0, false, false, false, .016f, Vector3.zero, m_Look.x);
            m_Model.SetNetworkViewPresentation(true, transform.rotation, 0);
            m_View.SetNetworkLookRotation(transform.rotation);
        }
        private void Update()
        {
            if (!m_Motor || Time.deltaTime <= 0) return;
            bool canRead = m_Input.CanReadPlayerInput;
            bool free = DollSingerLookController.Update(ref m_Look, ref m_FreeLook, ref m_Lean,
                out var rotation, m_Input, m_Model, m_View, !canRead, Time.deltaTime);
            var command = new PlayerInput { LookYawPitchDegrees = m_Look, FreeLookOffset = m_FreeLook,
                FreeLooking = free, Lean = m_Lean, MoveInput = canRead ? (float2)m_Input.Move : float2.zero };
            command.SetFlag(PlayerInput.InputFlag.Jump, canRead && m_Input.JumpPressed);
            command.SetFlag(PlayerInput.InputFlag.Sprint, canRead && m_Input.SprintHeld);
            command.SetFlag(PlayerInput.InputFlag.Aim, canRead && m_Input.AimHeld && !free);
            command.SetFlag(PlayerInput.InputFlag.ThirdPerson, !m_View.IsFirstPerson);
            var move = float3.zero;
            FirstPersonController.AccumulateMovement(ref m_State, ref move, command, m_Constants, Time.deltaTime);
            m_Motor.ApplyMovementUpdate(ref m_State, m_Constants, move, Time.deltaTime);
            m_Model.ApplyNetworkPresentation(m_State.MovementSpeed, m_State.JumpFallSpeed,
                m_State.MovementType == FirstPersonController.MovementType.Standing, m_State.Sprinting,
                m_State.JumpTriggered, Time.deltaTime, m_State.AnimatorMotion, ((Quaternion)m_State.CurrentRotation).eulerAngles.y);
            m_Model.SetNetworkViewPresentation(m_View.IsFirstPerson, rotation, m_Lean, free);
            m_State.JumpTriggered = m_State.LandTriggered = false;
        }
    }
}
