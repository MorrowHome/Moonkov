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
        private GameObject[] m_Markers;
        private Material[] m_Materials;

        public Vector3[] LootPositions => m_LootPositions;
        public Vector3 ExtractionPosition => m_ExtractionPosition;
        public float RaidDuration => m_RaidDuration;
        public float ExtractionSeconds => m_ExtractionSeconds;
        public float ExtractionRadius => m_ExtractionRadius;
        public float LootRespawnSeconds => m_LootRespawnSeconds;

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
            m_Materials = new Material[4];
            Color[] colors = { new Color(0.2f, 0.8f, 1f), new Color(1f, 0.65f, 0.15f), new Color(0.8f, 0.35f, 1f), new Color(0.2f, 1f, 0.4f) };
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            for (int i = 0; i < m_Materials.Length; i++)
            {
                m_Materials[i] = new Material(shader);
                m_Materials[i].SetColor("_BaseColor", colors[i]);
                m_Materials[i].SetColor("_EmissionColor", colors[i] * 1.5f);
                m_Materials[i].EnableKeyword("_EMISSION");
            }
            m_Markers = new GameObject[m_LootPositions.Length];
            for (int i = 0; i < m_Markers.Length; i++)
                m_Markers[i] = Marker("Supply cache " + i, PrimitiveType.Cube, m_LootPositions[i], Vector3.one * 0.6f, m_Materials[i % 3]);
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
                bool visible = hasSnapshot && (takenMask & (1u << i)) == 0;
                if (m_Markers[i].activeSelf != visible) m_Markers[i].SetActive(visible);
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
