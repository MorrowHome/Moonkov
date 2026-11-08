using System;
using System.Collections.Generic;
using Unity.Entities;
using Unity.MP_FPS.Inventory;
using UnityEngine;

namespace Unity.MP_FPS
{
    // One authoritative solo deployment. The local HUD can read a copy and submit a hold
    // intent. Only the server tick can consume a device, write data or advance a counter.
    public sealed class ResearchQuestRuntime
    {
        public struct Presentation
        {
            public int Active, Seconds, HaloKills, SentryKills, Samples, Recovered, Issued;
            public bool CanOperate, TabletAvailable;
            public ResearchQuest Target;
            public float InteractionProgress;
            public string Notice;
        }

        private static ResearchQuestRuntime s_Run;
        private readonly World m_World;
        private readonly Entity m_Connection, m_Player;
        private readonly int m_RaidId;
        private readonly RaidInventoryState m_Inventory;
        private readonly ResearchQuestProgress m_Saved;
        private readonly ResearchQuestResult m_Result;
        private readonly HashSet<string> m_Samples = new HashSet<string>();
        private readonly string m_TabletId, m_FlashId, m_HardDriveId, m_PackageId;
        private readonly RaidMissionWorld.Actor m_Action = new RaidMissionWorld.Actor();
        private GameObject m_TabletModel;
        private float m_AliveSeconds;
        private bool m_TabletPicked, m_TabletPlaced, m_RequireRelease;
        private ResearchQuest m_Requested;
        private double m_HoldLease, m_HoldStart, m_NoticeUntil;
        private string m_Notice;

