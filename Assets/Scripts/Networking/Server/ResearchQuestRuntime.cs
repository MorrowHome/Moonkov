using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.MP_FPS.Inventory;
using UnityEngine;

namespace Unity.MP_FPS
{
    // One server-owned run per deployed player. The player entity survives a
    // retained connection, so reconnects resume evidence without sharing it.
    public sealed partial class ResearchQuestRuntime
    {
        private static readonly Dictionary<World, Dictionary<Entity, ResearchQuestRuntime>> s_Runs = new();
        private static readonly ResearchDevice[] s_Devices = (ResearchDevice[])Enum.GetValues(typeof(ResearchDevice));
        private readonly World m_World;
        private readonly Entity m_Player;
        private Entity m_Connection;
        private readonly int m_RaidId;
        private readonly RaidInventoryState m_Inventory;
        private readonly ResearchQuestProgress m_Saved;
        private readonly ResearchQuestResult m_Result;
        private readonly HashSet<string> m_Samples = new HashSet<string>();
        private readonly string m_TabletId, m_FlashId, m_HardDriveId, m_PackageId, m_SampleKitId;
        private readonly RaidMissionWorld.Actor m_Action = new RaidMissionWorld.Actor();
        private readonly HashSet<int> m_SampledCorpses = new();
        private uint m_Sequence;
        private float m_AliveSeconds;
        private bool m_TabletPicked, m_RequireRelease;
        private ResearchDevice m_Requested;
        private bool m_PackageLoaded, m_PackageRemoved;
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
            m_SampleKitId = ResearchQuestRules.IssuedId(inventory.Graph, ResearchQuestRules.SampleKit);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() => s_Runs.Clear();

        private static ResearchQuestRuntime Find(World world, Entity player) =>
            s_Runs.TryGetValue(world, out var runs) && runs.TryGetValue(player, out var run) ? run : null;

        private bool Active(ResearchQuest quest) => ResearchQuestRules.Has(m_Result.Active, quest);
        private InventoryGraph Inventory => m_Inventory.Graph;

        public static void Tick(World world, Entity connection, Entity player, RaidSession session, RaidInventoryState inventory,
            ResearchQuestProgress progress, in PredictedPlayerGhost health, Vector3 position, double now, float dt, uint tick = 0)
        {
            if (progress == null || progress.Active == 0) return;
            if (!s_Runs.TryGetValue(world, out var runs)) s_Runs.Add(world, runs = new Dictionary<Entity, ResearchQuestRuntime>());
            if (!runs.TryGetValue(player, out var run) || run.m_RaidId != session.RaidId)
            {
                run = new ResearchQuestRuntime(world, connection, player, session.RaidId, inventory, progress);
                runs[player] = run;
            }
            if (run.m_Connection != connection)
            {
                run.m_Connection = connection;
                run.ReleaseHold();
            }
            run.Advance(health, position, now, dt, tick);
        }

        public static void Clear(World world) => s_Runs.Remove(world);

        public static ResearchQuestResult Finish(World world, Entity player, int raidId, bool extracted)
        {
            var run = Find(world, player);
            if (run == null || run.m_RaidId != raidId) return null;
            s_Runs[world].Remove(player);
            if (!extracted) return null;
            var result = run.m_Result;
            result.TabletId = run.m_TabletPicked ? run.m_TabletId : null;
            result.PackageId = run.m_PackageId;
            result.PackageSecured = run.ObserveCargo();
            var samples = new List<string>(ResearchQuestRules.SampleGoal);
            foreach (string id in run.m_Samples)
                if (ResearchQuestRules.Holds(run.Inventory, id, ResearchQuestRules.Sample, found: true)) samples.Add(id);
            result.SampleIds = samples.ToArray();
            return result;
        }

        public static void CollectSample(World world, Entity connection, Entity player, int raidId, int corpseId, double now)
        {
            var run = Find(world, player);
            if (run == null || run.m_Connection != connection || run.m_RaidId != raidId || !run.Active(ResearchQuest.Biology) ||
                run.m_Samples.Count >= ResearchQuestRules.SampleGoal || run.m_SampledCorpses.Contains(corpseId) ||
                !ResearchQuestRules.Holds(run.Inventory, run.m_SampleKitId, ResearchQuestRules.SampleKit)) return;
            string id = Guid.NewGuid().ToString("D");
            if (!run.Recover(ResearchQuestRules.Sample, id, now)) return;
            run.m_SampledCorpses.Add(corpseId);
            run.m_Samples.Add(id);
            run.Notice(run.m_Samples.Count == ResearchQuestRules.SampleGoal ? "extract" : "sample", now);
        }

