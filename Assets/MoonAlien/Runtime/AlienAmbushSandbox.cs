using UnityEngine;

namespace Unity.MP_FPS.MoonAlien
{
    // Opt-in fixture adapter only. No player lookup, damage call, RPC or server registration.
    [RequireComponent(typeof(AlienGroundProbe), typeof(AlienAdhesionRoute))]
    public sealed class AlienAmbushSandbox : MonoBehaviour
    {
        [SerializeField] private Transform m_Course, m_Dummy;
        [SerializeField] private AlienAdhesionRoute.Pose[] m_Nodes;
        [SerializeField] private Vector2Int[] m_Links;
        [SerializeField] private int m_StartNode;
        [SerializeField] private bool m_Running = true;
        private AlienDummyCombat m_Combat;
        private AlienSurfaceGraph m_Graph;
        private AlienAmbushBrain m_Brain;
        private AlienAdhesionRoute m_Motor;
        private AlienGroundProbe m_Probe;
        private ProceduralAlienLegs m_Legs;
        private readonly int[] m_Path = new int[AlienSurfaceGraph.MaxNodes];
        private readonly int[] m_PathNodes = new int[AlienSurfaceGraph.MaxNodes + 1];
        private float m_Time, m_NextSense, m_NextPlan;
        private int m_CurrentNode, m_GoalNode = -1, m_PathCount, m_AttackIntents;
        private Vector3 m_PlannedObservation;
        private bool m_Visible, m_BlockHandled;
        private string m_PlanStatus = "Waiting for observation";
        private float m_AttackFlash;
        private Vector3 m_AttackPosition;

        public void Configure(Transform course, Transform dummy, AlienAdhesionRoute.Pose[] nodes,
            Vector2Int[] links, int startNode)
        { m_Course = course; m_Dummy = dummy; m_Nodes = nodes; m_Links = links; m_StartNode = startNode; }

        private void Start()
        {
            m_Motor = GetComponent<AlienAdhesionRoute>();
            m_Probe = GetComponent<AlienGroundProbe>();
            m_Legs = GetComponent<ProceduralAlienLegs>();
            m_Combat = GetComponent<AlienDummyCombat>();
            m_Brain = new AlienAmbushBrain(externallyTimedCombat: m_Combat != null);
            if (m_Combat && !m_Combat.Initialize(m_Probe))
            { m_Running = false; m_PlanStatus = "Invalid dummy combat fixture"; m_Motor.SetRunning(false); return; }
            m_Graph = new AlienSurfaceGraph();
            if (!m_Course || !m_Dummy || m_Nodes == null || m_Links == null ||
                m_Nodes.Length > AlienSurfaceGraph.MaxNodes || m_Links.Length * 2 > AlienSurfaceGraph.MaxEdges ||
                m_StartNode < 0 || m_StartNode >= m_Nodes.Length)
            { m_PlanStatus = "Invalid authored graph"; m_Running = false; m_Motor.SetRunning(false); return; }
            for (int i = 0; i < m_Nodes.Length; i++)
                if (m_Graph.AddNode(m_Course.TransformPoint(m_Nodes[i].Centre),
                    m_Course.TransformDirection(m_Nodes[i].Up)) != i)
                { m_PlanStatus = "Invalid node pose"; m_Running = false; m_Motor.SetRunning(false); return; }
            foreach (var link in m_Links)
            {
                if (link.x < 0 || link.y < 0 || link.x >= m_Nodes.Length || link.y >= m_Nodes.Length)
                { m_Running = false; m_PlanStatus = "Invalid link"; break; }
                float distance = Vector3.Distance(m_Graph.GetNode(link.x).Position, m_Graph.GetNode(link.y).Position);
                float turn = Vector3.Angle(m_Graph.GetNode(link.x).Normal, m_Graph.GetNode(link.y).Normal) / 90f;
                float cost = Mathf.Max(.1f, distance + turn * 1.5f);
                if (m_Graph.AddDirectedEdge(link.x, link.y, cost) < 0 ||
                    m_Graph.AddDirectedEdge(link.y, link.x, cost) < 0)
                { m_PlanStatus = "Invalid edge cost/capacity"; m_Running = false; m_Motor.SetRunning(false); return; }
            }
            m_CurrentNode = m_StartNode;
            m_Motor.SetFaceTravel(true);
            m_Motor.SetRunning(false);
        }

        private void OnDisable()
        {
            if (m_Motor) m_Motor.SetRunning(false);
            if (m_Combat) m_Combat.Suspend();
        }