        private ResearchQuestRuntime(World world, Entity connection, Entity player, int raidId,
            RaidInventoryState inventory, ResearchQuestProgress saved)
        {
            m_World = world; m_Connection = connection; m_Player = player; m_RaidId = raidId;
            m_Inventory = inventory; m_Saved = saved;
            m_Result = new ResearchQuestResult { Active = saved.Active };
            m_TabletId = Guid.NewGuid().ToString("D");
            m_FlashId = ResearchQuestRules.IssuedId(inventory.Graph, ResearchQuestRules.Flash);
            m_HardDriveId = ResearchQuestRules.IssuedId(inventory.Graph, ResearchQuestRules.Drive);
            m_PackageId = ResearchQuestRules.IssuedId(inventory.Graph, ResearchQuestRules.Package);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() => s_Run = null;

        private bool Matches(int raidId) => m_World.IsCreated && m_RaidId == raidId &&
            m_World.EntityManager.Exists(m_Connection) && m_World.EntityManager.Exists(m_Player);
        private bool Active(ResearchQuest quest) => ResearchQuestRules.Has(m_Result.Active, quest);
        private InventoryGraph Inventory => m_Inventory.Graph;

        public static void Tick(World world, Entity connection, Entity player, RaidSession session, RaidInventoryState inventory,
            ResearchQuestProgress progress, in PredictedPlayerGhost health, Vector3 position, double now, float dt)
        {
            if (progress == null || progress.Active == 0) return;
            if (s_Run == null || s_Run.m_World != world || s_Run.m_Connection != connection || !s_Run.Matches(session.RaidId))
            {
                s_Run?.RemoveTablet();
                s_Run = new ResearchQuestRuntime(world, connection, player, session.RaidId, inventory, progress);
            }
            s_Run.Advance(health, position, now, dt);
        }

        public static void Clear(World world)
        {
            if (s_Run?.m_World != world) return;
            s_Run.RemoveTablet();
            s_Run = null;
        }

        public static ResearchQuestResult Finish(World world, Entity connection, int raidId, bool extracted)
        {
            var run = s_Run;
            if (run == null || run.m_World != world || run.m_Connection != connection || run.m_RaidId != raidId) return null;
            var result = run.m_Result;
            if (extracted)
            {
                result.TabletId = run.m_TabletPicked ? run.m_TabletId : null;
                result.PackageId = run.m_PackageId;
                var samples = new List<string>(ResearchQuestRules.SampleGoal);
                foreach (string id in run.m_Samples)
                {
                    if (ResearchQuestRules.Holds(run.Inventory, id, ResearchQuestRules.Sample, found: true)) samples.Add(id);
                    if (samples.Count == ResearchQuestRules.SampleGoal) break;
                }
                result.SampleIds = samples.ToArray();
            }
            Clear(world);
            return result;
        }

        public static void AddSample(World world, InventoryGraph harvest)
        {
            var run = s_Run;
            if (run == null || run.m_World != world || !run.Matches(run.m_RaidId) || !run.Active(ResearchQuest.Biology)) return;
            string id = Guid.NewGuid().ToString("D");
            if (harvest.AddSupply(ResearchQuestRules.Sample, 1, LootInventoryExchange.Root, foundInRaid: true, id: id) == InventoryError.None)
                run.m_Samples.Add(id);
        }

        public static ClashDamageSource TagShot(World world, Entity shooter, bool halo, bool sentry, ClashDamageSource source)
        {
            var run = s_Run;
            if (halo && run != null && run.m_World == world && run.m_Player == shooter &&
                (run.Active(ResearchQuest.HaloTrial) || run.Active(ResearchQuest.Sentry)))
            {
                source.ResearchShooter = shooter;
                source.ResearchSentry = sentry;
            }
            return source;
        }

        public static void RecordKill(World world, Entity victim, ClashDamageSource source)
        {
            var run = s_Run;
            if (run == null || run.m_World != world || source.ResearchShooter == Entity.Null ||
                run.m_Player != source.ResearchShooter || !run.Matches(run.m_RaidId) ||
                !world.EntityManager.HasComponent<ProceduralAlienEnemy.State>(victim)) return;
            if (run.Active(ResearchQuest.HaloTrial) && run.m_Result.HaloKills < ResearchQuestRules.HaloKillGoal) run.m_Result.HaloKills++;
            if (source.ResearchSentry && run.Active(ResearchQuest.Sentry) && run.m_Result.SentryKills < ResearchQuestRules.SentryKillGoal) run.m_Result.SentryKills++;
        }

        public static RaidMissionAnchor Anchor(ResearchQuest quest)
        {
            var site = RaidMissionSite.Active;
            if (!site) return null;
            return quest switch
            {
                ResearchQuest.Tablet => site.Anchor("power_a"),
                ResearchQuest.FlashDrive => site.Anchor("power_b"),
                ResearchQuest.HardDrive => site.Anchor("archive"),
                _ => null
            };
        }

        public static Vector3 InteractionPoint(ResearchQuest quest, RaidMissionAnchor anchor)
        {
            if (quest != ResearchQuest.Tablet) return anchor.Position;
            var mount = TabletMount(anchor);
            return mount + Vector3.up * .018f;
        }

        private static Vector3 TabletMount(RaidMissionAnchor anchor)
        {
            var surface = RaidInteractionAim.MissionSurface(anchor.Id);
            var casing = surface ? surface.Find("Physical equipment casing") : null;
            if (casing && casing.TryGetComponent<Renderer>(out var renderer))
            {
                var bounds = renderer.bounds;
                return new Vector3(bounds.center.x, bounds.max.y + .001f, bounds.center.z);
            }
            return anchor.Position + Vector3.up * .19f;
        }

        public static bool TryRead(int raidId, out Presentation view)
        {
            view = default;
            var run = s_Run;
            if (run == null || !run.Matches(raidId)) return false;
            view.Active = run.m_Result.Active;
            view.Seconds = run.m_Result.ExposureSeconds;
            view.HaloKills = Math.Min(ResearchQuestRules.HaloKillGoal, run.m_Saved.HaloKills + run.m_Result.HaloKills);
            view.SentryKills = Math.Min(ResearchQuestRules.SentryKillGoal, run.m_Saved.SentryKills + run.m_Result.SentryKills);
            view.TabletAvailable = run.Active(ResearchQuest.Tablet) && !run.m_TabletPicked;
            if (ResearchQuestRules.Holds(run.Inventory, run.m_FlashId, ResearchQuestRules.Flash)) view.Issued |= ResearchQuestRules.Bit(ResearchQuest.FlashDrive);
            if (ResearchQuestRules.Holds(run.Inventory, run.m_HardDriveId, ResearchQuestRules.Drive)) view.Issued |= ResearchQuestRules.Bit(ResearchQuest.HardDrive);
            if (ResearchQuestRules.Holds(run.Inventory, run.m_PackageId, ResearchQuestRules.Package)) view.Issued |= ResearchQuestRules.Bit(ResearchQuest.HaloStorage);
            if (ResearchQuestRules.Holds(run.Inventory, run.m_TabletId, ResearchQuestRules.Tablet)) view.Recovered |= ResearchQuestRules.Bit(ResearchQuest.Tablet);
            if (ResearchQuestRules.Holds(run.Inventory, run.m_Result.FlashId, ResearchQuestRules.Flash)) view.Recovered |= ResearchQuestRules.Bit(ResearchQuest.FlashDrive);
            if (ResearchQuestRules.Holds(run.Inventory, run.m_Result.HardDriveId, ResearchQuestRules.Drive)) view.Recovered |= ResearchQuestRules.Bit(ResearchQuest.HardDrive);
            if (ResearchQuestRules.StoredInHalo(run.Inventory, run.m_PackageId)) view.Recovered |= ResearchQuestRules.Bit(ResearchQuest.HaloStorage);
            if (run.Active(ResearchQuest.Biology))
                foreach (var item in run.Inventory.Items)
                    if (item.Code == ResearchQuestRules.Sample && run.m_Samples.Contains(item.Id) && run.Inventory.Carried(item)) view.Samples++;
            double now = run.m_World.Time.ElapsedTime;
            view.CanOperate = run.m_Action.Node >= 0;
            view.Target = (ResearchQuest)run.m_Action.Node;
            if (view.CanOperate) view.InteractionProgress = Mathf.Clamp01((float)(now - run.m_HoldStart) / Duration(view.Target));
            if (now < run.m_NoticeUntil) view.Notice = run.m_Notice;
            return true;
        }

        public static bool NeedsDevice(int raidId, ResearchQuest quest)
        {
            var run = s_Run;
            if (run == null || !run.Matches(raidId) || !run.Active(quest)) return false;
            return quest switch
            {
                ResearchQuest.Tablet => !run.m_TabletPicked,
                ResearchQuest.FlashDrive => run.m_Result.FlashId == null && ResearchQuestRules.Holds(run.Inventory, run.m_FlashId, ResearchQuestRules.Flash),
                ResearchQuest.HardDrive => run.m_Result.HardDriveId == null && ResearchQuestRules.Holds(run.Inventory, run.m_HardDriveId, ResearchQuestRules.Drive),
                _ => false
            };
        }

        public static void Hold(int raidId, ResearchQuest quest, bool pressed)
        {
            var run = s_Run;
            if (run == null || !run.Matches(raidId)) return;
            if (!pressed)
            {
                run.m_HoldLease = 0;
                run.m_RequireRelease = false;
                run.m_Action.Node = -1;
                return;
            }
            if (run.m_RequireRelease || !NeedsDevice(raidId, quest)) return;
            run.m_Requested = quest;
            run.m_HoldLease = run.m_World.Time.ElapsedTime + .24;
        }

        private static float Duration(ResearchQuest quest) => quest == ResearchQuest.Tablet ? .4f :
            quest == ResearchQuest.FlashDrive ? 6f : 16f;

        private void Advance(in PredictedPlayerGhost health, Vector3 position, double now, float dt)
        {
            if (Active(ResearchQuest.Exposure))
            {
                m_AliveSeconds = Mathf.Min(ResearchQuestRules.ExposureSeconds, m_AliveSeconds + dt);
                m_Result.ExposureSeconds = Mathf.FloorToInt(m_AliveSeconds);
            }
            if (Active(ResearchQuest.Tablet) && !m_TabletPlaced) PlaceTablet();
            if (m_HoldLease <= now || m_RequireRelease) { m_Action.Node = -1; return; }
            var anchor = Anchor(m_Requested);
            var target = anchor == null ? default : InteractionPoint(m_Requested, anchor);
            var eye = position + Vector3.up * 1.4f;
            var forward = Quaternion.Euler(health.ControllerState.PitchDegrees, health.ControllerState.YawDegrees, 0) * Vector3.forward;
            bool accessible = anchor != null && NeedsDevice(m_RaidId, m_Requested) && !m_Inventory.ActionPending &&
                !HaloTransferRules.Occupied(health) && !RaidHealth.HandsBusy(health) &&
                (position - target).sqrMagnitude <= 9 && Vector3.Dot(forward, (target - eye).normalized) >= .65f &&
                RaidInteractionAim.Visible(eye, target, RaidInteractionAim.MissionSurface(anchor.Id));
            if (!accessible || m_Action.Node >= 0 && (m_Action.Node != (int)m_Requested ||
                ((Vector3)m_Action.Origin - position).sqrMagnitude > .36f || m_Action.Interrupted(health, position)))
            {
                if (m_Action.Node >= 0) Notice("interrupted", now);
                m_Action.Node = -1; m_RequireRelease = true;
                return;
            }
            if (m_Action.Node < 0)
            {
                m_Action.Node = (int)m_Requested;
                m_Action.Capture(health, position);
                m_HoldStart = now;
            }
            if (now - m_HoldStart < Duration(m_Requested)) return;
            m_Action.Node = -1; m_RequireRelease = true;
            switch (m_Requested)
            {
                case ResearchQuest.Tablet:
                    if (Inventory.AddSupply(ResearchQuestRules.Tablet, 1, foundInRaid: true, id: m_TabletId) != InventoryError.None)
                    { Notice("space", now); return; }
                    m_TabletPicked = true;
                    m_Inventory.LastSentVersion = -1;
                    RemoveTablet();
                    break;
                case ResearchQuest.FlashDrive: m_Result.FlashId = m_FlashId; break;
                case ResearchQuest.HardDrive: m_Result.HardDriveId = m_HardDriveId; break;
            }
            Notice("extract", now);
        }

        private void Notice(string notice, double now) { m_Notice = notice; m_NoticeUntil = now + 3; }

        private void PlaceTablet()
        {
            var anchor = Anchor(ResearchQuest.Tablet);
            if (anchor == null) return;
            var surface = RaidInteractionAim.MissionSurface(anchor.Id);
            if (!surface) return;
            m_TabletPlaced = true;
            m_TabletModel = PhysicalItemVisuals.Create(ResearchQuestRules.Tablet, surface);
            if (!m_TabletModel) return;
            m_TabletModel.transform.SetPositionAndRotation(TabletMount(anchor),
                Quaternion.Euler(0, anchor.Yaw + 90, 0) * m_TabletModel.transform.localRotation);
        }

        private void RemoveTablet()
        {
            if (m_TabletModel) UnityEngine.Object.Destroy(m_TabletModel);
            m_TabletModel = null;
        }
    }
}
