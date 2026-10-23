using Unity.Entities;
using UnityEngine;

namespace Unity.MP_FPS
{
    public sealed partial class ProceduralAlienEnemy
    {
        private bool m_MucusInFlight, m_MucusLowArc;
        private uint m_ImpactSequence;
        private Vector3 m_ImpactPoint, m_ImpactNormal;
        private AlienImpactKind m_ImpactKind;
        public int LastArcQueries { get; private set; }

        private bool PrepareMucusArc(Vector3 target)
        {
            LastArcQueries = 0;
            Vector3 origin = MucusSocket();
            m_MucusHits ??= new RaycastHit[64];
            for (int mode = 0; mode < 2; mode++)
            {
                if (!MucusArcClear(origin, target, mode == 1)) continue;
                m_MucusLowArc = mode == 1; return true;
            }
            // A failed launch is reconsidered at most twice a second, not every server tick.
            m_NextAttack = m_Clock + .5f;
            return false;
        }
        private Vector3 MucusSocket() => m_Body.AttackSocket + transform.forward * .4f + Vector3.up * .12f;
        private bool MucusArcClear(Vector3 origin, Vector3 target, bool low)
        {
            int mask = LayerMask.GetMask("Default", "Ground");
            LastArcQueries++;
            if (UnityEngine.Physics.CheckSphere(origin, .18f, mask, QueryTriggerInteraction.Ignore)) return false;
            float duration = AlienMucusTrajectory.FlightTime(origin, target, profile.MucusSpeed);
            Vector3 previous = origin;
            // 24 bounded swept segments; the server motor still resolves every flight step.
            for (int i = 1; i <= 24; i++)
            {
                Vector3 point = AlienMucusTrajectory.Point(origin, target, profile.MucusSpeed, duration * i / 24, low);
                Vector3 delta = point - previous; LastArcQueries++;
                if (delta.sqrMagnitude > .000001f && UnityEngine.Physics.SphereCast(previous, .22f, delta.normalized,
                    out _, delta.magnitude, mask, QueryTriggerInteraction.Ignore)) return false;
                previous = point;
            }
            return true;
        }
        private float m_MucusStarted;
        private Vector3 m_MucusOrigin,m_MucusTarget,m_MucusPrevious;
        private RaycastHit[] m_MucusHits;
        public int MucusHits { get; private set; }

        private void StartMucusProjectile()
        {
            m_MucusHits ??= new RaycastHit[64];
            m_MucusInFlight=true;m_MucusStarted=m_Clock;
            m_MucusOrigin=m_CombatOrigin;m_MucusTarget=m_CombatPoint;m_MucusPrevious=m_MucusOrigin;
        }
        private void TickMucusProjectile()
        {
            if (!m_MucusInFlight) return;
            float age=m_Clock-m_MucusStarted;
            float duration=AlienMucusTrajectory.FlightTime(m_MucusOrigin,m_MucusTarget,profile.MucusSpeed);
            Vector3 next=AlienMucusTrajectory.Point(m_MucusOrigin,m_MucusTarget,profile.MucusSpeed,Mathf.Min(age,duration+.12f),m_MucusLowArc);
            Vector3 delta=next-m_MucusPrevious;
            if (delta.sqrMagnitude>.000001f)
            {
                UnityEngine.Physics.SyncTransforms();
                int mask=LayerMask.GetMask("Default","Ground","ServerPlayer");
                int count=UnityEngine.Physics.SphereCastNonAlloc(m_MucusPrevious,.18f,delta.normalized,m_MucusHits,delta.magnitude,mask,QueryTriggerInteraction.Ignore);
                if(count==m_MucusHits.Length){m_MucusInFlight=false;return;}
                float nearest=float.MaxValue;GhostGameObject victim=null;RaycastHit contact=default;
                for(int i=0;i<count;i++)
                {
                    var root=m_MucusHits[i].collider.GetComponentInParent<GhostGameObject>();
                    if(root==GhostGameObject || m_MucusHits[i].distance>=nearest)continue;
                    nearest=m_MucusHits[i].distance;victim=root;contact=m_MucusHits[i];
                }
                if(nearest<float.MaxValue)
                {
                    m_MucusInFlight=false;
                    m_ImpactSequence++; m_ImpactPoint=contact.point; m_ImpactNormal=contact.normal;
                    m_ImpactKind=AlienImpactKind.World;
                    var manager=GhostGameObject.EntityManager;
                    if(victim && victim.World==World && IsLivingHumanoid(manager,victim.LinkedEntity))
                    {
                        Entity entity=victim.LinkedEntity;
                        bool face=Vector3.Distance(contact.point,TargetHead(entity).position)<.7f;
                        m_ImpactKind=face?AlienImpactKind.Face:AlienImpactKind.Body;
                        ApplyAlienDamage(entity,profile.Damage*.4f,face?BodyPart.Head:BodyPart.Chest);
                        var health=manager.GetComponentData<PredictedPlayerGhost>(entity);
                        if(face && health.CurrentHealth>0)health.MucusBlindSeconds=Mathf.Max(health.MucusBlindSeconds,profile.MucusBlindSeconds);
                        manager.SetComponentData(entity,health);MucusHits++;
                    }
                    return;
                }
            }
            m_MucusPrevious=next;
            if(age>=duration+.12f)m_MucusInFlight=false;
        }
    }
}
