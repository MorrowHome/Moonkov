using System;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;

namespace Unity.MP_FPS
{
    public sealed partial class ResearchQuestRuntime
    {
        private readonly HashSet<ResearchDevice> m_AnnouncedDevices = new();
        private bool m_LowerOpened, m_ControlOpened;
        private RaidMissionAnchor m_EmitterAnchor;
        private double m_NextPulse;

        private void UpdateHazards(Vector3 player, double now, uint tick)
        {
            bool lower = Active(ResearchQuest.NoWayBack);
            if (!lower && (!Active(ResearchQuest.EndOfTransmission) || m_Result.SystemsDisabled)) return;
            if (m_EmitterAnchor == null)
            {
                var site = RaidMissionSite.Active;
                if (!site) return;
                m_EmitterAnchor = site.ResearchAnchor(lower ? "lower_security" : "unstable_feed");
                m_NextPulse = now + 1.05;
                if (m_EmitterAnchor == null) return;
            }

            var point = m_EmitterAnchor.Position;
            var eye = player + Vector3.up;
            bool near = (eye - point).sqrMagnitude < (lower ? 2.25f : 3.24f) && RaidInteractionAim.Visible(eye, point);
            if (!near) { m_NextPulse = now + 1.05; return; }
            if (now < m_NextPulse) return;
            m_NextPulse = now + 2.6;
            BountyClashDamage.Apply(m_World, m_Player, default(ClashDamageSource), BodyPart.Chest,
                lower ? 7 : 12, tick, RaidDamageKind.Fire, point);
            Notice("discharge", now);
        }

        private void CompleteFieldDevice(ResearchDevice device, double now)
        {
            switch (device)
            {
                case ResearchDevice.LowerSeal:
                    m_LowerOpened = true;
                    AlertAliens(device);
                    Notice("lower-open", now);
                    break;
                case ResearchDevice.NadirTerminal:
                {
                    string id = Guid.NewGuid().ToString("D");
                    if (!Recover(ResearchQuestRules.NadirRecords, id, now)) return;
                    m_Result.NadirRecordsId = id;
                    Notice(m_Result.CredentialsId == null ? "credentials" : "extract", now);
                    break;
                }
                case ResearchDevice.CredentialsLocker:
                {
                    string id = Guid.NewGuid().ToString("D");
                    if (!Recover(ResearchQuestRules.Credentials, id, now)) return;
                    m_Result.CredentialsId = id;
                    Notice(m_Result.NadirRecordsId == null ? "nadir-records" : "extract", now);
                    break;
                }
                case ResearchDevice.ControlAccess:
                    m_ControlOpened = true;
                    m_Result.CredentialsId = m_Saved.CredentialsId;
                    AlertAliens(device);
                    Notice("control-open", now);
                    break;
                case ResearchDevice.OriginalArchive:
                {
                    string id = Guid.NewGuid().ToString("D");
                    if (!Recover(ResearchQuestRules.OriginalArchive, id, now)) return;
                    m_Result.OriginalArchiveId = id;
                    Notice(m_Result.SystemsDisabled ? "extract" : "shutdown", now);
                    break;
                }
                case ResearchDevice.Shutdown:
                    m_Result.SystemsDisabled = true;
                    Notice(m_Result.OriginalArchiveId == null ? "archive" : "extract", now);
                    break;
            }
        }

        private void AlertAliens(ResearchDevice device)
        {
            var anchor = Anchor(device);
            if (anchor == null || !m_AnnouncedDevices.Add(device)) return;
            const float range = 22;
            var manager = m_World.EntityManager;
            using var query = manager.CreateEntityQuery(typeof(ProceduralAlienEnemy.State), typeof(GhostGameObjectLink), typeof(LocalTransform));
            using var nearby = query.ToEntityArray(Unity.Collections.Allocator.Temp);
            foreach (var entity in nearby)
            {
                var position = (Vector3)manager.GetComponentData<LocalTransform>(entity).Position;
                if ((position - anchor.Position).sqrMagnitude > range * range) continue;
                var link = manager.GetComponentObject<GhostGameObjectLink>(entity);
                var alien = link.LinkedInstance ? link.LinkedInstance.GetComponent<ProceduralAlienEnemy>() : null;
                if (alien && MissionAlarm.Audible(anchor.Position, alien.AimPoint, range))
                    alien.HearMissionAlarm($"moonrat-{m_RaidId}-{m_Player.Index}-{device}", anchor.Approach);
            }
        }

    }
}
