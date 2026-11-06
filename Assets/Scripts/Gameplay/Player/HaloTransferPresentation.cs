using Unity.MP_FPS.DollSinger;
using UnityEngine;
using UnityEngine.Rendering;

namespace Unity.MP_FPS
{
    // Move the equipped art itself. No substitute ring, extra lights or cloned weapon materials.
    [DefaultExecutionOrder(190)]
    public sealed class HaloTransferPresentation : MonoBehaviour
    {
        private DollSingerHaloAim m_Halo, m_Recipient;
        private HaloTransferPhase m_Phase;
        private float m_Remaining, m_SampledAt;
        private Vector3 m_Point, m_Departure, m_ReturnFrom, m_Position;
        private Color m_Color;
        private PredictedPlayerGhost m_State;
        private HaloDefenseSphere m_Shield;
        private uint m_HitSequence;
        private float m_HitAt = -100;
        private readonly LineRenderer[] m_Lines = new LineRenderer[5];
        private readonly Vector3[] m_Path = new Vector3[25];

        public void Configure(DollSingerHaloAim halo) { ResetPresentation(); m_Halo = halo; }

        public void Present(in PredictedPlayerGhost state, DollSingerHaloAim recipient)
        {
            m_State = state;
            if (m_HitSequence != state.ShieldHitSequence) { m_HitSequence = state.ShieldHitSequence; m_HitAt = Time.time; }
            var phase = EnergyShieldRules.Alive(state) && state.HaloEquipped ? state.HaloTransferPhase : HaloTransferPhase.Idle;
            if (phase != m_Phase)
            {
                if (m_Phase == HaloTransferPhase.Idle && m_Halo && m_Halo.haloVisual)
                    m_Position = m_Departure = m_Halo.haloVisual.position;
                if (phase == HaloTransferPhase.Returning) m_ReturnFrom = m_Position;
            }
            m_Phase = phase;
            m_Remaining = state.HaloTransferRemaining;
            m_SampledAt = Time.time;
            m_Point = (Vector3)state.HaloTransferPoint + (HaloSupportRules.Anchored(state) ? Vector3.zero : Vector3.up * .75f);
            m_Recipient = recipient;
            if (m_Halo) m_Color = m_Halo.ColorForWeapon((DollSingerHaloAim.HaloWeapon)(state.EquippedWeaponID - 1));
            if (phase == HaloTransferPhase.Idle) SetLines(false);
        }

        private void LateUpdate() => SimulatePresentation(Time.deltaTime);

        private void SimulatePresentation(float dt)
        {
            PresentShield(dt);
            if (m_Phase == HaloTransferPhase.Idle || !m_Halo || !m_Halo.haloVisual || !m_Halo.haloVisual.gameObject.activeInHierarchy)
            { SetLines(false); return; }
            var home = m_Halo.RestingAnchor;
            var recipient = m_Recipient ? m_Recipient.RestingAnchor : m_Point;
            Vector3 towardHome = Vector3.ProjectOnPlane(home - recipient, Vector3.up).normalized;
            Vector3 dock = HaloSupportRules.Anchored(m_State) ? m_Point : recipient + towardHome * .52f + Vector3.up * .08f;
            float remaining = Mathf.Max(0, m_Remaining - (Time.time - m_SampledAt));
            Vector3 desired;
            if (m_Phase == HaloTransferPhase.Returning)
            {
                float t = Mathf.SmoothStep(0, 1, 1 - remaining / HaloTransferRules.ReturnSeconds);
                desired = Vector3.Lerp(m_ReturnFrom, home, t) + Vector3.up * (Mathf.Sin(t * Mathf.PI) * .18f);
            }
            else
            {
                float t = m_Phase == HaloTransferPhase.Extending
                    ? Mathf.SmoothStep(0, 1, 1 - remaining / HaloTransferRules.ExtendSeconds) : 1;
                desired = Vector3.Lerp(m_Departure, dock, t) + Vector3.up * (Mathf.Sin(t * Mathf.PI) * .3f);
            }
            m_Position = Vector3.Lerp(m_Position, desired, 1 - Mathf.Exp(-dt * 45));
            m_Halo.haloVisual.position = m_Position;
            m_Halo.haloVisual.rotation = Quaternion.LookRotation(Vector3.up, towardHome.sqrMagnitude > .01f ? towardHome : transform.forward);
            if (HaloSupportRules.Anchored(m_State))
            {
                if (m_State.HaloDeploymentMode == HaloDeploymentMode.Sentry && ((Vector3)m_State.AimPoint - m_Position).sqrMagnitude > .01f)
                    m_Halo.haloVisual.rotation = Quaternion.LookRotation((Vector3)m_State.AimPoint - m_Position);
                if (m_State.HaloDeploymentMode == HaloDeploymentMode.Light && m_Halo.haloLight)
                { m_Halo.haloLight.enabled = true; m_Halo.haloLight.range = 8; m_Halo.haloLight.intensity = 4; }
                SetLines(false);
                return;
            }
            EnsureLines();
            if (!m_Lines[0]) return;
            float fade = m_Phase == HaloTransferPhase.Returning ? remaining / HaloTransferRules.ReturnSeconds : 1;
            SetLines(true);
            for (int i = 0; i < m_Path.Length; i++)
                m_Path[i] = Path(home, m_Position, i / (float)(m_Path.Length - 1));
            for (int i = 0; i < 2; i++)
            {
                m_Lines[i].SetPositions(m_Path);
                var color = m_Color; color.a = fade * (i == 0 ? .38f : .13f);
                m_Lines[i].startColor = m_Lines[i].endColor = color;
            }
            for (int i = 2; i < m_Lines.Length; i++)
            {
                float t = Mathf.Repeat(Time.time * .65f + (i - 2) / 3f, 1);
                m_Lines[i].enabled = m_Phase == HaloTransferPhase.Sending && m_State.HaloDeploymentMode == HaloDeploymentMode.Transfer;
                m_Lines[i].SetPosition(0, Path(home, m_Position, Mathf.Max(0, t - .045f)));
                m_Lines[i].SetPosition(1, Path(home, m_Position, t));
                m_Lines[i].startColor = new Color(m_Color.r, m_Color.g, m_Color.b, 0);
                m_Lines[i].endColor = m_Color;
            }
        }

