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
        [SerializeField] private RaidLootProfile[] m_LootProfiles = new RaidLootProfile[0];
        [SerializeField] private float[] m_LootYaw = new float[0];
        [SerializeField] private Vector3[] m_LootGroundNormals = new Vector3[0];
        [Tooltip("Dedicated PMC entry points. Empty uses the supply route for older maps.")]
        [SerializeField] private Vector3[] m_EnemySpawnPositions = new Vector3[0];
        [Tooltip("Only activate PMC entries this close to a living player. Zero keeps all entries eligible.")]
        [SerializeField, Min(0)] private float m_EnemyActivationDistance;
        [SerializeField] private Vector3 m_ExtractionPosition;
        [Tooltip("Up to seven additional ordinary-raid exits, in world coordinates. Leave empty for the original single exit.")]
        [SerializeField] private Vector3[] m_AdditionalExtractionPositions = new Vector3[0];
        [SerializeField] private PanelSettings m_PanelSettings;
        [SerializeField, Min(30)] private float m_RaidDuration = 600;
        [SerializeField, Min(1)] private float m_ExtractionSeconds = 8;
        [SerializeField, Min(1)] private float m_ExtractionRadius = 5;
        [SerializeField, Min(10)] private float m_LootRespawnSeconds = 60;
        [SerializeField, Min(3)] private float m_LootRefillPlayerRadius = 18;
        [Tooltip("Maximum refills per cache in this server map session; zero disables refills.")]
        [SerializeField, Range(0, 10)] private int m_LootRefillLimit = 2;
        [Header("DollSinger enemies (server authority)")]
        [Tooltip("Optional map-specific population and behavior tuning. Empty retains the shared default.")]
        [SerializeField] private DollSingerAITuning m_EnemyTuning;
        [SerializeField, Range(0, 12)] private int m_EnemyCount = 3;
        [SerializeField, Range(10, 100)] private float m_EnemySightRange = 45;
        [SerializeField, Range(0, 12)] private int m_EnemyCells = 4;
        [Header("Expedition clock (server authority)")]
        [SerializeField, Range(0, 24)] private float m_StartHour = 7;
        [SerializeField, Min(1)] private float m_DayLengthMinutes = 24;
        [SerializeField] private bool m_TimeRunning = true;
        [Header("Timed actions (server authority)")]
        [SerializeField, Min(.1f)] private float m_MedicalSeconds = 3;
        [SerializeField, Min(.1f)] private float m_SearchSeconds = 2;
        [Header("Survival (server authority)")]
        [Tooltip("Optional shared oxygen profile. When empty, existing scene values below are retained.")]
        [SerializeField] private OxygenSettings m_OxygenSettings;
        [SerializeField, Min(1)] private float m_OxygenWalkingMultiplier = 1.1f;
        [SerializeField, Min(1)] private float m_OxygenSprintingMultiplier = 1.75f;
        [SerializeField, Min(1)] private float m_OutdoorOxygenSeconds = 600;
        [SerializeField, Min(0)] private float m_OxygenRecoveryPerSecond = 5;
        [SerializeField, Min(0)] private float m_HypoxiaGraceSeconds = 8;
        [SerializeField, Min(0)] private float m_HypoxiaDamagePerSecond = 3;
        [SerializeField, Min(1)] private float m_EnergyReserveSeconds = 900;
        [SerializeField, Min(1)] private float m_HydrationReserveSeconds = 720;
        [SerializeField, Min(1)] private float m_DigestionSeconds = 600;
        [SerializeField, Min(0)] private float m_NutritionGraceSeconds = 10;
        [SerializeField, Min(0)] private float m_StarvationDamagePerSecond = .5f;
        [SerializeField, Min(0)] private float m_DehydrationDamagePerSecond = 1;
        private GameObject[] m_Markers;
        private Material m_ExtractionMaterial;
        private GameObject m_ExtractionVisuals;
        private RaidSupplyCacheVisuals m_CacheVisuals;
        private readonly System.Collections.Generic.Dictionary<int,GameObject> m_DeathBags = new System.Collections.Generic.Dictionary<int,GameObject>();
        private CorpsePresentationSettings m_CorpseSettings;

        public Vector3[] LootPositions => m_LootPositions;
        public Vector3[] EnemySpawnPositions => m_EnemySpawnPositions != null && m_EnemySpawnPositions.Length > 0
            ? m_EnemySpawnPositions : m_LootPositions;
        public float EnemyActivationDistance => Mathf.Max(0, m_EnemyActivationDistance);
        public RaidLootProfile GetLootProfile(int index) => m_LootProfiles != null && index >= 0 && index < m_LootProfiles.Length
            ? m_LootProfiles[index] : (RaidLootProfile)(Mathf.Abs(index) % 4);
        public float GetLootYaw(int index) => m_LootYaw != null && index >= 0 && index < m_LootYaw.Length ? m_LootYaw[index] : 0;
        public Vector3 GetLootGroundNormal(int index) => m_LootGroundNormals != null && index >= 0 &&
            index < m_LootGroundNormals.Length && m_LootGroundNormals[index].sqrMagnitude > .5f
            ? m_LootGroundNormals[index].normalized : Vector3.up;
        public Vector3 ExtractionPosition => m_ExtractionPosition;
        public int ExtractionCount => RaidExtractionRules.Count(m_AdditionalExtractionPositions);
        public bool TryExtractionPosition(int index, out Vector3 position) =>
            RaidExtractionRules.TryPosition(m_ExtractionPosition, m_AdditionalExtractionPositions, index, out position);
        public int NearestExtractionIndex(Vector3 position) =>
            RaidExtractionRules.NearestIndex(m_ExtractionPosition, m_AdditionalExtractionPositions, position);
        public Vector3 NearestExtraction(Vector3 position) =>
            RaidExtractionRules.Nearest(m_ExtractionPosition, m_AdditionalExtractionPositions, position);
        public void AdvanceExtraction(ref RaidSession session, Vector3 position, float deltaTime) =>
            RaidExtractionRules.Advance(ref session, m_ExtractionPosition, m_AdditionalExtractionPositions, position, m_ExtractionRadius, deltaTime);
        public float MedicalSeconds => float.IsFinite(m_MedicalSeconds) && m_MedicalSeconds>0 ? Mathf.Max(.1f,m_MedicalSeconds) : 3;
        public float SearchSeconds => float.IsFinite(m_SearchSeconds) && m_SearchSeconds>0 ? Mathf.Max(.1f,m_SearchSeconds) : 2;
        public float RaidDuration => m_RaidDuration;
        public float ExtractionSeconds => m_ExtractionSeconds;
        public float ExtractionRadius => m_ExtractionRadius;
        public float LootRespawnSeconds => m_LootRespawnSeconds;
        public float LootRefillPlayerRadius => Mathf.Max(3, m_LootRefillPlayerRadius);
        public int LootRefillLimit => Mathf.Clamp(m_LootRefillLimit, 0, 10);
        public int EnemyCount => m_EnemyCount;
        public DollSingerAITuning EnemyTuning => m_EnemyTuning;
        public float EnemySightRange => m_EnemySightRange;
        public int EnemyCells => m_EnemyCells;
        public float StartHour => m_StartHour;
        public float DayLengthMinutes => Mathf.Max(1, m_DayLengthMinutes);
        public bool TimeRunning => m_TimeRunning;
        public OxygenParameters OxygenParameters => m_OxygenSettings ? m_OxygenSettings.Parameters :
            new OxygenParameters(m_OutdoorOxygenSeconds, m_OxygenRecoveryPerSecond, m_HypoxiaGraceSeconds,
                m_HypoxiaDamagePerSecond, m_OxygenWalkingMultiplier, m_OxygenSprintingMultiplier);
        public float OutdoorOxygenSeconds => OxygenParameters.OutdoorSeconds;
        public float OxygenRecoveryPerSecond => OxygenParameters.AirRefillPerSecond;
        public float HypoxiaGraceSeconds => OxygenParameters.GraceSeconds;
        public float HypoxiaDamagePerSecond => OxygenParameters.DamagePerSecond;
        public float EnergyReserveSeconds => m_EnergyReserveSeconds;
        public float HydrationReserveSeconds => m_HydrationReserveSeconds;
        public float DigestionSeconds => m_DigestionSeconds;
        public float NutritionGraceSeconds => m_NutritionGraceSeconds;
        public float StarvationDamagePerSecond => m_StarvationDamagePerSecond;
        public float DehydrationDamagePerSecond => m_DehydrationDamagePerSecond;

        private void OnEnable()
        {
            if (m_LootPositions == null || m_LootPositions.Length == 0 || m_LootPositions.Length > RaidLootContainers.FirstDeathBagId)
            {
                Debug.LogError("Moon raid needs between 1 and 64 authored loot points.", this);
                return;
            }
            if (m_AdditionalExtractionPositions != null && m_AdditionalExtractionPositions.Length >= RaidExtractionRules.MaxExits)
                Debug.LogWarning("Moon raid supports at most seven additional exits; extra entries are ignored.", this);
            for (int i = 0; i < ExtractionCount; i++)
                if (!TryExtractionPosition(i, out _)) Debug.LogError("Invalid extraction position at index " + i + ".", this);
            Active = this;
            if(Application.isPlaying)BountyClashArenaSettings.EnsureForMap(this);
        }

        private void OnDisable()
        {
            if (Active == this) Active = null;
        }

        private void Start()
        {
            // No presentation objects or UI are created in a dedicated server.
            if (!ClientServerBootstrap.HasClientWorlds) return;
            m_CorpseSettings=Resources.Load<CorpsePresentationSettings>("Moonkov/CorpsePresentation");
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            var beaconColor = new Color(.2f, 1f, .4f);
            m_ExtractionMaterial = new Material(shader);
            m_ExtractionMaterial.SetColor("_BaseColor", beaconColor);
            m_ExtractionMaterial.SetColor("_EmissionColor", beaconColor * 1.5f);
            m_ExtractionMaterial.EnableKeyword("_EMISSION");
            m_CacheVisuals = new RaidSupplyCacheVisuals(shader, m_LootPositions.Length);
            m_Markers = new GameObject[m_LootPositions.Length];
            for (int i = 0; i < m_Markers.Length; i++)
            {
                m_Markers[i] = m_CacheVisuals.Create(transform, m_LootPositions[i], i);
                m_Markers[i].transform.rotation = Quaternion.FromToRotation(Vector3.up, GetLootGroundNormal(i)) *
                    Quaternion.Euler(0, GetLootYaw(i), 0);
                m_Markers[i].name += " / " + GetLootProfile(i);
                m_Markers[i].SetActive(false); // Do not show stocked caches before the first server snapshot.
            }
            m_ExtractionVisuals=new GameObject("Expedition extraction presentation");
            m_ExtractionVisuals.transform.SetParent(transform,false);
            for (int i = 0; i < ExtractionCount; i++)
            {
                if (!TryExtractionPosition(i, out var exit)) continue;
                Marker(i == 0 ? "Extraction zone" : "Extraction zone " + i, PrimitiveType.Cylinder, exit + Vector3.up * 0.08f,
                    new Vector3(m_ExtractionRadius * 2, 0.06f, m_ExtractionRadius * 2), m_ExtractionMaterial);
                Marker(i == 0 ? "Extraction beacon" : "Extraction beacon " + i, PrimitiveType.Cylinder, exit + Vector3.up * 4,
                    new Vector3(0.3f, 4, 0.3f), m_ExtractionMaterial);
            }
            var document = gameObject.AddComponent<UIDocument>();
            document.panelSettings = m_PanelSettings;
            document.sortingOrder = 20;
            gameObject.AddComponent<RaidHUD>();
        }

        private GameObject Marker(string label, PrimitiveType primitive, Vector3 position, Vector3 scale, Material material)
        {
            var marker = GameObject.CreatePrimitive(primitive);
            marker.name = label;
            marker.transform.SetParent(m_ExtractionVisuals.transform, false);
            marker.transform.position = position;
            marker.transform.localScale = scale;
            var collider = marker.GetComponent<UnityEngine.Collider>();
            collider.enabled = false; // Visual markers never change hitscan, LOS, or movement.
            Destroy(collider);
            marker.GetComponent<Renderer>().sharedMaterial = material;
            return marker;
        }

        private void LateUpdate()
        {
            if(m_ExtractionVisuals)
                m_ExtractionVisuals.SetActive(!(BountyClashArenaSettings.Active && BountyClashArenaSettings.Active.ModeActive));
        }

        public void ShowLoot(ulong takenMask, bool hasSnapshot)
        {
            if (m_Markers == null) return;
            for (int i = 0; i < m_Markers.Length; i++)
            {
                bool visible = hasSnapshot;
                var arena=BountyClashArenaSettings.Active;
                if(visible && arena && arena.ModeActive)visible=arena.Contains(m_LootPositions[i]);
                if (m_Markers[i].activeSelf != visible) m_Markers[i].SetActive(visible);
                if (hasSnapshot) m_CacheVisuals.SetEmpty(i, (takenMask & (1UL << i)) != 0);
            }
        }
        private readonly System.Collections.Generic.Dictionary<int,string> m_DropVisualCodes = new System.Collections.Generic.Dictionary<int,string>();
        private readonly System.Collections.Generic.List<int> m_HiddenBags = new System.Collections.Generic.List<int>();
        public void ShowDeathBags(RaidDeathBagClientState state)
        {
            if(m_CorpseSettings==null || state==null)return;
            m_HiddenBags.Clear();
            foreach(var pair in m_DeathBags)
                if(!state.Bags.ContainsKey(pair.Key)) {Destroy(pair.Value);m_HiddenBags.Add(pair.Key);}
            foreach(int id in m_HiddenBags){m_DeathBags.Remove(id);m_DropVisualCodes.Remove(id);}
            foreach(var bag in state.Bags.Values)
            {
                if(m_DeathBags.TryGetValue(bag.LootId,out var existing))
                {
                    // Empty drops retain their authoritative grace-period record, but no visible item.
                    if(bag.CharacterIndex!=RaidLootContainers.DroppedItemCharacterIndex)continue;
                    existing.SetActive(!bag.Empty);
                    state.ItemCodes.TryGetValue(bag.LootId,out string currentCode);
                    m_DropVisualCodes.TryGetValue(bag.LootId,out string displayedCode);
                    if(currentCode==displayedCode)continue;
                    Destroy(existing);m_DeathBags.Remove(bag.LootId);
                }
                if(bag.CharacterIndex == RaidLootContainers.DroppedItemCharacterIndex)
                {
                    state.ItemCodes.TryGetValue(bag.LootId,out string code);
                    var dropped = PhysicalItemVisuals.Create(code,transform);
                    bool fallback=!dropped;
                    if(!dropped)
                    {
                        // Halo art is intentionally outside this asset pass.
                        dropped = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        dropped.transform.SetParent(transform,false);
                        dropped.transform.localScale = new Vector3(.4f,.3f,.3f);
                        var collider=dropped.GetComponent<Collider>();collider.enabled=false;Destroy(collider);
                    }
                    dropped.name = "Dropped " + code + " / " + bag.LootId;
                    dropped.transform.SetPositionAndRotation((Vector3)bag.Position+(fallback ? Vector3.up*.15f : Vector3.zero),bag.Rotation);
                    dropped.SetActive(!bag.Empty);
                    m_DeathBags.Add(bag.LootId,dropped);
                    m_DropVisualCodes[bag.LootId]=code;
                    continue;
                }
                if(bag.CharacterIndex == -1)
                {
                    m_DeathBags.Add(bag.LootId, AlienLootVisual.Create(transform, bag));
                    continue;
                }
                var corpse=CorpseVisual.Spawn(transform,m_CorpseSettings,bag);
                m_DeathBags.Add(bag.LootId,corpse.gameObject);
            }
        }

        private void OnDestroy()
        {
            if (Active == this) Active = null;
            m_CacheVisuals?.Dispose();
            if (m_ExtractionMaterial != null) Destroy(m_ExtractionMaterial);
        }
    }

    // Map-authored exits share the existing timer and radius. All configured
    // exits are open; spawn-specific availability is a separate future rule.
    public static class RaidExtractionRules
    {
        public const int MaxExits = 8;
        public static int Count(Vector3[] extra) => 1 + System.Math.Min(extra?.Length ?? 0, MaxExits - 1);
        private static bool Finite(Vector3 p) => float.IsFinite(p.x) && float.IsFinite(p.y) && float.IsFinite(p.z);
        public static bool TryPosition(Vector3 primary, Vector3[] extra, int index, out Vector3 position)
        {
            position = default;
            if (index < 0 || index >= Count(extra)) return false;
            position = index == 0 ? primary : extra[index - 1];
            return Finite(position);
        }
        private static double HorizontalSquared(Vector3 a, Vector3 b)
        {
            double x = (double)a.x - b.x, z = (double)a.z - b.z;
            return x * x + z * z;
        }
        private static bool Inside(Vector3 position, Vector3 exit, float radius) =>
            Finite(position) && Finite(exit) && float.IsFinite(radius) && radius > 0 &&
            System.Math.Abs((double)position.y - exit.y) <= 3 && HorizontalSquared(position, exit) <= (double)radius * radius;
        public static int NearestIndex(Vector3 primary, Vector3[] extra, Vector3 position)
        {
            if (!Finite(position)) return -1;
            int best = -1; double distance = double.MaxValue;
            for (int i = 0; i < Count(extra); i++)
            {
                if (!TryPosition(primary, extra, i, out var candidate)) continue;
                double y = (double)position.y - candidate.y;
                double next = HorizontalSquared(position, candidate) + y * y;
                if (next >= distance) continue;
                best = i; distance = next;
            }
            return best;
        }
        public static Vector3 Nearest(Vector3 primary, Vector3[] extra, Vector3 position)
        {
            int index = NearestIndex(primary, extra, position);
            return TryPosition(primary, extra, index, out var exit) ? exit : primary;
        }
        public static int AtPosition(Vector3 primary, Vector3[] extra, Vector3 position, float radius, int previous)
        {
            if (TryPosition(primary, extra, previous, out var prior) && Inside(position, prior, radius)) return previous;
            int best = -1; double distance = double.MaxValue;
            for (int i = 0; i < Count(extra); i++)
            {
                if (!TryPosition(primary, extra, i, out var candidate) || !Inside(position, candidate, radius)) continue;
                double next = HorizontalSquared(position, candidate);
                if (next >= distance) continue;
                best = i; distance = next;
            }
            return best;
        }
        public static void Advance(ref RaidSession session, Vector3 primary, Vector3[] extra, Vector3 position, float radius, float deltaTime)
        {
            int previous = session.ExtractionProgress > 0 ? session.ExtractionIndex : -1;
            int current = AtPosition(primary, extra, position, radius, previous);
            if (current < 0) { session.ExtractionProgress = 0; session.ExtractionIndex = -1; return; }
            if (current != previous) session.ExtractionProgress = 0;
            session.ExtractionIndex = current;
            if (float.IsFinite(deltaTime) && deltaTime > 0) session.ExtractionProgress += deltaTime;
        }
    }
}