        public static void InventoryChanged(World world, Entity connection, int raidId)
        {
            if (!s_Runs.TryGetValue(world, out var runs)) return;
            foreach (var run in runs.Values)
                if (run.m_Connection == connection && run.m_RaidId == raidId) run.ObserveCargo();
        }

        private bool ObserveCargo()
        {
            if (m_PackageId == null) return false;
            bool loaded = ResearchQuestRules.StoredInHalo(Inventory, m_PackageId);
            m_PackageRemoved |= m_PackageLoaded && !loaded;
            m_PackageLoaded |= loaded;
            return loaded && !m_PackageRemoved;
        }

        public static ClashDamageSource TagShot(World world, Entity shooter, bool halo, bool sentry, ClashDamageSource source)
        {
            var run = halo ? Find(world, shooter) : null;
            if (run != null &&
                (run.Active(ResearchQuest.HaloTrial) || run.Active(ResearchQuest.Sentry)))
            {
                source.ResearchShooter = shooter;
                source.ResearchSentry = sentry;
            }
            return source;
        }

        public static void RecordKill(World world, Entity victim, ClashDamageSource source)
        {
            var run = Find(world, source.ResearchShooter);
            if (run == null ||
                !world.EntityManager.HasComponent<ProceduralAlienEnemy.State>(victim)) return;
            if (run.Active(ResearchQuest.HaloTrial) && run.m_Result.HaloKills < ResearchQuestRules.HaloKillGoal) run.m_Result.HaloKills++;
            if (source.ResearchSentry && run.Active(ResearchQuest.Sentry) && run.m_Result.SentryKills < ResearchQuestRules.SentryKillGoal) run.m_Result.SentryKills++;
        }

        public static RaidMissionAnchor Anchor(ResearchDevice device)
        {
            var site = RaidMissionSite.Active;
            if (!site) return null;
            return device switch
            {
                ResearchDevice.Tablet => site.Anchor("power_a"),
                ResearchDevice.Administrator => site.Anchor("power_b"),
                ResearchDevice.Server => site.Anchor("archive"),
                ResearchDevice.LowerSeal => site.ResearchAnchor("lower_seal"),
                ResearchDevice.NadirTerminal => site.ResearchAnchor("nadir_records"),
                ResearchDevice.CredentialsLocker => site.ResearchAnchor("lucent_access"),
                ResearchDevice.ControlAccess => site.ResearchAnchor("control_access"),
                ResearchDevice.OriginalArchive => site.ResearchAnchor("original_archive"),
                ResearchDevice.Shutdown => site.ResearchAnchor("experiment_shutdown"),
                _ => null
            };
        }

        public static Vector3 InteractionPoint(ResearchDevice device, RaidMissionAnchor anchor)
        {
            if (device != ResearchDevice.Tablet) return anchor.Position;
            var mount = TabletMount(anchor);
            return mount + Vector3.up * .018f;
        }

        internal static Vector3 TabletMount(RaidMissionAnchor anchor)
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

