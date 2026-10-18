using Unity.Entities;
using UnityEngine;

namespace Unity.MP_FPS
{
    public sealed partial class ProceduralAlienEnemy
    {
        private bool m_MucusInFlight;
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
            Vector3 next=AlienMucusTrajectory.Point(m_MucusOrigin,m_MucusTarget,profile.MucusSpeed,Mathf.Min(age,duration+.12f));
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
                    var manager=GhostGameObject.EntityManager;
                    if(victim && victim.World==World && IsLivingHumanoid(manager,victim.LinkedEntity))
                    {
                        Entity entity=victim.LinkedEntity;
                        bool face=Vector3.Distance(contact.point,TargetHead(entity).position)<.7f;
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