        private void Update()
        {
            if (m_Brain == null || !m_Running || !m_Course) return;
            if (m_Combat)
            {
                if (!m_Combat.isActiveAndEnabled) { m_Combat.Suspend(); m_Motor.SetRunning(false); return; }
                m_Time = m_Combat.BeginFrame(Time.deltaTime, ref m_Brain, out bool resetRoute);
                if (resetRoute)
                { m_PathCount = 0; m_BlockHandled = false; m_NextPlan = m_Time; m_Motor.SetRunning(false); }
            }
            else m_Time += Mathf.Min(Time.deltaTime, .05f);
            if (m_Time >= m_NextSense)
            {
                m_NextSense = m_Time + .1f;
                Sense();
            }
            bool currentAttackEligible = SenseAttackEligibility();
            m_Brain.SetAttackEligibility(currentAttackEligible, m_Time);
            m_Brain.Advance(m_Time);
            m_Brain.Decide(m_Time, transform.position + transform.up * AlienAdhesionRoute.BodyOffset);
            if (m_PathCount > 0)
            {
                int reached = Mathf.Clamp(m_Motor.CurrentWaypoint - 1, 0, m_PathCount);
                m_CurrentNode = m_PathNodes[reached];
            }
            if (!m_Brain.WantsMovement)
            {
                m_Motor.SetRunning(false);
            }
            else if (m_Motor.IsBlocked && !m_BlockHandled)
            {
                DisableBlockedLink();
                m_BlockHandled = true;
                m_PathCount = 0;
                m_Motor.SetRunning(false);
                m_Brain.NotifyRouteUnavailable(m_Time);
                m_PlanStatus = "Physical route blocked: hold/search";
                m_NextPlan = m_Time + 1f;
            }
            else if (m_Time >= m_NextPlan && (m_PathCount == 0 || m_Motor.IsComplete ||
                (m_Brain.LastObservedPosition - m_PlannedObservation).sqrMagnitude > 1f))
            {
                Plan();
            }
            else m_Motor.SetRunning(m_PathCount > 0 && !m_BlockHandled);
            if (m_Combat) m_Combat.ResolveFrame(m_Brain, currentAttackEligible);
            if (m_Brain.TryConsumeAttack(out var intent))
            {
                // A diagnostic intent at the locked observed point, never actual health damage.
                m_AttackIntents++;
                m_AttackPosition = intent.Position;
                m_AttackFlash = m_Time + .25f;
            }
        }

        private void Sense()
        {
            m_Visible = false;
            if (!m_Dummy || (m_Combat && !m_Combat.TargetAlive)) return;
            Vector3 eye = transform.position + transform.up * AlienAdhesionRoute.BodyOffset;
            Vector3 point = m_Dummy.position + Vector3.up * .8f;
            Vector3 view = (transform.forward + transform.up * .75f).normalized;
            // Only the sensor reads the live dummy. Decision/planning receive confirmed snapshots.
            m_Visible = TryObserveVisible(m_Probe, m_Brain, eye, view, point, m_Time);
        }

        private bool SenseAttackEligibility()
        {
            if (!m_Dummy || (m_Combat && (!m_Combat.TargetAlive || !m_Combat.AttackerAlive))) return false;
            Vector3 eye = transform.position + transform.up * AlienAdhesionRoute.BodyOffset;
            Vector3 point = m_Dummy.position + Vector3.up * .8f;
            Vector3 view = (transform.forward + transform.up * .75f).normalized;
            // A same-frame sensor boolean protects wind-up/strike. Hidden coordinates never
            // leave this sensor or overwrite the remembered/committed target position.
            return IsAttackEligible(m_Probe, eye, view, point, m_Brain.AttackDistance);
        }

        public static bool IsAttackEligible(AlienGroundProbe probe, Vector3 eye, Vector3 view,
            Vector3 point, float range)
        {
            return range > 0f && (point - eye).sqrMagnitude <= range * range && CanSee(probe, eye, view, point);
        }

        public static bool TryObserveVisible(AlienGroundProbe probe, AlienAmbushBrain brain,
            Vector3 eye, Vector3 view, Vector3 point, float now)
        {
            return brain != null && CanSee(probe, eye, view, point) && brain.Observe(point, now);
        }

        private static bool CanSee(AlienGroundProbe probe, Vector3 eye, Vector3 view, Vector3 point)
        {
            if (!probe || !Finite(eye) || !Finite(view) || !Finite(point) || view.sqrMagnitude < .00001f) return false;
            Vector3 offset = point - eye;
            return offset.sqrMagnitude <= 18f * 18f && Vector3.Angle(view, offset) <= 80f &&
                !probe.Overlaps(eye, .01f) && !probe.Obstructed(eye, point, .01f);
        }

