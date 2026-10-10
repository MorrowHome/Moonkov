using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Unity.MP_FPS.MoonAlien.Editor
{
    /// <summary>Actual C# checks against runtime classes; no saved scene or asset changes.</summary>
    public static class AlienAmbushChecks
    {
        [MenuItem("Moonkov/Alien/Run Surface Graph and Ambush Checks")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Run outside Play.");
            int groups = RunAll();
            Debug.Log(groups + " surface graph/ambush C# check groups passed. This does not validate " +
                "full-scene perception, the sandbox motor, visual telegraph, damage, performance or networking.");
        }

        // Also callable by a future EditMode harness without adding a test package dependency.
        public static int RunAll()
        {
            EqualCostTie();
            ExposureCosts();
            DisabledAndDisconnected();
            ExpansionBudget();
            OutputAndInvalidInput();
            CapacityAndZeroCostCycle();
            NearestNodeTie();
            VisibleSnapshotOnly();
            ObservationExpires();
            DecisionCadence();
            CommittedTelegraph();
            AttackConsumedOnce();
            RecoveryAndMonotonicIds();
            HitchCancelsExpiredTelegraph();
            DeathCancelsEverything();
            RouteFailureSearch();
            InvalidAndRegressingTime();
            ConfigurableAttackDistance();
            EligibilityRequiredAndFresh();
            LostEligibilityCancelsTelegraph();
            PhysicsSensorBoundary();
            return 21;
        }

        private static AlienSurfaceGraph Diamond(out int lowerEdge, out int upperEdge)
        {
            var graph = new AlienSurfaceGraph();
            graph.AddNode(Vector3.zero, Vector3.up);
            graph.AddNode(Vector3.left, Vector3.up);
            graph.AddNode(Vector3.right, Vector3.up);
            graph.AddNode(Vector3.forward, Vector3.up);
            // Intentionally author the higher-index branch first.
            upperEdge = graph.AddDirectedEdge(0, 2, 1f);
            graph.AddDirectedEdge(2, 3, 1f);
            lowerEdge = graph.AddDirectedEdge(0, 1, 1f);
            graph.AddDirectedEdge(1, 3, 1f);
            return graph;
        }

        private static void EqualCostTie()
        {
            AlienSurfaceGraph graph = Diamond(out _, out _);
            var path = new int[AlienSurfaceGraph.MaxNodes];
            for (int i = 0; i < 32; i++)
            {
                AlienPathResult result = graph.FindPath(0, 3, 128, path, out int count, out int expansions);
                Require(result == AlienPathResult.Found && count == 3 && path[0] == 0 &&
                    path[1] == 1 && path[2] == 3 && expansions == 4, "Equal-cost stable lowest-index tie");
            }
        }

        private static void ExposureCosts()
        {
            AlienSurfaceGraph graph = Diamond(out int lower, out _);
            var path = new int[4];
            Require(graph.SetEdgeExposure(lower, 4f), "Valid exposure update");
            Require(graph.FindPath(0, 3, 128, path, out int count, out _) == AlienPathResult.Found &&
                count == 3 && path[1] == 2, "Exposure must contribute to route cost");
            Require(!graph.SetEdgeExposure(lower, float.NaN) && !graph.SetEdgeExposure(lower, -1f) &&
                !graph.SetEdgeExposure(lower, float.PositiveInfinity), "Reject invalid exposure updates");
            Require(graph.GetEdge(lower).ExposureCost == 4f, "Rejected update preserves cost");
            Require(graph.AddDirectedEdge(0, 3, float.MaxValue, float.MaxValue) >= 0,
                "Finite author costs are valid even when their float sum would overflow");
            Require(graph.FindPath(0, 3, 128, path, out count, out _) == AlienPathResult.Found &&
                count == 3 && path[1] == 2, "Double accumulator preserves finite cheaper route");
        }

        private static void DisabledAndDisconnected()
        {
            AlienSurfaceGraph graph = Diamond(out int lower, out int upper);
            var path = new int[4];
            graph.SetEdgeEnabled(lower, false);
            Require(graph.FindPath(0, 3, 128, path, out int count, out _) == AlienPathResult.Found &&
                count == 3 && path[1] == 2, "Disabled lower branch must not be traversed");
            graph.SetEdgeEnabled(upper, false);
            Require(graph.FindPath(0, 3, 128, path, out count, out int expanded) == AlienPathResult.NoRoute &&
                count == 0 && expanded == 1, "Disabled branches produce explicit no route");
            Require(graph.FindPath(3, 0, 128, path, out count, out _) == AlienPathResult.NoRoute && count == 0,
                "Directed edges do not imply reverse connectivity");
            Require(graph.AddNode(Vector3.down, Vector3.down) == 4, "Stable node index");
            graph.SetEdgeEnabled(lower, true);
            Require(graph.FindPath(0, 4, 128, path, out count, out _) == AlienPathResult.NoRoute && count == 0,
                "Disconnected destination");
        }

        private static void ExpansionBudget()
        {
            AlienSurfaceGraph graph = Diamond(out _, out _);
            var path = new int[4];
            Require(graph.FindPath(0, 3, 0, path, out int count, out int expanded) ==
                AlienPathResult.BudgetExhausted && count == 0 && expanded == 0, "Zero budget is explicit");
            Require(graph.FindPath(0, 3, 2, path, out count, out expanded) ==
                AlienPathResult.BudgetExhausted && count == 0 && expanded == 2, "Expansion cap");
            Require(graph.FindPath(0, 3, 4, path, out count, out expanded) ==
                AlienPathResult.Found && count == 3 && expanded == 4, "Budget covers settled goal");
        }

        private static void OutputAndInvalidInput()
        {
            AlienSurfaceGraph graph = Diamond(out _, out _);
            Require(graph.FindPath(0, 3, 128, new int[2], out int count, out _) ==
                AlienPathResult.OutputTooSmall && count == 0, "Short output never exposes partial route");
            Require(graph.FindPath(0, 3, 128, null, out count, out _) ==
                AlienPathResult.InvalidInput && count == 0, "Null output");
            Require(graph.FindPath(-1, 3, 128, new int[4], out count, out _) ==
                AlienPathResult.InvalidInput && count == 0, "Invalid endpoint");
            Require(graph.FindPath(0, 3, -1, new int[4], out count, out _) ==
                AlienPathResult.InvalidInput && count == 0, "Negative budget");
            var one = new int[1];
            Require(graph.FindPath(2, 2, 0, one, out count, out int expanded) ==
                AlienPathResult.Found && count == 1 && one[0] == 2 && expanded == 0, "Already at goal");
            Require(graph.AddDirectedEdge(0, 3, -1f) == -1 &&
                graph.AddDirectedEdge(0, 3, float.NaN) == -1 &&
                graph.AddDirectedEdge(0, 3, 0f, float.PositiveInfinity) == -1, "Reject invalid edge costs");
            Require(graph.AddNode(Vector3.zero, Vector3.zero) == -1 &&
                graph.AddNode(new Vector3(float.NaN, 0, 0), Vector3.up) == -1, "Reject malformed node");
        }

        private static void CapacityAndZeroCostCycle()
        {
            var graph = new AlienSurfaceGraph();
            for (int i = 0; i < AlienSurfaceGraph.MaxNodes; i++)
                Require(graph.AddNode(Vector3.right * i, Vector3.up * 3f) == i, "Node capacity and stable index");
            Require(graph.AddNode(Vector3.zero, Vector3.up) == -1, "Bound node capacity");
            Require(Vector3.Distance(graph.GetNode(0).Normal, Vector3.up) < .0001f, "Normalize authored normal");
            for (int i = 0; i < AlienSurfaceGraph.MaxEdges; i++)
                Require(graph.AddDirectedEdge(i % 127, i % 127 + 1, 0f) == i, "Edge capacity and stable index");
            Require(graph.AddDirectedEdge(0, 1, 0f) == -1, "Bound edge capacity");
            var path = new int[128];
            Require(graph.FindPath(0, 127, int.MaxValue, path, out int count, out int expanded) ==
                AlienPathResult.Found && count == 128 && expanded == 128, "Hard maximum search budget");
            var cycle = new AlienSurfaceGraph();
            cycle.AddNode(Vector3.zero, Vector3.up); cycle.AddNode(Vector3.one, Vector3.up);
            cycle.AddNode(Vector3.left, Vector3.up);
            cycle.AddDirectedEdge(0, 1, 0); cycle.AddDirectedEdge(1, 0, 0); cycle.AddDirectedEdge(1, 2, 0);
            Require(cycle.FindPath(0, 2, 128, path, out count, out expanded) ==
                AlienPathResult.Found && count == 3 && expanded == 3, "Zero-cost cycle terminates");
        }

        private static void NearestNodeTie()
        {
            var graph = new AlienSurfaceGraph();
            Require(graph.FindNearestNode(Vector3.zero) == -1, "Empty nearest query");
            graph.AddNode(Vector3.left, Vector3.up); graph.AddNode(Vector3.right, Vector3.up);
            Require(graph.FindNearestNode(Vector3.zero) == 0, "Nearest tie selects lowest stable index");
            Require(graph.FindNearestNode(new Vector3(float.NaN, 0, 0)) == -1, "Invalid nearest position");
        }

        private static void VisibleSnapshotOnly()
        {
            var brain = new AlienAmbushBrain();
            Vector3 visible = Vector3.forward * 10f;
            Require(brain.Observe(visible, 0), "Visible sample accepted");
            brain.Decide(0, Vector3.zero);
            Require(brain.State == AlienAmbushState.Stalk && brain.WantsMovement, "Far visible sample stalks");
            visible += Vector3.right * 100f; // Target moves while hidden. No Observe call is made.
            brain.Decide(.21f, Vector3.zero);
            Require(brain.LastObservedPosition == Vector3.forward * 10f &&
                brain.MoveTarget == Vector3.forward * 10f && brain.LastObservedTime == 0,
                "Hidden target movement never updates the copied observation or its timestamp");
        }

        private static void ObservationExpires()
        {
            var brain = new AlienAmbushBrain();
            brain.Observe(Vector3.forward * 10f, 0); brain.Decide(0, Vector3.zero);
            brain.Advance(3.999f);
            Require(brain.HasObservation, "Observation is retained before timeout");
            brain.Advance(4f);
            Require(!brain.HasObservation && !brain.WantsMovement && brain.State == AlienAmbushState.Search &&
                brain.LastObservedPosition == Vector3.zero, "Timeout explicitly forgets and holds to search");
            brain.Advance(6f);
            Require(brain.State == AlienAmbushState.Perch && !brain.WantsMovement, "Search timeout returns to perch");
        }

        private static void DecisionCadence()
        {
            var brain = new AlienAmbushBrain();
            brain.Observe(Vector3.forward * 10f, 0); brain.Decide(0, Vector3.zero);
            brain.Decide(.1f, Vector3.forward * 10f);
            Require(brain.State == AlienAmbushState.Stalk, "No tactical reevaluation before cadence");
            brain.SetAttackEligibility(true, .201f);
            brain.Decide(.201f, Vector3.forward * 10f);
            Require(brain.State == AlienAmbushState.Telegraph, "Cadence permits next tactical decision");
        }

        private static void CommittedTelegraph()
        {
            var brain = new AlienAmbushBrain();
            brain.Observe(Vector3.forward, 0); brain.SetAttackEligibility(true, 0); brain.Decide(0, Vector3.zero);
            Require(brain.State == AlienAmbushState.Telegraph && !brain.WantsMovement, "Telegraph holds movement");
            brain.Observe(Vector3.right, .3f);
            brain.SetAttackEligibility(true, .701f); brain.Advance(.701f);
            Require(brain.TryConsumeAttack(out AlienAttackIntent intent) && intent.Position == Vector3.forward &&
                brain.LastObservedPosition == Vector3.right, "Telegraph commits a point, not live tracking");
        }

        private static void AttackConsumedOnce()
        {
            var brain = new AlienAmbushBrain();
            brain.Observe(Vector3.forward, 0); brain.SetAttackEligibility(true, 0); brain.Decide(0, Vector3.zero);
            brain.SetAttackEligibility(true, .5f); brain.Advance(.5f);
            Require(!brain.TryConsumeAttack(out _), "No early attack");
            brain.SetAttackEligibility(true, .701f); brain.Advance(.701f);
            Require(brain.TryConsumeAttack(out AlienAttackIntent intent) && intent.Id == 1, "Single first intent");
            for (int i = 0; i < 100; i++)
            {
                brain.SetAttackEligibility(true, .701f); brain.Advance(.701f); brain.Decide(.701f, Vector3.zero);
                Require(!brain.TryConsumeAttack(out _), "Repeated same-time updates never re-emit attack");
            }
            brain.Advance(.882f);
            Require(brain.State == AlienAmbushState.Recover && brain.LastAttackId == 1, "Strike enters recovery once");
        }

        private static void RecoveryAndMonotonicIds()
        {
            var brain = new AlienAmbushBrain();
            brain.Observe(Vector3.forward, 0); brain.SetAttackEligibility(true, 0); brain.Decide(0, Vector3.zero);
            brain.SetAttackEligibility(true, .701f); brain.Advance(.701f);
            Require(brain.TryConsumeAttack(out AlienAttackIntent first), "First attack exists");
            brain.Advance(.882f); brain.Decide(1.5f, Vector3.zero);
            Require(brain.State == AlienAmbushState.Recover, "Recovery cannot be bypassed by decision");
            brain.SetAttackEligibility(true, 2.083f);
            brain.Advance(2.083f); brain.Decide(2.083f, Vector3.zero);
            Require(brain.State == AlienAmbushState.Telegraph, "Fresh telegraph after recovery");
            brain.SetAttackEligibility(true, 2.784f); brain.Advance(2.784f);
            Require(brain.TryConsumeAttack(out AlienAttackIntent second) && second.Id == first.Id + 1,
                "Attack identity is monotonic per brain lifetime");
            Require(!brain.TryConsumeAttack(out _), "Second intent also single-consumption");
        }

        private static void HitchCancelsExpiredTelegraph()
        {
            var brain = new AlienAmbushBrain();
            brain.Observe(Vector3.forward, 0); brain.SetAttackEligibility(true, 0); brain.Decide(0, Vector3.zero);
            brain.Advance(100f);
            Require(brain.State == AlienAmbushState.Search && !brain.HasObservation &&
                brain.LastAttackId == 0 && !brain.TryConsumeAttack(out _), "Large hitch forgets before strike");
            var pending = new AlienAmbushBrain();
            pending.Observe(Vector3.forward, 0); pending.SetAttackEligibility(true, 0); pending.Decide(0, Vector3.zero);
            pending.SetAttackEligibility(true, .701f); pending.Advance(.701f); pending.Advance(100f);
            Require(pending.State == AlienAmbushState.Recover && pending.LastAttackId == 1,
                "A hitch advances one timed state and cannot catch up attack loops");
            Require(pending.TryConsumeAttack(out _) && !pending.TryConsumeAttack(out _), "Pending intent is never duplicated");
        }

        private static void DeathCancelsEverything()
        {
            var windup = new AlienAmbushBrain();
            windup.Observe(Vector3.forward, 0); windup.SetAttackEligibility(true, 0); windup.Decide(0, Vector3.zero); windup.Kill();
            windup.Advance(1f); windup.Decide(1f, Vector3.zero); windup.NotifyRouteUnavailable(1f);
            Require(windup.State == AlienAmbushState.Dead && !windup.HasObservation && !windup.WantsMovement &&
                !windup.Observe(Vector3.one, 2f) && !windup.TryConsumeAttack(out _), "Death is terminal during telegraph");
            var issued = new AlienAmbushBrain();
            issued.Observe(Vector3.forward, 0); issued.SetAttackEligibility(true, 0); issued.Decide(0, Vector3.zero);
            issued.SetAttackEligibility(true, .701f); issued.Advance(.701f);
            issued.Kill(); issued.Kill();
            Require(issued.LastAttackId == 1 && !issued.TryConsumeAttack(out _) &&
                issued.CommittedAttackPosition == Vector3.zero, "Death cancels unconsumed intent without reusing its id");
        }

        private static void RouteFailureSearch()
        {
            var brain = new AlienAmbushBrain();
            brain.Observe(Vector3.forward * 10f, 0); brain.Decide(0, Vector3.zero);
            brain.NotifyRouteUnavailable(.1f); brain.NotifyRouteUnavailable(.2f);
            Require(brain.State == AlienAmbushState.Search && !brain.HasObservation && !brain.WantsMovement,
                "Unavailable route forgets and holds");
            brain.Advance(2.101f);
            Require(brain.State == AlienAmbushState.Perch, "Repeated failure notifications do not extend search forever");
            brain.Observe(Vector3.forward * 5f, 2.2f); brain.Decide(2.2f, Vector3.zero);
            Require(brain.State == AlienAmbushState.Approach && brain.WantsMovement, "New visible observation reacquires");
            var windup = new AlienAmbushBrain();
            windup.Observe(Vector3.forward, 0); windup.SetAttackEligibility(true, 0); windup.Decide(0, Vector3.zero);
            windup.SetAttackEligibility(true, .701f); windup.NotifyRouteUnavailable(.701f);
            Require(windup.State == AlienAmbushState.Search && windup.LastAttackId == 0 &&
                !windup.TryConsumeAttack(out _), "Route failure cancels before advancing a due strike");
        }

        private static void InvalidAndRegressingTime()
        {
            var brain = new AlienAmbushBrain();
            Require(!brain.Observe(Vector3.one, float.NaN) && !brain.Observe(Vector3.one, -1f), "Reject invalid time");
            brain.Observe(Vector3.forward * 10f, 2f); brain.Decide(2f, Vector3.zero);
            Require(!brain.Observe(Vector3.right, 1f), "Reject old observation arrival");
            brain.Advance(1f); brain.Decide(float.PositiveInfinity, Vector3.zero);
            Require(brain.LastObservedTime == 2f && brain.State == AlienAmbushState.Stalk,
                "Invalid or backwards time cannot mutate state");
            Require(!brain.Observe(new Vector3(float.NaN, 0, 0), 3f), "Reject invalid observation position");
        }

        private static void ConfigurableAttackDistance()
        {
            var brain = new AlienAmbushBrain(.5f);
            brain.Observe(Vector3.forward, 0); brain.SetAttackEligibility(true, 0); brain.Decide(0, Vector3.zero);
            Require(brain.AttackDistance == .5f && brain.State == AlienAmbushState.Approach,
                "Configured attack distance controls telegraph entry");
            bool threw = false;
            try { new AlienAmbushBrain(float.NaN); } catch (ArgumentOutOfRangeException) { threw = true; }
            Require(threw, "Invalid attack distance fails configuration explicitly");
        }

        private static void EligibilityRequiredAndFresh()
        {
            var brain = new AlienAmbushBrain();
            brain.Observe(Vector3.forward, 0); brain.Decide(0, Vector3.zero);
            Require(brain.State == AlienAmbushState.Approach && brain.LastAttackId == 0,
                "Attack eligibility defaults false even with a nearby remembered target");
            brain.SetAttackEligibility(true, .21f); brain.Decide(.21f, Vector3.zero);
            Require(brain.State == AlienAmbushState.Telegraph, "Fresh explicit eligibility permits a telegraph");
            brain.Advance(.361f);
            Require(brain.State == AlienAmbushState.Approach && !brain.TryConsumeAttack(out _) &&
                brain.CommittedAttackPosition == Vector3.zero, "Expired eligibility cancels wind-up before issuance");
            brain.SetAttackEligibility(true, .42f); brain.Decide(.42f, Vector3.zero);
            Require(brain.State == AlienAmbushState.Telegraph, "Reacquisition begins a full fresh telegraph");
            brain.Advance(1.121f);
            Require(brain.LastAttackId == 0 && !brain.TryConsumeAttack(out _),
                "Telegraph duration alone cannot issue with stale eligibility");
        }

        private static void LostEligibilityCancelsTelegraph()
        {
            var brain = new AlienAmbushBrain();
            brain.Observe(Vector3.forward, 0); brain.SetAttackEligibility(true, 0); brain.Decide(0, Vector3.zero);
            brain.SetAttackEligibility(false, .3f);
            Require(brain.State == AlienAmbushState.Approach && brain.HasObservation &&
                brain.LastObservedPosition == Vector3.forward && !brain.TryConsumeAttack(out _),
                "Lost current LOS cancels telegraph while preserving last-observation stalking memory");
            brain.Advance(.701f);
            Require(brain.LastAttackId == 0, "Cancelled telegraph never emits later");
            brain.SetAttackEligibility(true, .8f); brain.Decide(.8f, Vector3.zero);
            Require(brain.State == AlienAmbushState.Telegraph, "Visibility recovery can start another full telegraph");
            brain.Observe(Vector3.forward * 3f, .9f); brain.SetAttackEligibility(false, .9f);
            brain.Advance(1.501f);
            Require(brain.State == AlienAmbushState.Approach && brain.LastAttackId == 0 &&
                !brain.TryConsumeAttack(out _), "Out-of-attack-range eligibility cancels committed wind-up");
        }

        private static void PhysicsSensorBoundary()
        {
            Scene previous = SceneManager.GetActiveScene();
            bool previousDirty = previous.isDirty;
            int sceneCount = SceneManager.sceneCount;
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(scene);
                var root = new GameObject("Temporary alien ambush sensor checks");
                Vector3 origin = new Vector3(24000, 24000, 24000);
                root.transform.position = origin;
                var terrain = new GameObject("Allowed sensor occluders").transform;
                terrain.SetParent(root.transform, false);
                AlienGroundProbe probe = root.AddComponent<AlienGroundProbe>();
                probe.Configure(terrain);
                var brain = new AlienAmbushBrain();
                Vector3 eye = origin + Vector3.up;
                Vector3 seen = eye + Vector3.forward * 10f;
                Physics.SyncTransforms();
                Require(AlienAmbushSandbox.TryObserveVisible(probe, brain, eye, Vector3.forward, seen, 0f),
                    "Real sensor accepts an unobstructed in-cone, in-range observation");
                brain.Decide(0f, eye);
                Require(brain.LastObservedPosition == seen && brain.LastObservedTime == 0f,
                    "Successful physics sensor publishes the snapshot");

                var blocker = GameObject.CreatePrimitive(PrimitiveType.Cube);
                blocker.name = "Physical opaque sensor blocker";
                blocker.transform.SetParent(terrain, false);
                blocker.transform.localPosition = new Vector3(0f, 1f, 5f);
                blocker.transform.localScale = new Vector3(4f, 4f, .5f);
                Physics.SyncTransforms();
                Vector3 hidden = seen + Vector3.right + Vector3.forward;
                Require(probe.Obstructed(eye, hidden, .01f), "Actual Unity physics sees the cover");
                Require(!AlienAmbushSandbox.TryObserveVisible(probe, brain, eye, Vector3.forward, hidden, .1f),
                    "Actual sensor rejects a target behind a physical blocker");
                brain.Advance(.1f);
                Require(brain.LastObservedPosition == seen && brain.LastObservedTime == 0f,
                    "Physically hidden movement does not refresh location or timestamp");

                blocker.GetComponent<Collider>().enabled = false;
                Physics.SyncTransforms();
                Require(!AlienAmbushSandbox.TryObserveVisible(probe, brain, eye, Vector3.forward,
                    eye + Vector3.forward * 19f, .2f), "Real sensor rejects outside 18m range");
                Require(!AlienAmbushSandbox.TryObserveVisible(probe, brain, eye, Vector3.forward,
                    eye + Vector3.back * 5f, .3f), "Real sensor rejects outside the sight cone");
                Require(brain.LastObservedPosition == seen && brain.LastObservedTime == 0f,
                    "Range and FOV rejection preserve the last confirmed observation");
                Require(AlienAmbushSandbox.TryObserveVisible(probe, brain, eye, Vector3.forward, hidden, .4f) &&
                    brain.LastObservedPosition == hidden && brain.LastObservedTime == .4f,
                    "Removing physical cover permits a new confirmed observation");
                brain.Advance(4.401f);
                Require(!brain.HasObservation && brain.State == AlienAmbushState.Search,
                    "No further visible samples means timed forgetting still applies");

                var attackBrain = new AlienAmbushBrain();
                Vector3 near = eye + Vector3.forward;
                Require(AlienAmbushSandbox.TryObserveVisible(probe, attackBrain, eye, Vector3.forward, near, 0f),
                    "Near target produces a visible snapshot");
                bool eligible = AlienAmbushSandbox.IsAttackEligible(probe, eye, Vector3.forward,
                    near, attackBrain.AttackDistance);
                Require(eligible, "Real attack gate accepts clear visible target in attack range");
                attackBrain.SetAttackEligibility(eligible, 0f); attackBrain.Decide(0f, eye);
                Require(attackBrain.State == AlienAmbushState.Telegraph, "Visible close target starts telegraph");
                blocker.transform.localPosition = new Vector3(0f, 1f, .5f);
                blocker.transform.localScale = new Vector3(4f, 4f, .1f);
                blocker.GetComponent<Collider>().enabled = true;
                Physics.SyncTransforms();
                eligible = AlienAmbushSandbox.IsAttackEligible(probe, eye, Vector3.forward,
                    near, attackBrain.AttackDistance);
                Require(!eligible, "Real attack gate rejects new physical cover during telegraph");
                attackBrain.SetAttackEligibility(eligible, .1f); attackBrain.Advance(.8f);
                Require(attackBrain.State == AlienAmbushState.Approach && attackBrain.LastAttackId == 0 &&
                    !attackBrain.TryConsumeAttack(out _), "Physical LOS loss cancels the attack without an intent");

                blocker.GetComponent<Collider>().enabled = false;
                Physics.SyncTransforms();
                Require(AlienAmbushSandbox.TryObserveVisible(probe, attackBrain, eye, Vector3.forward, near, .9f),
                    "Near target reacquires after physical cover removal");
                attackBrain.SetAttackEligibility(AlienAmbushSandbox.IsAttackEligible(probe, eye,
                    Vector3.forward, near, attackBrain.AttackDistance), .9f);
                attackBrain.Decide(.9f, eye);
                Require(attackBrain.State == AlienAmbushState.Telegraph, "Reacquisition begins new telegraph");
                Vector3 outOfAttackRange = eye + Vector3.forward * 3f;
                Require(AlienAmbushSandbox.TryObserveVisible(probe, attackBrain, eye, Vector3.forward,
                    outOfAttackRange, 1.601f), "Out-of-attack-range target remains visible to observation sensor");
                Require(attackBrain.State == AlienAmbushState.Telegraph && attackBrain.LastAttackId == 0,
                    "Observation callback cannot issue a strike before the same-frame eligibility update");
                eligible = AlienAmbushSandbox.IsAttackEligible(probe, eye, Vector3.forward,
                    outOfAttackRange, attackBrain.AttackDistance);
                Require(!eligible, "Actual attack gate rejects beyond attack distance");
                attackBrain.SetAttackEligibility(eligible, 1.601f); attackBrain.Advance(1.601f);
                Require(attackBrain.LastAttackId == 0 && !attackBrain.TryConsumeAttack(out _),
                    "Same-frame out-of-range rejection prevents strike even after telegraph duration");

                blocker.transform.localPosition = new Vector3(0f, 1f, 0f);
                blocker.transform.localScale = Vector3.one * .5f;
                blocker.GetComponent<Collider>().enabled = true;
                Physics.SyncTransforms();
                Require(probe.Overlaps(eye, .01f), "Actual Unity overlap detects eye-enclosing cover");
                Require(!AlienAmbushSandbox.TryObserveVisible(probe, attackBrain, eye, Vector3.forward, near, 2f) &&
                    !AlienAmbushSandbox.IsAttackEligible(probe, eye, Vector3.forward, near, attackBrain.AttackDistance),
                    "Both observation and attack sensing fail closed when cover encloses the eye");
            }
            finally
            {
                if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
                Physics.SyncTransforms();
                Require(SceneManager.sceneCount == sceneCount &&
                    SceneManager.GetActiveScene().handle == previous.handle && previous.isDirty == previousDirty,
                    "Physics sensor checks restore the scene and dirty-state snapshot");
            }
        }

        private static void Require(bool value, string message)
        {
            if (!value) throw new InvalidOperationException("Alien ambush check failed: " + message);
        }
    }
}
