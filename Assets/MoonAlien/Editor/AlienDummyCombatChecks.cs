using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Unity.MP_FPS.MoonAlien.Editor
{
    public static class AlienDummyCombatChecks
    {
        [MenuItem("Moonkov/Alien/Run Dummy Combat Integration Checks")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Run outside Play.");
            var snapshot = new SceneSnapshot();
            Scene previous = SceneManager.GetActiveScene(), scene = default;
            try
            {
                scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                SceneManager.SetActiveScene(scene);
                var root = new GameObject("Temporary dummy combat checks");
                root.transform.position = new Vector3(28000, 28000, 28000);
                var terrain = new GameObject("Allowed terrain").transform;
                terrain.SetParent(root.transform, false);
                var alien = new GameObject("Fixture attacker"); alien.transform.SetParent(root.transform, false);
                var probe = alien.AddComponent<AlienGroundProbe>(); probe.Configure(terrain);
                Vector3 eye = alien.transform.position + Vector3.up * AlienAdhesionRoute.BodyOffset;
                var dummy = GameObject.CreatePrimitive(PrimitiveType.Cube);
                dummy.transform.SetParent(root.transform, false); dummy.transform.localScale = Vector3.one * .2f;
                Collider target = dummy.GetComponent<Collider>(); target.isTrigger = true;
                dummy.transform.position = eye + Vector3.forward;
                var hits = new RaycastHit[32]; var overlaps = new Collider[32];
                Physics.SyncTransforms();
                Require(Sample(probe, target, eye, Vector3.forward, hits, overlaps), "Aligned locked sweep hits exact dummy");
                dummy.transform.position = eye + Vector3.forward + Vector3.right * .8f;
                Physics.SyncTransforms();
                Require(!Sample(probe, target, eye, Vector3.forward, hits, overlaps), "Moving sideways misses locked direction despite range and LOS");
                var decoy = GameObject.CreatePrimitive(PrimitiveType.Cube);
                decoy.transform.SetParent(root.transform, false); decoy.transform.localScale = Vector3.one * .2f;
                decoy.transform.position = eye + Vector3.forward;
                Physics.SyncTransforms();
                Require(!Sample(probe, target, eye, Vector3.forward, hits, overlaps), "Wrong collider cannot satisfy committed target");
                UnityEngine.Object.DestroyImmediate(decoy);
                dummy.transform.position = eye + Vector3.forward;
                var cover = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cover.transform.SetParent(terrain, false); cover.transform.localScale = new Vector3(1, 1, .1f);
                cover.transform.position = eye + Vector3.forward * .5f;
                Physics.SyncTransforms();
                Require(!Sample(probe, target, eye, Vector3.forward, hits, overlaps), "Physical cover blocks dummy hit");
                cover.transform.position = eye;
                Physics.SyncTransforms();
                Require(!Sample(probe, target, eye, Vector3.forward, hits, overlaps), "Start inside terrain fails closed");
                UnityEngine.Object.DestroyImmediate(cover);
                target.enabled = false;
                Physics.SyncTransforms();
                Require(!Sample(probe, target, eye, Vector3.forward, hits, overlaps), "Disabled target cannot receive hit");
                target.enabled = true;
                dummy.transform.position = eye + Vector3.forward * 2f;
                Physics.SyncTransforms();
                Require(!Sample(probe, target, eye, Vector3.forward, hits, overlaps), "Out-of-range target misses");
                dummy.transform.position = eye + Vector3.forward;
                Physics.SyncTransforms();
                Require(!Sample(probe, target, eye, Vector3.forward, new RaycastHit[1], overlaps), "Saturated cast buffer fails closed");
                foreach (Vector3 direction in new[] { Vector3.up, Vector3.down, Vector3.left })
                {
                    dummy.transform.position = eye + direction;
                    Physics.SyncTransforms();
                    Require(Sample(probe, target, eye, direction, hits, overlaps), "Sweep is independent of world-up orientation");
                }
                dummy.transform.position = eye + Vector3.forward * .05f;
                Physics.SyncTransforms();
                Require(Sample(probe, target, eye, Vector3.forward, hits, overlaps), "Initial matching target overlap is explicit");
                Require(!Sample(probe, target, eye, Vector3.forward, hits, new Collider[1]), "Saturated overlap buffer fails closed");
                dummy.transform.position = eye + Vector3.forward;
                Physics.SyncTransforms();

                var combat = alien.AddComponent<AlienDummyCombat>(); combat.Configure(target);
                var serialized = new SerializedObject(combat);
                Require(serialized.FindProperty("m_Hitbox") != null &&
                    serialized.FindProperty("m_Hitbox").objectReferenceValue == target,
                    "Authored hitbox is a serialized scene reference rather than transient editor state");
                Require(combat.Initialize(probe), "Initialize isolated adapter");
                Require(!combat.Initialize(probe), "Second initialization cannot reset identities or replay barriers");
                var brain = new AlienAmbushBrain(externallyTimedCombat: true);
                StartWindup(combat, brain, eye, target.bounds.center);
                Require(combat.Phase == AlienCombatPhase.Windup && combat.TargetHealth == 100d, "Committed windup does not apply damage");
                float now = combat.BeginFrame(.71f, ref brain, out _);
                brain.SetAttackEligibility(true, now); brain.Advance(now); combat.ResolveFrame(brain, true);
                Require(combat.TargetHealth == 75d && combat.AcceptedHits == 1, "Actual physics-backed adapter applies 25 once");
                combat.ResolveFrame(brain, true);
                Require(combat.TargetHealth == 75d && combat.AcceptedHits == 1, "Repeated adapter sample cannot duplicate hit");
                combat.RequestTargetRespawn(); combat.BeginFrame(0, ref brain, out _); combat.ResolveFrame(brain, true);
                Require(combat.TargetHealth == 100d && combat.AcceptedHits == 0 && combat.Phase == AlienCombatPhase.Recovery,
                    "Target respawn clears diagnostics and old receipt while preserving attacker cooldown");
                combat.RequestAttackerDeath(); combat.BeginFrame(0, ref brain, out _); combat.ResolveFrame(brain, true);
                Require(!combat.AttackerAlive && brain.State == AlienAmbushState.Dead, "Queued attacker death precedes resolution");
                combat.RequestAttackerRespawn(); now = combat.BeginFrame(0, ref brain, out _);
                Require(combat.AttackerAlive && brain.State != AlienAmbushState.Dead, "Explicit attacker new life creates fresh tactical brain");
                StartWindup(combat, brain, eye, target.bounds.center);
                combat.RequestTargetDeath(); now = combat.BeginFrame(.71f, ref brain, out _);
                brain.SetAttackEligibility(false, now); combat.ResolveFrame(brain, true);
                Require(!combat.TargetAlive && combat.TargetHealth == 0 && combat.AcceptedHits == 0, "Target death on strike sample wins over hit");
                combat.RequestTargetRespawn(); combat.RequestAttackerRespawn(); combat.BeginFrame(0, ref brain, out _);
                StartWindup(combat, brain, eye, target.bounds.center);
                now = combat.BeginFrame(1f, ref brain, out _); brain.SetAttackEligibility(true, now); combat.ResolveFrame(brain, true);
                Require(combat.Phase == AlienCombatPhase.Recovery && combat.TargetHealth == 100d,
                    "Long frame skips the complete strike window without catch-up damage");
                combat.RequestAttackerRespawn(); combat.BeginFrame(0, ref brain, out _);
                StartWindup(combat, brain, eye, target.bounds.center);
                combat.Suspend(); combat.BeginFrame(0, ref brain, out _); combat.ResolveFrame(brain, true);
                Require(combat.Phase == AlienCombatPhase.Idle && combat.TargetHealth == 100d,
                    "Pause/re-enable suspension cancels queued windup rather than resuming a stale hit");
                combat.RequestAttackerRespawn(); combat.BeginFrame(0, ref brain, out _);
                StartWindup(combat, brain, eye, target.bounds.center);
                now = combat.BeginFrame(.71f, ref brain, out _);
                bool eligible = AlienAmbushSandbox.IsAttackEligible(probe, eye, Vector3.back, target.bounds.center, 1.6f);
                Require(!eligible, "Physical attack sensor rejects target outside current FOV at strike opening");
                brain.SetAttackEligibility(eligible, now); brain.Advance(now); combat.ResolveFrame(brain, eligible);
                Require(combat.Phase == AlienCombatPhase.Recovery && combat.TargetHealth == 100d && combat.AcceptedHits == 0,
                    "Fresh boundary rejection cancels opened strike before phase mirroring/damage");
                combat.RequestAttackerRespawn(); combat.BeginFrame(0, ref brain, out _);
                StartWindup(combat, brain, eye, target.bounds.center);
                dummy.transform.position = eye + Vector3.forward + Vector3.right * .8f;
                // Deliberately no Physics.SyncTransforms here: exercise the runtime BeginFrame flush.
                now = combat.BeginFrame(.71f, ref brain, out _);
                eligible = AlienAmbushSandbox.IsAttackEligible(probe, eye, Vector3.forward, target.bounds.center, 1.6f);
                Require(eligible, "Sideways target remains observable and in range");
                brain.SetAttackEligibility(eligible, now); brain.Advance(now); combat.ResolveFrame(brain, eligible);
                Require(combat.TargetHealth == 100d && combat.AcceptedHits == 0,
                    "Runtime sample flushes moved collider and cannot hit its stale locked-path position");
                dummy.transform.position = eye + Vector3.forward;
                combat.RequestAttackerRespawn(); combat.BeginFrame(0, ref brain, out _);
                StartWindup(combat, brain, eye, target.bounds.center);
                now = combat.BeginFrame(.71f, ref brain, out _);
                brain.NotifyRouteUnavailable(now); combat.ResolveFrame(brain, true);
                Require(combat.TargetHealth == 100d && combat.Phase == AlienCombatPhase.Recovery,
                    "Explicit tactical cancellation at strike opening wins even if sampled visibility was true");
                combat.RequestAttackerDeath(); combat.RequestAttackerRespawn(); combat.BeginFrame(0, ref brain, out _);
                Require(!combat.AttackerAlive, "Simultaneous queued death and respawn fail closed with death winning");
            }
            finally
            {
                if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                Physics.SyncTransforms(); snapshot.RequireUnchanged();
            }
            Debug.Log("Dummy combat integration checks passed in Unity physics. This does not validate production health, networking or visual traversal.");
        }
        private static void StartWindup(AlienDummyCombat combat, AlienAmbushBrain brain, Vector3 eye, Vector3 point)
        {
            float now = (float)((double)combat.Tick / AlienDummyCombat.TicksPerSecond);
            brain.Observe(point, now); brain.SetAttackEligibility(true, now); brain.Decide(now, eye); combat.ResolveFrame(brain, true);
            Require(combat.Phase == AlienCombatPhase.Windup, "Start a new full contract-controlled windup");
        }
        private static bool Sample(AlienGroundProbe probe, Collider target, Vector3 eye, Vector3 direction,
            RaycastHit[] hits, Collider[] overlaps) => AlienDummyCombat.SampleLockedSweep(probe, target,
                eye, direction, 1.6f, AlienDummyCombat.SweepRadius, hits, overlaps, out _, out _);
        private static void Require(bool condition, string label) { if (!condition) throw new InvalidOperationException(label); }

        private sealed class SceneSnapshot
        {
            private readonly int active = SceneManager.GetActiveScene().handle;
            private readonly Dictionary<int, bool> dirty = new Dictionary<int, bool>();
            private readonly Dictionary<int, HashSet<int>> roots = new Dictionary<int, HashSet<int>>();
            public SceneSnapshot()
            {
                for (int i = 0; i < SceneManager.sceneCount; i++)
                {
                    Scene scene = SceneManager.GetSceneAt(i); dirty.Add(scene.handle, scene.isDirty);
                    roots.Add(scene.handle, RootIds(scene));
                }
            }
            public void RequireUnchanged()
            {
                Require(SceneManager.sceneCount == dirty.Count && SceneManager.GetActiveScene().handle == active, "Existing scene count/active preserved");
                for (int i = 0; i < SceneManager.sceneCount; i++)
                {
                    Scene scene = SceneManager.GetSceneAt(i);
                    Require(dirty.ContainsKey(scene.handle) && dirty[scene.handle] == scene.isDirty &&
                        roots[scene.handle].SetEquals(RootIds(scene)), "Existing scene identity/dirty/root state preserved");
                }
            }
            private static HashSet<int> RootIds(Scene scene)
            {
                var result = new HashSet<int>();
                if (scene.isLoaded) foreach (var root in scene.GetRootGameObjects()) result.Add(root.GetInstanceID());
                return result;
            }
        }
    }
}