        private static bool Finite(Vector3 value) => !float.IsNaN(value.sqrMagnitude) && !float.IsInfinity(value.sqrMagnitude);

        private void Plan()
        {
            m_NextPlan = m_Time + 1f;
            if (!m_Brain.HasObservation) { m_Motor.SetRunning(false); return; }
            Vector3 remembered = m_Brain.LastObservedPosition;
            m_GoalNode = m_Graph.FindNearestNode(remembered);
            // Exposure is to remembered evidence, never the hidden dummy's current position.
            for (int i = 0; i < m_Graph.EdgeCount; i++)
            {
                var edge = m_Graph.GetEdge(i);
                Vector3 midpoint = (m_Graph.GetNode(edge.From).Position + m_Graph.GetNode(edge.To).Position) * .5f;
                bool exposed = !m_Probe.Obstructed(midpoint, remembered, .01f);
                m_Graph.SetEdgeExposure(i, exposed ? 2f : 0f);
            }
            var result = m_Graph.FindPath(m_CurrentNode, m_GoalNode, AlienSurfaceGraph.MaxNodes,
                m_Path, out int count, out int expansions);
            m_PlanStatus = result + " / nodes expanded " + expansions;
            if (result != AlienPathResult.Found)
            { m_PathCount = 0; m_Motor.SetRunning(false); m_Brain.NotifyRouteUnavailable(m_Time); return; }
            var route = new AlienAdhesionRoute.Pose[count + 1];
            route[0] = new AlienAdhesionRoute.Pose(m_Course.InverseTransformPoint(
                transform.position + transform.up * AlienAdhesionRoute.BodyOffset),
                m_Course.InverseTransformDirection(transform.up));
            m_PathNodes[0] = m_CurrentNode;
            for (int i = 0; i < count; i++)
            {
                var node = m_Graph.GetNode(m_Path[i]);
                route[i + 1] = new AlienAdhesionRoute.Pose(m_Course.InverseTransformPoint(node.Position),
                    m_Course.InverseTransformDirection(node.Normal));
                m_PathNodes[i + 1] = m_Path[i];
            }
            m_PathCount = count;
            m_PlannedObservation = remembered;
            m_Motor.ReplaceRoute(route);
            m_BlockHandled = false;
        }

        private void DisableBlockedLink()
        {
            if (m_PathCount == 0) return;
            int next = Mathf.Clamp(m_Motor.CurrentWaypoint, 1, m_PathCount);
            int from = m_PathNodes[next - 1], to = m_PathNodes[next];
            for (int i = 0; i < m_Graph.EdgeCount; i++)
            {
                var edge = m_Graph.GetEdge(i);
                if (edge.From == from && edge.To == to || edge.From == to && edge.To == from)
                    m_Graph.SetEdgeEnabled(i, false);
            }
        }

        private void OnGUI()
        {
            if (m_Brain == null) return;
            GUILayout.BeginArea(new Rect(16, 16, 670, 245), GUI.skin.box);
            GUILayout.Label("AMBUSH SANDBOX / observations and authored graph / no real damage or networking");
            GUILayout.Label("State: " + m_Brain.State + " | last sensor LOS: " + m_Visible + " | memory: " + m_Brain.HasObservation);
            GUILayout.Label(m_PlanStatus + " | " + m_Motor.Status);
            GUILayout.Label("Attack intents: " + m_AttackIntents + " | feet: " + (m_Legs ? m_Legs.PlantedFeet : 0));
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(m_Running ? "Pause alien" : "Run alien"))
            { m_Running = !m_Running; m_Motor.SetRunning(false); if (!m_Running && m_Combat) m_Combat.Suspend(); }
            if (GUILayout.Button("Kill (sandbox)"))
            { if (m_Combat) m_Combat.RequestAttackerDeath(); else m_Brain.Kill(); m_Motor.SetRunning(false); }
            GUILayout.EndHorizontal();
            GUILayout.Label("Move the dummy behind cover or remove a support collider in Scene view.");
            GUILayout.Label("Cyan = planned route; red sphere = one locked attack intent. Stop/Play resets.");
            GUILayout.EndArea();
        }

        private void OnDrawGizmos()
        {
            if (!Application.isPlaying || m_Graph == null) return;
            Gizmos.color = Color.cyan;
            for (int i = 1; i < m_PathCount; i++)
                Gizmos.DrawLine(m_Graph.GetNode(m_Path[i-1]).Position, m_Graph.GetNode(m_Path[i]).Position);
            if (m_Time < m_AttackFlash) { Gizmos.color = Color.red; Gizmos.DrawWireSphere(m_AttackPosition, .6f); }
        }
    }
}
