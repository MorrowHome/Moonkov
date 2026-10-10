using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Unity.MP_FPS.Moon
{
    /// <summary>Owns the lunar environment and safe, runtime-only comparison state.</summary>
    public sealed class MoonEnvironment : MonoBehaviour
    {
        [SerializeField] private Terrain surroundingTerrain;
        [SerializeField] private GameObject detailRoot;
        [SerializeField] private Material regolith;
        [SerializeField] private Material[] rockMaterials;
        [SerializeField] private Light sun;
        [SerializeField] private Transform spawnPoint;
        [Header("Runtime near-field geology")]
        [SerializeField] private MoonSurfaceDetailSettings surfaceDetail = new();
        private MoonSurfaceDetail surface;

        private TerrainData sourceTerrainData;
        private TerrainData runtimeTerrainData;
        private readonly Dictionary<Material, Material> runtimeMaterials = new();
        private readonly List<(Renderer renderer, Material[] materials)> originalRenderers = new();
        private Terrain[] terrains;
        private bool[,] authoredHoles;
        private bool[,] completeHoles;
        private bool initialized;

        public bool Detailed { get; private set; } = true;
        public bool LunarLighting { get; private set; } = true;
        public float SunElevation { get; private set; }
        public Transform SpawnPoint => spawnPoint;
        public Light Sun => sun;

        private void Awake() => InitializeRuntime();

        private void InitializeRuntime()
        {
            if (initialized) return;
            if (!surroundingTerrain || !detailRoot || !regolith || !sun || !spawnPoint)
            {
                Debug.LogError("MoonEnvironment is missing required authoring references.", this);
                enabled = false;
                return;
            }

            // SetHoles and material comparisons must never mutate imported source assets.
            sourceTerrainData = surroundingTerrain.terrainData;
            runtimeTerrainData = Instantiate(sourceTerrainData);
            runtimeTerrainData.name = sourceTerrainData.name + " (runtime)";
            surroundingTerrain.terrainData = runtimeTerrainData;
            surroundingTerrain.GetComponent<TerrainCollider>().terrainData = runtimeTerrainData;
            int resolution = runtimeTerrainData.holesResolution;
            authoredHoles = runtimeTerrainData.GetHoles(0, 0, resolution, resolution);
            completeHoles = new bool[resolution, resolution];
            for (int z = 0; z < resolution; z++)
                for (int x = 0; x < resolution; x++) completeHoles[z, x] = true;

            CloneMaterial(regolith);
            foreach (Material material in rockMaterials) CloneMaterial(material);
            terrains = GetComponentsInChildren<Terrain>(true);
            foreach (Terrain terrain in terrains)
            {
                terrain.shadowCastingMode = ShadowCastingMode.TwoSided;
                if (runtimeMaterials.TryGetValue(terrain.materialTemplate, out Material copy))
                    terrain.materialTemplate = copy;
            }
            foreach (Renderer renderer in detailRoot.GetComponentsInChildren<Renderer>(true))
            {
                Material[] originals = renderer.sharedMaterials;
                Material[] replacements = (Material[])originals.Clone();
                bool changed = false;
                for (int i = 0; i < replacements.Length; i++)
                {
                    if (replacements[i] && runtimeMaterials.TryGetValue(replacements[i], out Material copy))
                    {
                        replacements[i] = copy;
                        changed = true;
                    }
                }
                if (!changed) continue;
                originalRenderers.Add((renderer, originals));
                renderer.sharedMaterials = replacements;
            }
            LunarLighting = regolith.IsKeywordEnabled("_MOON_LUNAR_LIGHTING");
            SunElevation = sun.transform.eulerAngles.x;
            if (surfaceDetail != null && surfaceDetail.enabled)
            {
                Terrain near = detailRoot.GetComponentInChildren<Terrain>(true);
                if (near && near != surroundingTerrain)
                {
                    Material rock = rockMaterials.Length > 0 && rockMaterials[0]
                        ? runtimeMaterials[rockMaterials[0]] : null;
                    surface = new MoonSurfaceDetail(near, detailRoot.transform, rock, surfaceDetail, spawnPoint.position);
                    if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null)
                        surface.BindGeometry(runtimeMaterials.Values);
                }
            }
            initialized = true;
        }

        private void LateUpdate()
        {
            if (initialized && Detailed) surface?.Tick();
        }

        private void CloneMaterial(Material source)
        {
            if (source && !runtimeMaterials.ContainsKey(source))
                runtimeMaterials.Add(source, new Material(source) { name = source.name + " (runtime)" });
        }

        public void SetDetailed(bool value)
        {
            if (!initialized) return;
            runtimeTerrainData.SetHoles(0, 0, value ? authoredHoles : completeHoles);
            detailRoot.SetActive(value);
            runtimeMaterials[regolith].SetFloat("_DetailStrength", value ? 1 : 0);
            foreach (Material material in runtimeMaterials.Values)
            {
                material.SetFloat("_LocalTerrainReady", value && surface != null ? 1 : 0);
                material.SetFloat("_ImpactReady", value && surface != null ? 1 : 0);
            }
            Detailed = value;
            Physics.SyncTransforms();
        }

        public void SetSun(float elevation)
        {
            SunElevation = Mathf.Clamp(elevation, 0.1f, 89.9f);
            sun.transform.localRotation = Quaternion.Euler(SunElevation, 120, 0);
        }

        public void SetLunarLighting(bool value)
        {
            if (!initialized) return;
            foreach (Material material in runtimeMaterials.Values)
            {
                material.SetFloat("_LunarLighting", value ? 1 : 0);
                if (value) material.EnableKeyword("_MOON_LUNAR_LIGHTING");
                else material.DisableKeyword("_MOON_LUNAR_LIGHTING");
            }
            LunarLighting = value;
        }

        private void OnDestroy()
        {
            if (!initialized) return;
            surface?.Dispose();
            surface = null;
            if (surroundingTerrain)
            {
                surroundingTerrain.terrainData = sourceTerrainData;
                TerrainCollider collider = surroundingTerrain.GetComponent<TerrainCollider>();
                if (collider) collider.terrainData = sourceTerrainData;
            }
            foreach (Terrain terrain in terrains)
            {
                if (!terrain) continue;
                foreach (var pair in runtimeMaterials)
                    if (terrain.materialTemplate == pair.Value) terrain.materialTemplate = pair.Key;
            }
            foreach (var original in originalRenderers)
                if (original.renderer) original.renderer.sharedMaterials = original.materials;
            foreach (Material material in runtimeMaterials.Values) Destroy(material);
            Destroy(runtimeTerrainData);
        }
    }
}
