using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Unity.MP_FPS.Moon
{
    /// <summary>
    /// Sole global lightmap/probe owner for one building. Room switches compose
    /// baked irradiance once; key lights keep realtime direct light and full shadows.
    /// All input atlases and the receiving shaders must use linear full-HDR lightmaps.
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class BuildingBakedLighting : MonoBehaviour
    {
        [Serializable] public sealed class RendererBinding
        {
            public MeshRenderer renderer;
            public int lightmapIndex;
            public Vector4 scaleOffset;
        }

        [Serializable] public sealed class Circuit
        {
            public string id;
            public Light[] fixtures = Array.Empty<Light>();
            public Light[] realtimeLights = Array.Empty<Light>();
            public ReflectionProbe[] reflections = Array.Empty<ReflectionProbe>();
        }

        private struct LightSnapshot
        {
            public Light light;
            public bool enabled;
            public LightBakingOutput bakingOutput;
        }

        private struct ReflectionSnapshot
        {
            public ReflectionProbe probe;
            public float intensity;
        }

        [SerializeField] private BuildingBakedLightingData data;
        [SerializeField] private RendererBinding[] renderers = Array.Empty<RendererBinding>();
        [SerializeField] private Circuit[] circuits = Array.Empty<Circuit>();
        [SerializeField] private ComputeShader composer;

        [NonSerialized] private bool initialized, valid, applied;
        [NonSerialized] private bool[] powered;
        [NonSerialized] private BuildingBakedLightingData.State[] roomStates;
        [NonSerialized] private SphericalHarmonicsL2[] baselineSH, environmentSH, combinedSH;
        [NonSerialized] private SphericalHarmonicsL2[][] roomSH;
        [NonSerialized] private LightmapData[] previousMaps;
        [NonSerialized] private LightmapsMode previousMode;
        [NonSerialized] private LightProbes previousProbes, composedProbes;
        [NonSerialized] private RendererBinding[] previousRenderers;
        [NonSerialized] private LightSnapshot[] previousLights;
        [NonSerialized] private ReflectionSnapshot[] previousReflections;
        [NonSerialized] private Texture2D[] outputMaps;
        [NonSerialized] private RenderTexture[] workMaps;
        [NonSerialized] private int initializeKernel, accumulateKernel, finalizeKernel;
        private readonly Dictionary<Light, int> fixtureRooms = new Dictionary<Light, int>();
        private readonly HashSet<Light> realtimeLights = new HashSet<Light>();
        private readonly Dictionary<ReflectionProbe, int> reflectionRooms = new Dictionary<ReflectionProbe, int>();
        private static BuildingBakedLighting owner;

        public event Action PowerChanged;
        public string ConfigurationError { get; private set; }
        public bool IsApplied => applied;
        public BuildingBakedLightingData Data => data;

        public void Configure(BuildingBakedLightingData lightingData, RendererBinding[] bindings,
            Circuit[] roomCircuits, ComputeShader computeShader)
        {
            Restore();
            data = lightingData;
            renderers = bindings ?? Array.Empty<RendererBinding>();
            circuits = roomCircuits ?? Array.Empty<Circuit>();
            composer = computeShader;
            initialized = valid = false;
            powered = null;
            if (Application.isPlaying && isActiveAndEnabled) Apply();
        }

        private void OnEnable()
        {
            if (Application.isPlaying && data) Apply();
        }

        private void OnDisable() => Restore();
        private void OnDestroy() => Restore();

        private bool Fail(string error)
        {
            ConfigurationError = error;
            return false;
        }

        private bool Initialize()
        {
            if (initialized) return valid;
            initialized = true;
            valid = false;
            fixtureRooms.Clear();
            realtimeLights.Clear();
            reflectionRooms.Clear();
            if (!data || !composer || data.baseline == null || data.baseline.colorMaps == null ||
                data.baseline.colorMaps.Length == 0 || data.rooms == null || data.rooms.Length == 0 ||
                circuits.Length != data.rooms.Length)
                return Fail("Missing lighting data/composer, or room circuit count does not match the bake.");
            if (!data.baseline.probes || data.baseline.probes.count == 0)
                return Fail("The baseline must contain baked character light probes.");

            var baselineMaps = data.baseline.colorMaps;
            foreach (var map in baselineMaps)
                if (!IsLinearHDR(map)) return Fail("Lightmap inputs must be linear BC6H, RGBAHalf or RGBAFloat textures.");
            var roomById = new Dictionary<string, BuildingBakedLightingData.State>(StringComparer.Ordinal);
            foreach (var room in data.rooms)
            {
                if (room == null || string.IsNullOrWhiteSpace(room.id) || room.onState == null ||
                    !roomById.TryAdd(room.id, room.onState)) return Fail("Invalid or duplicate baked room ID.");
            }

            var positions = data.baseline.probes.positions;
            baselineSH = data.baseline.probes.bakedProbes;
            if (baselineSH.Length != positions.Length) return Fail("Invalid baseline probe coefficients.");
            var environment = data.environment;
            if (environment == null || environment.colorMaps == null ||
                environment.colorMaps.Length != baselineMaps.Length || !environment.probes)
                return Fail("The environment must contain complete baked atlases and character light probes.");
            for (int i = 0; i < baselineMaps.Length; i++)
            {
                var map = environment.colorMaps[i];
                if (!IsLinearHDR(map) || map.format != baselineMaps[i].format ||
                    map.width != baselineMaps[i].width || map.height != baselineMaps[i].height)
                    return Fail($"Environment atlas {i} differs from the baseline layout or format.");
            }
            var environmentPositions = environment.probes.positions;
            environmentSH = environment.probes.bakedProbes;
            if (environmentPositions.Length != positions.Length || environmentSH.Length != baselineSH.Length)
                return Fail("Environment has a different probe count.");
            for (int i = 0; i < positions.Length; i++)
                if ((environmentPositions[i] - positions[i]).sqrMagnitude > .000001f)
                    return Fail("Environment has a different probe layout.");
            roomStates = new BuildingBakedLightingData.State[circuits.Length];
            roomSH = new SphericalHarmonicsL2[circuits.Length][];
            var circuitIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < circuits.Length; i++)
            {
                var circuit = circuits[i];
                if (circuit == null || string.IsNullOrWhiteSpace(circuit.id) || !circuitIds.Add(circuit.id) ||
                    !roomById.TryGetValue(circuit.id, out var state) ||
                    circuit.fixtures == null || circuit.fixtures.Length == 0 || circuit.realtimeLights == null)
                    return Fail("Invalid room circuit or missing room lighting basis.");
                if (state.colorMaps == null || state.colorMaps.Length != baselineMaps.Length || !state.probes)
                    return Fail($"Room {circuit.id} has an incomplete atlas/probe basis.");
                for (int j = 0; j < baselineMaps.Length; j++)
                {
                    var map = state.colorMaps[j];
                    if (!IsLinearHDR(map) || map.width != baselineMaps[j].width || map.height != baselineMaps[j].height)
                        return Fail($"Room {circuit.id} atlas {j} differs from the baseline layout or format.");
                }
                var roomPositions = state.probes.positions;
                var coefficients = state.probes.bakedProbes;
                if (roomPositions.Length != positions.Length || coefficients.Length != baselineSH.Length)
                    return Fail($"Room {circuit.id} has a different probe count.");
                for (int j = 0; j < positions.Length; j++)
                    if ((roomPositions[j] - positions[j]).sqrMagnitude > .000001f)
                        return Fail($"Room {circuit.id} has a different probe layout.");
                foreach (var fixture in circuit.fixtures)
                    if (!fixture || !fixtureRooms.TryAdd(fixture, i))
                        return Fail("A fixture is missing or assigned to multiple room circuits.");
                foreach (var light in circuit.realtimeLights)
                {
                    if (!light || !fixtureRooms.TryGetValue(light, out int roomIndex) || roomIndex != i ||
                        !realtimeLights.Add(light) || light.shadows == LightShadows.None)
                        return Fail("Each key light must belong to its room and retain realtime shadows.");
                }
                foreach (var probe in circuit.reflections ?? Array.Empty<ReflectionProbe>())
                    if (!probe || !reflectionRooms.TryAdd(probe, i))
                        return Fail("A reflection probe is missing or assigned more than once across room circuits.");
                roomStates[i] = state;
                roomSH[i] = coefficients;
            }
            if (renderers.Length == 0) return Fail("No baked geometry bindings.");
            var boundRenderers = new HashSet<MeshRenderer>();
            foreach (var binding in renderers)
                if (binding == null || !binding.renderer || !boundRenderers.Add(binding.renderer) ||
                    binding.lightmapIndex < 0 || binding.lightmapIndex >= baselineMaps.Length)
                    return Fail("Invalid, duplicate or out-of-range baked geometry binding.");
            powered = new bool[circuits.Length];
            for (int i = 0; i < powered.Length; i++) powered[i] = true;
            combinedSH = new SphericalHarmonicsL2[baselineSH.Length];
            ConfigurationError = null;
            return valid = true;
        }

        private static bool IsLinearHDR(Texture2D texture) => texture &&
            (texture.format == TextureFormat.BC6H || texture.format == TextureFormat.RGBAHalf ||
             texture.format == TextureFormat.RGBAFloat);

        public bool TryGetPowered(Light fixture, out bool on)
        {
            on = false;
            if (!Initialize() || !fixture || !fixtureRooms.TryGetValue(fixture, out int index)) return false;
            on = powered[index];
            return true;
        }

        public bool SetPoweredBatch(Light[] fixtures, bool on)
        {
            if (!isActiveAndEnabled || !Initialize() || fixtures == null || fixtures.Length == 0) return false;
            // Resolve the entire request before touching either lights or baked data.
            var rooms = new HashSet<int>();
            foreach (var fixture in fixtures)
            {
                if (!fixture || !fixtureRooms.TryGetValue(fixture, out int room)) return false;
                rooms.Add(room);
            }
            if (!applied && !Apply()) return false;
            var previousPower = (bool[])powered.Clone();
            bool changed = false;
            foreach (int room in rooms)
            {
                changed |= powered[room] != on;
                powered[room] = on;
            }
            if (!changed) return true;
            try
            {
                Compose();
                ApplyFixtureStates();
            }
            catch (Exception exception)
            {
                powered = previousPower;
                Restore();
                return Fail("Room switch failed; restored original realtime lighting: " + exception.Message);
            }
            PowerChanged?.Invoke();
            return true;
        }

        public bool Apply()
        {
            if (applied) return true;
            if (!isActiveAndEnabled || !Initialize()) return false;
            if (owner && owner != this) return Fail("Another building already owns global lightmaps/probes.");
            if (!SystemInfo.supportsComputeShaders || !SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf) ||
                !SystemInfo.SupportsRandomWriteOnRenderTextureFormat(RenderTextureFormat.ARGBHalf) ||
                (SystemInfo.copyTextureSupport & CopyTextureSupport.RTToTexture) == 0)
                return Fail("Building lightmap composition requires compute shaders and HDR RT-to-texture copies.");
            try
            {
                initializeKernel = composer.FindKernel("Initialize");
                accumulateKernel = composer.FindKernel("Accumulate");
                finalizeKernel = composer.FindKernel("Finalize");
                SavePrevious();
                owner = this;
                applied = true;
                AllocateOutputs();
                var maps = new LightmapData[outputMaps.Length];
                for (int i = 0; i < maps.Length; i++) maps[i] = new LightmapData { lightmapColor = outputMaps[i] };
                LightmapSettings.lightmapsMode = LightmapsMode.NonDirectional;
                LightmapSettings.lightmaps = maps;
                LightmapSettings.lightProbes = composedProbes;
                // Unity updates SH on the active probe set. Writing the clone
                // before binding it leaves its environment-only coefficients
                // active until the first room switch triggers another Compose.
                Compose();
                foreach (var binding in renderers)
                {
                    binding.renderer.lightmapIndex = binding.lightmapIndex;
                    binding.renderer.lightmapScaleOffset = binding.scaleOffset;
                }
                ApplyFixtureStates();
                ConfigurationError = null;
                PowerChanged?.Invoke();
                return true;
            }
            catch (Exception exception)
            {
                Restore();
                return Fail("Could not compose building lighting: " + exception.Message);
            }
        }

        private void SavePrevious()
        {
            previousMaps = LightmapSettings.lightmaps;
            previousMode = LightmapSettings.lightmapsMode;
            previousProbes = LightmapSettings.lightProbes;
            previousRenderers = new RendererBinding[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i].renderer;
                previousRenderers[i] = new RendererBinding
                {
                    renderer = renderer, lightmapIndex = renderer.lightmapIndex, scaleOffset = renderer.lightmapScaleOffset
                };
            }
            previousLights = new LightSnapshot[fixtureRooms.Count];
            int index = 0;
            foreach (var pair in fixtureRooms)
                previousLights[index++] = new LightSnapshot
                { light = pair.Key, enabled = pair.Key.enabled, bakingOutput = pair.Key.bakingOutput };
            previousReflections = new ReflectionSnapshot[reflectionRooms.Count];
            index = 0;
            foreach (var pair in reflectionRooms)
                previousReflections[index++] = new ReflectionSnapshot
                { probe = pair.Key, intensity = pair.Key.intensity };
        }

        private void AllocateOutputs()
        {
            var maps = data.baseline.colorMaps;
            outputMaps = new Texture2D[maps.Length];
            workMaps = new RenderTexture[maps.Length];
            for (int i = 0; i < maps.Length; i++)
            {
                var source = maps[i];
                var work = new RenderTexture(source.width, source.height, 0, RenderTextureFormat.ARGBHalf,
                    RenderTextureReadWrite.Linear)
                {
                    name = $"{name} Lighting Work {i}", enableRandomWrite = true,
                    useMipMap = true, autoGenerateMips = false,
                    filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.HideAndDontSave
                };
                workMaps[i] = work;
                if (!work.Create()) throw new InvalidOperationException("Failed to create HDR lightmap render texture.");
                var output = new Texture2D(source.width, source.height, TextureFormat.RGBAHalf, true, true)
                {
                    name = $"{name} Composed Lightmap {i}", filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave
                };
                outputMaps[i] = output;
                output.Apply(false, true);
            }
            composedProbes = Instantiate(data.environment.probes);
            composedProbes.name = name + " Composed Light Probes";
            composedProbes.hideFlags = HideFlags.HideAndDontSave;
        }

        private void Compose()
        {
            for (int atlas = 0; atlas < workMaps.Length; atlas++)
            {
                var baseline = data.baseline.colorMaps[atlas];
                var result = workMaps[atlas];
                int groupsX = (result.width + 7) / 8, groupsY = (result.height + 7) / 8;
                composer.SetInts("_Size", result.width, result.height);
                composer.SetTexture(initializeKernel, "_Baseline", data.environment.colorMaps[atlas]);
                composer.SetTexture(initializeKernel, "_Result", result);
                composer.Dispatch(initializeKernel, groupsX, groupsY, 1);
                composer.SetTexture(accumulateKernel, "_Baseline", baseline);
                composer.SetTexture(accumulateKernel, "_Result", result);
                for (int room = 0; room < powered.Length; room++)
                {
                    if (!powered[room]) continue;
                    composer.SetTexture(accumulateKernel, "_Room", roomStates[room].colorMaps[atlas]);
                    composer.Dispatch(accumulateKernel, groupsX, groupsY, 1);
                }
                composer.SetTexture(finalizeKernel, "_Result", result);
                composer.Dispatch(finalizeKernel, groupsX, groupsY, 1);
                result.GenerateMips();
                Graphics.CopyTexture(result, outputMaps[atlas]);
            }
            Array.Copy(environmentSH, combinedSH, environmentSH.Length);
            for (int room = 0; room < powered.Length; room++)
            {
                if (!powered[room]) continue;
                for (int probe = 0; probe < combinedSH.Length; probe++)
                    for (int channel = 0; channel < 3; channel++)
                        for (int coefficient = 0; coefficient < 9; coefficient++)
                            combinedSH[probe][channel, coefficient] +=
                                roomSH[room][probe][channel, coefficient] - baselineSH[probe][channel, coefficient];
            }
            composedProbes.bakedProbes = combinedSH;
        }

        private void ApplyFixtureStates()
        {
            foreach (var pair in fixtureRooms)
            {
                var light = pair.Key;
                if (!light) continue;
                bool realtime = realtimeLights.Contains(light);
                light.bakingOutput = new LightBakingOutput
                {
                    isBaked = true, lightmapBakeType = realtime ? LightmapBakeType.Mixed : LightmapBakeType.Baked,
                    mixedLightingMode = MixedLightingMode.IndirectOnly,
                    occlusionMaskChannel = -1, probeOcclusionLightIndex = -1
                };
                light.enabled = realtime && powered[pair.Value];
            }
            foreach (var pair in reflectionRooms)
                // Keep the probe volume active when dark, so another room's
                // bright cubemap does not become this room's fallback reflection.
                if (pair.Key) pair.Key.intensity = powered[pair.Value] ? 1f : 0f;
        }

        public void Restore()
        {
            if (applied)
            {
                LightmapSettings.lightmapsMode = previousMode;
                LightmapSettings.lightmaps = previousMaps;
                LightmapSettings.lightProbes = previousProbes;
                foreach (var binding in previousRenderers)
                    if (binding.renderer)
                    {
                        binding.renderer.lightmapIndex = binding.lightmapIndex;
                        binding.renderer.lightmapScaleOffset = binding.scaleOffset;
                    }
                foreach (var snapshot in previousLights)
                    if (snapshot.light)
                    {
                        snapshot.light.bakingOutput = snapshot.bakingOutput;
                        snapshot.light.enabled = snapshot.enabled;
                    }
                foreach (var snapshot in previousReflections)
                    if (snapshot.probe) snapshot.probe.intensity = snapshot.intensity;
            }
            applied = false;
            if (owner == this) owner = null;
            if (workMaps != null)
                foreach (var work in workMaps)
                    if (work) { work.Release(); DestroyOwned(work); }
            if (outputMaps != null)
                foreach (var output in outputMaps) if (output) DestroyOwned(output);
            if (composedProbes) DestroyOwned(composedProbes);
            workMaps = null;
            outputMaps = null;
            composedProbes = null;
            previousMaps = null;
            previousProbes = null;
            previousRenderers = null;
            previousLights = null;
            previousReflections = null;
        }

        private static void DestroyOwned(UnityEngine.Object value)
        {
            if (Application.isPlaying) Destroy(value);
            else DestroyImmediate(value);
        }
    }
}