        public static bool TryRead(World world, Entity player, int raidId, out ResearchQuestSnapshotRpc view)
        {
            view = default;
            var run = Find(world, player);
            if (run == null || run.m_RaidId != raidId) return false;
            view.RaidId = raidId;
            view.Sequence = ++run.m_Sequence;
            foreach (var device in s_Devices)
                if (run.Needs(device)) view.Devices |= (ushort)(1 << (int)device);
            view.Active = run.m_Result.Active;
            view.Seconds = run.m_Result.ExposureSeconds;
            view.HaloKills = Math.Min(ResearchQuestRules.HaloKillGoal, run.m_Saved.HaloKills + run.m_Result.HaloKills);
            view.SentryKills = Math.Min(ResearchQuestRules.SentryKillGoal, run.m_Saved.SentryKills + run.m_Result.SentryKills);
            view.TabletAvailable = run.Active(ResearchQuest.Tablet) && !run.m_TabletPicked;
            view.LowerOpened = run.m_LowerOpened;
            view.HeardNadir = run.m_Result.NadirRecordsId != null;
            view.RecordsHeld = ResearchQuestRules.Holds(run.Inventory, run.m_Result.NadirRecordsId, ResearchQuestRules.NadirRecords);
            view.CredentialsHeld = ResearchQuestRules.Holds(run.Inventory, run.m_Result.CredentialsId, ResearchQuestRules.Credentials);
            view.ControlOpened = run.m_ControlOpened;
            view.ArchiveHeld = ResearchQuestRules.Holds(run.Inventory, run.m_Result.OriginalArchiveId, ResearchQuestRules.OriginalArchive);
            view.SystemsDisabled = run.m_Result.SystemsDisabled;
            if (ResearchQuestRules.Holds(run.Inventory, run.m_FlashId, ResearchQuestRules.Flash)) view.Issued |= ResearchQuestRules.Bit(ResearchQuest.FlashDrive);
            if (ResearchQuestRules.Holds(run.Inventory, run.m_HardDriveId, ResearchQuestRules.Drive)) view.Issued |= ResearchQuestRules.Bit(ResearchQuest.HardDrive);
            if (ResearchQuestRules.Holds(run.Inventory, run.m_PackageId, ResearchQuestRules.Package)) view.Issued |= ResearchQuestRules.Bit(ResearchQuest.HaloStorage);
            if (ResearchQuestRules.Holds(run.Inventory, run.m_SampleKitId, ResearchQuestRules.SampleKit)) view.Issued |= ResearchQuestRules.Bit(ResearchQuest.Biology);
            if (ResearchQuestRules.Holds(run.Inventory, run.m_Saved.CredentialsId, ResearchQuestRules.Credentials))
                view.Issued |= ResearchQuestRules.Bit(ResearchQuest.EndOfTransmission);
            if (ResearchQuestRules.Holds(run.Inventory, run.m_TabletId, ResearchQuestRules.Tablet)) view.Recovered |= ResearchQuestRules.Bit(ResearchQuest.Tablet);
            if (ResearchQuestRules.Holds(run.Inventory, run.m_Result.FlashId, ResearchQuestRules.Flash)) view.Recovered |= ResearchQuestRules.Bit(ResearchQuest.FlashDrive);
            if (ResearchQuestRules.Holds(run.Inventory, run.m_Result.HardDriveId, ResearchQuestRules.Drive)) view.Recovered |= ResearchQuestRules.Bit(ResearchQuest.HardDrive);
            if (run.m_PackageLoaded && !run.m_PackageRemoved) view.Recovered |= ResearchQuestRules.Bit(ResearchQuest.HaloStorage);
            if (view.RecordsHeld && view.CredentialsHeld) view.Recovered |= ResearchQuestRules.Bit(ResearchQuest.NoWayBack);
            if (view.ArchiveHeld && view.SystemsDisabled) view.Recovered |= ResearchQuestRules.Bit(ResearchQuest.EndOfTransmission);
            view.PackageRemoved = run.m_PackageRemoved;
            if (run.Active(ResearchQuest.Biology))
                foreach (var item in run.Inventory.Items)
                    if (item.Code == ResearchQuestRules.Sample && run.m_Samples.Contains(item.Id) && run.Inventory.Carried(item)) view.Samples++;
            double now = run.m_World.Time.ElapsedTime;
            view.CanOperate = run.m_Action.Node >= 0;
            view.Target = (ResearchDevice)run.m_Action.Node;
            if (view.CanOperate)
                view.InteractionPercent = (byte)Mathf.FloorToInt(100 * Mathf.Clamp01((float)(now - run.m_HoldStart) / Duration(view.Target)));
            if (now < run.m_NoticeUntil) view.Notice = new FixedString32Bytes(run.m_Notice);
            return true;
        }

        private bool Needs(ResearchDevice device)
        {
            return device switch
            {
                ResearchDevice.Tablet => Active(ResearchQuest.Tablet) && !m_TabletPicked,
                ResearchDevice.Administrator => Active(ResearchQuest.FlashDrive) && m_Result.FlashId == null &&
                    ResearchQuestRules.Holds(Inventory, m_FlashId, ResearchQuestRules.Flash),
                ResearchDevice.Server => Active(ResearchQuest.HardDrive) && m_Result.HardDriveId == null &&
                    ResearchQuestRules.Holds(Inventory, m_HardDriveId, ResearchQuestRules.Drive),
                ResearchDevice.LowerSeal => Active(ResearchQuest.NoWayBack) && !m_LowerOpened,
                ResearchDevice.NadirTerminal => Active(ResearchQuest.NoWayBack) && m_LowerOpened && m_Result.NadirRecordsId == null,
                ResearchDevice.CredentialsLocker => Active(ResearchQuest.NoWayBack) && m_LowerOpened && m_Result.CredentialsId == null,
                ResearchDevice.ControlAccess => Active(ResearchQuest.EndOfTransmission) && !m_ControlOpened &&
                    ResearchQuestRules.Holds(Inventory, m_Saved.CredentialsId, ResearchQuestRules.Credentials),
                ResearchDevice.OriginalArchive => Active(ResearchQuest.EndOfTransmission) && m_ControlOpened && m_Result.OriginalArchiveId == null,
                ResearchDevice.Shutdown => Active(ResearchQuest.EndOfTransmission) && m_ControlOpened && !m_Result.SystemsDisabled,
                _ => false
            };
        }

