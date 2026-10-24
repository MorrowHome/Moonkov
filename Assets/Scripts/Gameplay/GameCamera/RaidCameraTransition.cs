using System;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Unity.MP_FPS
{
    /// <summary>One temporary rendering camera; the player camera continues updating its destination.</summary>
    internal sealed class RaidCameraTransition : IDisposable
    {
        internal const float DeploymentSeconds = 4.5f;
        internal const float DeathSeconds = 4.6f;
        private readonly Camera m_Camera;
        private Camera m_Source;
        private bool m_SourceEnabled;
        private float m_Time;
        private Vector3 m_Overview;
        private Vector3 m_Approach;
        private Quaternion m_OverviewRotation;
        private Quaternion m_ApproachRotation;
        private float m_FarClip;
        private Vector3 m_LastEye, m_LastFeet, m_DeathStart, m_DeathEnd;
        private Quaternion m_LastLook, m_DeathLook;
        private PhysicsScene m_PhysicsScene;
        private bool m_HasPose;
        public bool Deploying { get; private set; }
        public Camera Camera => m_Camera;
        public bool DeathActive { get; private set; }
        public bool DeathComplete => DeathActive && m_Time >= DeathSeconds;
        public bool HidesHUD => Deploying || DeathActive && !DeathComplete;
        // The fall and a brief grounded hold remain fully visible before eyelids close.
        public float BlackAlpha => DeathActive ? Mathf.SmoothStep(0, 1, Mathf.Clamp01((m_Time - 2f) / 2.2f)) : 0;

        public RaidCameraTransition(Transform owner)
        {
            var go = new GameObject("Raid transition camera");
            go.transform.SetParent(owner, false);
            m_Camera = go.AddComponent<Camera>();
            m_Camera.enabled = false;
        }

        public void Capture(Camera source)
        {
            if (!source || source == m_Camera) return;
            if (m_Source != source)
            {
                RestoreSource();
                m_Source = source;
                m_SourceEnabled = source.enabled;
            }
            m_Camera.CopyFrom(source);
            m_Camera.enabled = false;
            m_Camera.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
            RememberEye();
            m_FarClip = source.farClipPlane;
            var from = source.GetUniversalAdditionalCameraData();
            var to = m_Camera.GetUniversalAdditionalCameraData();
            to.renderType = CameraRenderType.Base;
            to.renderPostProcessing = from.renderPostProcessing;
            to.renderShadows = from.renderShadows;
            to.antialiasing = from.antialiasing;
            to.antialiasingQuality = from.antialiasingQuality;
            to.volumeLayerMask = from.volumeLayerMask;
            to.volumeTrigger = m_Camera.transform;
        }

        public void BeginDeployment(Bounds bounds)
        {
            if (!m_Source) return;
            DeathActive = false;
            m_Time = 0;
            Deploying = true;
            var eye = m_Source.transform.position;
            bounds.Encapsulate(eye);
            float span = Mathf.Max(bounds.size.z, bounds.size.x / Mathf.Max(.5f, m_Source.aspect));
            float height = Mathf.Max(160, span * .56f / Mathf.Tan(30 * Mathf.Deg2Rad));
            m_Overview = new Vector3(bounds.center.x, bounds.max.y + height, bounds.center.z);
            m_OverviewRotation = Quaternion.Euler(90, 0, 0);
            var forward = Vector3.ProjectOnPlane(m_Source.transform.forward, Vector3.up).normalized;
            m_Approach = eye + Vector3.up * 80 - forward * 35;
            m_ApproachRotation = Quaternion.LookRotation(eye - m_Approach);
        }

        public void TickDeployment(float deltaTime)
        {
            if (!Deploying) return;
            if (!m_Source) { Stop(); return; }
            RememberEye();
            m_Time += Mathf.Max(0, deltaTime);
            var eye = m_Source.transform.position;
            var rotation = m_Source.transform.rotation;
            float descend = Mathf.SmoothStep(0, 1, Mathf.Clamp01((m_Time - .65f) / 2.35f));
            float approach = Mathf.SmoothStep(0, 1, Mathf.Clamp01((m_Time - 3f) / 1.5f));
            var position = Vector3.Lerp(m_Overview, m_Approach, descend);
            var look = Quaternion.Slerp(m_OverviewRotation, m_ApproachRotation, descend);
            m_Camera.transform.SetPositionAndRotation(Vector3.Lerp(position, eye, approach),
                Quaternion.Slerp(look, rotation, approach));
            m_Camera.fieldOfView = Mathf.Lerp(60, m_Source.fieldOfView, approach);
            m_Camera.farClipPlane = Mathf.Max(m_FarClip, Vector3.Distance(m_Overview, eye) * 2);
            m_Source.enabled = false;
            m_Camera.tag = "MainCamera";
            m_Camera.enabled = true;
            if (m_Time >= DeploymentSeconds) Stop();
        }

        private void RememberEye()
        {
            m_LastEye = m_Source.transform.position;
            m_LastLook = m_Source.transform.rotation;
            m_HasPose = true;
        }

        public void CaptureBody(Vector3 feet, PhysicsScene physicsScene)
        {
            m_LastFeet = feet;
            m_PhysicsScene = physicsScene;
        }

        public void BeginDeath()
        {
            Deploying = false;
            DeathActive = true;
            m_Time = 0;
            if (!m_HasPose) return; // Missing spawn/camera still receives a bounded fade and result.
            // A third-person orbit must not make death tumble from several metres behind the body.
            m_DeathStart = Vector3.Distance(m_LastEye, m_LastFeet) > 2.5f ? m_LastFeet + Vector3.up * 1.45f : m_LastEye;
            var yaw = Quaternion.Euler(0, m_LastLook.eulerAngles.y, 0);
            var floor = m_LastFeet + yaw * new Vector3(.28f, .2f, .15f);
            int mask = LayerMask.GetMask("Default", "Ground");
            if (m_PhysicsScene.IsValid() && m_PhysicsScene.Raycast(floor + Vector3.up, Vector3.down,
                out var ground, 3, mask, QueryTriggerInteraction.Ignore))
                floor = ground.point + ground.normal * .2f;
            var delta = floor - m_DeathStart;
            if (m_PhysicsScene.IsValid() && delta.sqrMagnitude > .001f &&
                m_PhysicsScene.SphereCast(m_DeathStart, .12f, delta.normalized, out var obstruction,
                    delta.magnitude, mask, QueryTriggerInteraction.Ignore))
                floor = m_DeathStart + delta.normalized * Mathf.Max(0, obstruction.distance - .03f);
            m_DeathEnd = floor;
            m_DeathLook = Quaternion.Euler(12, m_LastLook.eulerAngles.y, 76);
            m_Camera.nearClipPlane = .03f;
            m_Camera.farClipPlane = m_FarClip;
            m_Camera.fieldOfView = Mathf.Max(60, m_Camera.fieldOfView);
            m_Camera.transform.SetPositionAndRotation(m_DeathStart, m_LastLook);
        }

        public void TickDeath(float deltaTime)
        {
            if (!DeathActive) return;
            m_Time = Mathf.Min(DeathSeconds, m_Time + Mathf.Max(0, deltaTime));
            if (!m_HasPose) return;
            float t = Mathf.Clamp01(m_Time / 1.1f);
            float fall = t * t * (2 - t);
            m_Camera.transform.SetPositionAndRotation(Vector3.Lerp(m_DeathStart, m_DeathEnd, fall),
                Quaternion.Slerp(m_LastLook, m_DeathLook, Mathf.SmoothStep(0, 1, t)));
            if (m_Source) m_Source.enabled = false;
            m_Camera.tag = "MainCamera";
            m_Camera.enabled = true;
        }

        public void Stop()
        {
            Deploying = false;
            DeathActive = false;
            if (m_Camera)
            {
                m_Camera.enabled = false;
                m_Camera.tag = "Untagged";
            }
            RestoreSource();
        }

        private void RestoreSource()
        {
            if (m_Source) m_Source.enabled = m_SourceEnabled;
        }

        public void Dispose()
        {
            Stop();
            if (!m_Camera) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(m_Camera.gameObject);
            else UnityEngine.Object.DestroyImmediate(m_Camera.gameObject);
        }
    }
}
