using Unity.MP_FPS.DollSinger;
using UnityEngine;
using Unity.Mathematics;

namespace Unity.MP_FPS
{
    /// <summary>Client presentation for the template's predicted player, with no independent movement simulation.</summary>
    [DefaultExecutionOrder(-50)]
    public sealed class DollSingerNetworkPresentation : GhostMonoBehaviour
    {
        [SerializeField] private DollSingerMovement m_Model;
        [SerializeField] private DollSingerInput m_Input;
        [SerializeField] private DollSingerView m_View;
        [SerializeField] private DollSingerHaloAim m_Halo;
        private bool m_Linked;
        private bool m_ViewActivated;
        private uint m_LastJumpTick;
        private readonly RaycastHit[] m_AimHits = new RaycastHit[32];

        public DollSingerInput OwnedInput => m_Linked && Role == MultiplayerRole.ClientOwned ? m_Input : null;
        public bool IsThirdPerson => m_View != null && !m_View.IsFirstPerson;

        public Vector3 CaptureAimPoint(float2 look, float range)
        {
            m_View.SetNetworkLookRotation(Quaternion.Euler(look.y, look.x, 0f));
            var ray = m_View.camera.ViewportPointToRay(new Vector3(0.5f, 0.5f));
            var target = ray.GetPoint(range);
            // Host server colliders duplicate the client avatars; ignore that world here.
            int count = UnityEngine.Physics.RaycastNonAlloc(ray, m_AimHits, range,
                ~LayerMask.GetMask("ServerPlayer"), QueryTriggerInteraction.Ignore);
            float nearest = range;
            for (int i = 0; i < count; i++)
            {
                var hit = m_AimHits[i];
                if (!hit.collider || hit.collider.transform.IsChildOf(transform) || hit.distance >= nearest)
                    continue;
                nearest = hit.distance;
                target = hit.point;
            }
            return target;
        }

        public override void OnGhostLinked()
        {
            m_Linked = true;
            m_Model.enabled = false;
            m_Model.GetComponent<UnityEngine.CharacterController>().enabled = false;
            m_Model.gameObject.SetActive(Role != MultiplayerRole.Server);
            if (Role == MultiplayerRole.ClientOwned)
                ActivateOwnedView();
            else
            {
                m_Input.enabled = false;
                m_View.gameObject.SetActive(false);
            }
        }

        public Camera ActivateOwnedView()
        {
            if (m_ViewActivated) return m_View.camera;
            m_ViewActivated = true;
            m_Model.gameObject.SetActive(true);
            m_View.gameObject.SetActive(true);
            m_Input.enabled = true;
            m_View.BindPlayer(m_Model.gameObject, true);
            m_View.camera.tag = "MainCamera";
            m_View.camera.enabled = true;
            return m_View.camera;
        }

        private void Update()
        {
            if (!m_Linked || Role == MultiplayerRole.Server || GhostGameObject.IsPendingDestroy)
                return;

            var ghost = ReadGhostComponentData<PredictedPlayerGhost>();
            var state = ghost.ControllerState;
            bool jumped = ghost.LastJumpTick != 0 &&
                (m_LastJumpTick == 0 || (int)(ghost.LastJumpTick - m_LastJumpTick) > 0);
            if (jumped) m_LastJumpTick = ghost.LastJumpTick;
            m_Model.ApplyNetworkPresentation(state.MovementSpeed, state.JumpFallSpeed,
                state.MovementType == FirstPersonController.MovementType.Standing,
                state.Sprinting, jumped, Time.deltaTime);
            var viewRotation = Quaternion.Euler(state.PitchDegrees, state.YawDegrees, 0f);
            m_Halo.SetNetworkPresentation(state.Aiming, viewRotation * Vector3.forward, ghost.AimPoint);
            if (Role == MultiplayerRole.ClientOwned)
            {
                m_View.SetNetworkLookRotation(viewRotation);
                m_View.SetAimBlend(m_Halo.AimBlend);
            }
        }

        public void PlayShot(Vector3 aimPoint)
        {
            if (m_Linked && Role != MultiplayerRole.Server)
                m_Halo.PlayNetworkShot(aimPoint);
        }

        public override void OnGhostPreDestroy()
        {
            m_Linked = false;
            m_Input.enabled = false;
            m_View.gameObject.SetActive(false);
        }
    }
}