        public static void Hold(World world, Entity connection, Entity player, int raidId, ResearchDevice device, bool pressed)
        {
            var run = Find(world, player);
            if (run == null || run.m_RaidId != raidId || run.m_Connection != connection) return;
            if (!pressed) { run.ReleaseHold(); return; }
            if (run.m_RequireRelease || !run.Needs(device)) return;
            run.m_Requested = device;
            run.m_HoldLease = world.Time.ElapsedTime + .45;
        }

        private void ReleaseHold()
        {
            m_HoldLease = 0;
            m_RequireRelease = false;
            m_Action.Node = -1;
        }

        private static float Duration(ResearchDevice device) => device switch
        {
            ResearchDevice.Tablet or ResearchDevice.CredentialsLocker => .4f,
            ResearchDevice.Administrator => 6f,
            ResearchDevice.Server or ResearchDevice.OriginalArchive => 16f,
            ResearchDevice.NadirTerminal => 4f,
            ResearchDevice.Shutdown => 5f,
            _ => 2f
        };

        private void Advance(in PredictedPlayerGhost health, Vector3 position, double now, float dt, uint tick)
        {
            if (Active(ResearchQuest.Exposure))
            {
                m_AliveSeconds = Mathf.Min(ResearchQuestRules.ExposureSeconds, m_AliveSeconds + dt);
                m_Result.ExposureSeconds = Mathf.FloorToInt(m_AliveSeconds);
            }
            ObserveCargo();
            UpdateHazards(position, now, tick);
            if (m_HoldLease <= now || m_RequireRelease) { m_Action.Node = -1; return; }
            var anchor = Anchor(m_Requested);
            var target = anchor == null ? default : InteractionPoint(m_Requested, anchor);
            var eye = position + Vector3.up * 1.4f;
            var forward = Quaternion.Euler(health.ControllerState.PitchDegrees, health.ControllerState.YawDegrees, 0) * Vector3.forward;
            bool accessible = anchor != null && Needs(m_Requested) && !m_Inventory.ActionPending &&
                !RaidHealth.HandsBusy(health, allowAnchoredHalo: true) &&
                (position - target).sqrMagnitude <= 9 && Vector3.Dot(forward, (target - eye).normalized) >= .65f &&
                RaidInteractionAim.Visible(eye, target, RaidInteractionAim.MissionSurface(anchor.Id));
            if (!accessible || m_Action.Node >= 0 && (m_Action.Node != (int)m_Requested ||
                ((Vector3)m_Action.Origin - position).sqrMagnitude > .36f || m_Action.Interrupted(health, position, allowAnchoredHalo: true)))
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
                if (m_Requested is ResearchDevice.Server or ResearchDevice.OriginalArchive) AlertAliens(m_Requested);
            }
            if (now - m_HoldStart < Duration(m_Requested)) return;
            m_Action.Node = -1; m_RequireRelease = true;
            switch (m_Requested)
            {
                case ResearchDevice.Tablet:
                    if (!Recover(ResearchQuestRules.Tablet, m_TabletId, now)) return;
                    m_TabletPicked = true;
                    break;
                case ResearchDevice.Administrator: m_Result.FlashId = m_FlashId; break;
                case ResearchDevice.Server: m_Result.HardDriveId = m_HardDriveId; break;
                default: CompleteFieldDevice(m_Requested, now); return;
            }
            Notice("extract", now);
        }

        private void Notice(string notice, double now) { m_Notice = notice; m_NoticeUntil = now + 3; }

        private bool Recover(string code, string id, double now)
        {
            if (Inventory.AddSupply(code, 1, foundInRaid: true, id: id) != InventoryError.None)
            { Notice("space", now); return false; }
            m_Inventory.LastSentVersion = -1;
            return true;
        }

    }
}
