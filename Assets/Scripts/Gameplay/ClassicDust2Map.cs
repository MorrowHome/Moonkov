using System.Linq;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.AI;

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

        private void OnEnable()
        {
            if (!Configured) return;
            Active = this;
            m_Instance = NavMesh.AddNavMeshData(m_Navigation, transform.position, transform.rotation);
        }

        private void OnDisable()
        {
            if (Active == this) Active = null;
            if (m_Instance.valid) m_Instance.Remove();
        }
    }
}
