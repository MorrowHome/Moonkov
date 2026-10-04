using Unity.NetCode;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MP_FPS
{
    /// <summary>Authored shared positions only. All loot, inventory and extraction rules run on the server.</summary>
    public sealed class MoonRaidMap : MonoBehaviour
    {
        public static MoonRaidMap Active { get; private set; }
        [SerializeField] private Vector3[] m_LootPositions = new Vector3[0];
        [SerializeField] private Vector3 m_ExtractionPosition;
        [SerializeField] private PanelSettings m_PanelSettings;
        [SerializeField, Min(30)] private float m_RaidDuration = 600;
        [SerializeField, Min(1)] private float m_ExtractionSeconds = 8;
        [SerializeField, Min(1)] private float m_ExtractionRadius = 5;
        [SerializeField, Min(10)] private float m_LootRespawnSeconds = 60;
        [Header("DollSinger enemies (server authority)")]
        [SerializeField, Range(0, 12)] private int m_EnemyCount = 3;
        [SerializeField, Range(10, 100)] private float m_EnemySightRange = 45;
        [SerializeField, Range(0, 12)] private int m_EnemyCells = 4;
        [Header("Expedition clock (server authority)")]
        [SerializeField, Range(0, 24)] private float m_StartHour = 7;
        [SerializeField, Min(1)] private float m_DayLengthMinutes = 24;
        [SerializeField] private bool m_TimeRunning = true;
        private GameObject[] m_Markers;
        private Material[] m_Materials;
        private readonly System.Collections.Generic.Dictionary<int,GameObject> m_DeathBags = new System.Collections.Generic.Dictionary<int,GameObject>();
        private CorpsePresentationSettings m_CorpseSettings;

        public Vector3[] LootPositions => m_LootPositions;
        public Vector3 ExtractionPosition => m_ExtractionPosition;
        public float RaidDuration => m_RaidDuration;
        public float ExtractionSeconds => m_ExtractionSeconds;
        public float ExtractionRadius => m_ExtractionRadius;
        public float LootRespawnSeconds => m_LootRespawnSeconds;
        public int EnemyCount => m_EnemyCount;
        public float EnemySightRange => m_EnemySightRange;
        public int EnemyCells => m_EnemyCells;
        public float StartHour => m_StartHour;
        public float DayLengthMinutes => Mathf.Max(1, m_DayLengthMinutes);
        public bool TimeRunning => m_TimeRunning;

        private void OnEnable()
        {
            if (m_LootPositions.Length == 0 || m_LootPositions.Length > 24)
            {
                Debug.LogError("Moon raid needs between 1 and 24 authored loot points.", this);
                return;
            }
            Active = this;
        }

        private void Start()
        {
            // No presentation objects or UI are created in a dedicated server.
            if (!ClientServerBootstrap.HasClientWorlds) return;
            m_CorpseSettings=Resources.Load<CorpsePresentationSettings>("Moonkov/CorpsePresentation");
            m_Materials = new Material[5];
            Color[] colors = { new Color(0.2f, 0.8f, 1f), new Color(1f, 0.65f, 0.15f), new Color(0.8f, 0.35f, 1f), new Color(0.2f, 1f, 0.4f),new Color(.12f,.14f,.16f) };
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            for (int i = 0; i < m_Materials.Length; i++)
            {
                m_Materials[i] = new Material(shader);
                m_Materials[i].SetColor("_BaseColor", colors[i]);
                if(i<4){m_Materials[i].SetColor("_EmissionColor", colors[i] * 1.5f);m_Materials[i].EnableKeyword("_EMISSION");}
                else {m_Materials[i].SetFloat("_Metallic",.55f);m_Materials[i].SetFloat("_Smoothness",.35f);}
            }
            m_Markers = new GameObject[m_LootPositions.Length];
            for (int i = 0; i < m_Markers.Length; i++)
            {
                var position=m_LootPositions[i];
                m_Markers[i]=Marker("Supply cache "+(i+1).ToString("00"),PrimitiveType.Cube,position,new Vector3(1.1f,.6f,.8f),m_Materials[4]);
                var lid=Marker("Cache lid",PrimitiveType.Cube,position+Vector3.up*.33f,new Vector3(1.14f,.08f,.84f),m_Materials[4]);
                lid.transform.SetParent(m_Markers[i].transform,true);
                var latch=Marker("Cache indicator",PrimitiveType.Cube,position+new Vector3(0,.1f,-.41f),new Vector3(.18f,.12f,.025f),m_Materials[i%3]);
                latch.transform.SetParent(m_Markers[i].transform,true);
            }
            Marker("Extraction zone", PrimitiveType.Cylinder, m_ExtractionPosition + Vector3.up * 0.08f,
                new Vector3(m_ExtractionRadius * 2, 0.06f, m_ExtractionRadius * 2), m_Materials[3]);
            Marker("Extraction beacon", PrimitiveType.Cylinder, m_ExtractionPosition + Vector3.up * 4,
                new Vector3(0.3f, 4, 0.3f), m_Materials[3]);
            var document = gameObject.AddComponent<UIDocument>();
            document.panelSettings = m_PanelSettings;
            document.sortingOrder = 20;
            gameObject.AddComponent<RaidHUD>();
        }

        private GameObject Marker(string label, PrimitiveType primitive, Vector3 position, Vector3 scale, Material material)
        {
            var marker = GameObject.CreatePrimitive(primitive);
            marker.name = label;
            marker.transform.SetParent(transform, false);
            marker.transform.position = position;
            marker.transform.localScale = scale;
            var collider = marker.GetComponent<UnityEngine.Collider>();
            collider.enabled = false; // Visual markers never change hitscan, LOS, or movement.
            Destroy(collider);
            marker.GetComponent<Renderer>().sharedMaterial = material;
            return marker;
        }

        public void ShowLoot(uint takenMask, bool hasSnapshot)
        {
            if (m_Markers == null) return;
            for (int i = 0; i < m_Markers.Length; i++)
            {
                bool visible = hasSnapshot;
                if (m_Markers[i].activeSelf != visible) m_Markers[i].SetActive(visible);
            }
        }
        public void ShowDeathBags(RaidDeathBagClientState state)
        {
            if(m_CorpseSettings==null || state==null)return;
            foreach(var bag in state.Bags.Values)
            {
                if(m_DeathBags.ContainsKey(bag.LootId))continue;
                var corpse=CorpseVisual.Spawn(transform,m_CorpseSettings,bag);
                m_DeathBags.Add(bag.LootId,corpse.gameObject);
            }
        }

        private void OnDestroy()
        {
            if (Active == this) Active = null;
            if (m_Materials != null)
                foreach (var material in m_Materials) if (material != null) Destroy(material);
        }
    }
}
