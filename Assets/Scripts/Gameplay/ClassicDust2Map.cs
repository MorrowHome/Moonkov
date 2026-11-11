using System.Linq;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.AI;
using Unity.NetCode;

namespace Unity.MP_FPS
{
    [DisallowMultipleComponent]
    public sealed class ClassicDust2Map : MonoBehaviour
    {
        public const string ResourcePath = "Moonkov/Maps/ClassicDust2";
        public const string SceneName = "ClassicGameScene";
        [SerializeField] private Transform[] m_AttackSpawns, m_DefenseSpawns;
        [SerializeField] private BoxCollider m_SiteA, m_SiteB;
        [SerializeField] private NavMeshData m_Navigation;
        private RaidSupplyCacheVisuals m_SupplyCaches;
        private GameObject[] m_SupplyCacheObjects;
        private Vector3[] m_SupplyCachePositions;
        private const int SupplyCacheCount = 3;

        public static ClassicDust2Map Active { get; private set; }
        public bool Configured => ValidSpawns(m_AttackSpawns) && ValidSpawns(m_DefenseSpawns) &&
            m_SiteA && m_SiteB && m_SiteA.isTrigger && m_SiteB.isTrigger && m_Navigation;
        private NavMeshDataInstance m_Instance;

        private static bool ValidSpawns(Transform[] spawns) => spawns != null && spawns.Length == ClassicMatch.TeamSize &&
            spawns.All(point => point) && spawns.Distinct().Count() == ClassicMatch.TeamSize;

        public static ClassicDust2Map LoadPrefab() => Resources.Load<GameObject>(ResourcePath)?.GetComponent<ClassicDust2Map>();
        public static bool Available
        {
            get { return Resources.Load<TextAsset>("Moonkov/Maps/ClassicDust2Installed") != null; }
        }

        public Vector3 Site(int index) => (index == 0 ? m_SiteA : m_SiteB).bounds.center;
        public Vector3 SupplyCachePosition(int index)
        {
            if (index < 0 || index >= SupplyCacheCount) return Vector3.zero;
            return m_SupplyCachePositions != null ? m_SupplyCachePositions[index] :
                Vector3.Lerp(Site(0), Site(1), (index + 1) * .25f) + Vector3.up * .35f;
        }

        public void PresentSupplyCacheMask(byte availableMask)
        {
            if (m_SupplyCaches == null) return;
            for (int i = 0; i < SupplyCacheCount; i++)
                m_SupplyCaches.SetEmpty(i, (availableMask & (1 << i)) == 0);
        }
        public bool InSite(int index, Vector3 position)
        {
            var site = index == 0 ? m_SiteA : m_SiteB;
            var offset = site.transform.InverseTransformPoint(position) - site.center;
            var half = site.size * .5f;
            return Mathf.Abs(offset.x) <= half.x && Mathf.Abs(offset.y) <= half.y && Mathf.Abs(offset.z) <= half.z;
        }

        public LocalTransform Spawn(bool attacking, int slot)
        {
            var point = (attacking ? m_AttackSpawns : m_DefenseSpawns)[slot];
            return LocalTransform.FromPositionRotation(point.position, point.rotation);
        }

        internal static void InitializeAir(ref PredictedPlayerGhost player)
        {
            player.BreathableAir = true;
            player.AmbientPressureKPa = 101.325f;
            player.AmbientOxygenPartialPressureKPa = player.AmbientPressureKPa * .21f;
        }

        private void OnEnable()
        {
            if (!Configured) return;
            Active = this;
            m_Instance = NavMesh.AddNavMeshData(m_Navigation, transform.position, transform.rotation);
            m_SupplyCachePositions = new Vector3[SupplyCacheCount];
            for (int i = 0; i < SupplyCacheCount; i++)
            {
                var candidate = Vector3.Lerp(Site(0), Site(1), (i + 1) * .25f);
                if (NavMesh.SamplePosition(candidate, out var hit, 18f, NavMesh.AllAreas)) candidate = hit.position;
                m_SupplyCachePositions[i] = candidate + Vector3.up * .35f;
            }
        }

        private void OnDisable()
        {
            if (Active == this) Active = null;
            if (m_Instance.valid) m_Instance.Remove();
            m_SupplyCachePositions = null;
            if (m_SupplyCacheObjects != null)
                foreach (var cache in m_SupplyCacheObjects) if (cache) Destroy(cache);
            m_SupplyCacheObjects = null;
            m_SupplyCaches?.Dispose();
            m_SupplyCaches = null;
        }

        private void Start()
        {
            if (!ClientServerBootstrap.HasClientWorlds) return;
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            m_SupplyCaches = new RaidSupplyCacheVisuals(shader, SupplyCacheCount);
            m_SupplyCacheObjects = new GameObject[SupplyCacheCount];
            for (int i = 0; i < SupplyCacheCount; i++)
                m_SupplyCacheObjects[i] = m_SupplyCaches.Create(transform, SupplyCachePosition(i), i);
        }
    }
}