        private void PresentShield(float dt)
        {
            bool area = m_State.HaloDeploymentMode == HaloDeploymentMode.Shield;
            bool active = m_Phase == HaloTransferPhase.Sending && (area || m_State.HaloDeploymentMode == HaloDeploymentMode.AllyGuard);
            if (active && !m_Shield)
            {
                var child = new GameObject("External halo defense") { hideFlags = HideFlags.DontSave, layer = gameObject.layer };
                child.transform.SetParent(transform, false);
                m_Shield = child.AddComponent<HaloDefenseSphere>();
            }
            if (!m_Shield) return;
            Vector3 head = m_Recipient ? m_Recipient.RestingAnchor : m_Point;
            m_Shield.Present(active, head, head - Vector3.up * (area ? .6f : .9f), area ? HaloSupportRules.ShieldRadius : 1.15f, m_Color,
                HaloEnergyRules.Energy(m_State) / HaloEnergyRules.Maximum(m_State), Time.time - m_HitAt,
                m_State.ShieldHitPoint, false, dt, m_State.EquippedWeaponID);
        }

        private static Vector3 Path(Vector3 start, Vector3 end, float t) =>
            Vector3.Lerp(start, end, t) + Vector3.up * (Mathf.Sin(t * Mathf.PI) * .13f);

        private void EnsureLines()
        {
            if (m_Lines[0] || !m_Halo.beamCore || !m_Halo.beamCore.sharedMaterial) return;
            for (int i = 0; i < m_Lines.Length; i++)
            {
                var child = new GameObject("Halo transfer " + i) { hideFlags = HideFlags.DontSave, layer = m_Halo.haloVisual.gameObject.layer };
                child.transform.SetParent(transform, false);
                var line = child.AddComponent<LineRenderer>();
                line.sharedMaterial = m_Halo.beamCore.sharedMaterial;
                line.useWorldSpace = true;
                line.positionCount = i < 2 ? m_Path.Length : 2;
                line.widthMultiplier = i == 0 ? .009f : i == 1 ? .038f : .018f;
                line.numCapVertices = 2;
                line.shadowCastingMode = ShadowCastingMode.Off;
                line.receiveShadows = false;
                line.lightProbeUsage = LightProbeUsage.Off;
                line.reflectionProbeUsage = ReflectionProbeUsage.Off;
                m_Lines[i] = line;
            }
        }

        private void SetLines(bool visible)
        { foreach (var line in m_Lines) if (line) line.enabled = visible; }

        public void ResetPresentation()
        {
            if (m_Phase != HaloTransferPhase.Idle && m_Halo && m_Halo.haloVisual)
            {
                m_Halo.haloVisual.position = m_Halo.RestingAnchor;
                m_Halo.haloVisual.rotation = Quaternion.LookRotation(m_Halo.transform.up, m_Halo.transform.forward);
            }
            m_Phase = HaloTransferPhase.Idle;
            m_Recipient = null;
            if (m_Shield) m_Shield.Clear();
            SetLines(false);
        }
        private void OnDisable() => ResetPresentation();
        private void OnDestroy()
        {
            if (m_Shield) { if (Application.isPlaying) Destroy(m_Shield.gameObject); else DestroyImmediate(m_Shield.gameObject); }
            foreach (var line in m_Lines)
                if (line) { if (Application.isPlaying) Destroy(line.gameObject); else DestroyImmediate(line.gameObject); }
        }
    }
}
