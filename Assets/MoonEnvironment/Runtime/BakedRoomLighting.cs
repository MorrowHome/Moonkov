using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Unity.MP_FPS.Moon
{
    /// <summary>
    /// Two baked states for one isolated room sample. Owns the GLOBAL lightmaps,
    /// probes and Shadowmask quality setting while applied. This is not a manager
    /// for independently switched rooms or additively loaded lighting scenarios.
    /// Mixed key lights use Distance Shadowmask so nearby buildings and characters
    /// both cast and receive realtime shadows. Baked fill lights use probes on characters.
    /// </summary>
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class BakedRoomLighting : MonoBehaviour
    {
        [Serializable] public sealed class Map
        {
            public Texture2D color, direction, shadowMask;
        }

        [Serializable] public sealed class RendererBinding
        {
            public MeshRenderer renderer;
            public int lightmapIndex;
            public Vector4 scaleOffset;
        }

        [Serializable] public sealed class MaterialBinding
        {
            public Renderer renderer;
            public Material[] materials;
        }

        [Serializable] public sealed class MixedLightBinding
        {
            public Light light;
            public bool enabled, isBaked;
            public LightmapBakeType bakeType;
            public MixedLightingMode mixedMode;
            public int occlusionMaskChannel, probeOcclusionLightIndex;

            public void Apply()
            {
                if (!light) return;
                light.bakingOutput = new LightBakingOutput
                {
                    isBaked = isBaked, lightmapBakeType = bakeType,
                    mixedLightingMode = mixedMode, occlusionMaskChannel = occlusionMaskChannel,
                    probeOcclusionLightIndex = probeOcclusionLightIndex
                };
                light.enabled = enabled;
            }
        }

        [Serializable] public sealed class State
        {
            public bool captured;
            public Map[] maps = Array.Empty<Map>();
            public LightmapsMode lightmapsMode;
            public RendererBinding[] renderers = Array.Empty<RendererBinding>();
            public MixedLightBinding[] lights = Array.Empty<MixedLightBinding>();
            public MaterialBinding[] emission = Array.Empty<MaterialBinding>();
            // The complete native asset also preserves probe occlusion/channel data.
            // SphericalHarmonicsL2 has private fields and is not Unity-serializable;
            // its coefficients are persisted inside this LightProbes asset instead.
            public LightProbes probes;
            public SphericalHarmonicsL2[] ProbeCoefficients => probes
                ? probes.bakedProbes : Array.Empty<SphericalHarmonicsL2>();
        }

        [SerializeField] private Transform roomRoot;
        [SerializeField] private Light[] mixedLights = Array.Empty<Light>();
        [SerializeField] private Renderer[] emissiveRenderers = Array.Empty<Renderer>();
        [SerializeField] private State onState, offState;
        [SerializeField] private bool powerOn = true;
        [SerializeField] private RenderPipelineAsset trialPipeline;
        [SerializeField] private Transform shadowWitness;
        [SerializeField] private bool showTrialControls;
        [NonSerialized] private State restoreState;
        [NonSerialized] private ShadowmaskMode restoreShadowmaskMode;
        [NonSerialized] private RenderPipelineAsset restorePipeline;
        private static BakedRoomLighting owner;

        public bool PowerOn => powerOn;
        public bool HasBothStates => onState != null && onState.captured && offState != null && offState.captured;
        public State OnState => onState;
        public State OffState => offState;

        public void ConfigureTrial(RenderPipelineAsset pipeline, Transform witness)
        {
            trialPipeline = pipeline;
            shadowWitness = witness;
            showTrialControls = true;
        }

        public void Configure(Transform root, Light[] heroes, Renderer[] emission, bool initiallyOn = true)
        {
            if (!root) throw new ArgumentNullException(nameof(root));
            if (heroes == null || heroes.Length == 0 || heroes.Length > 4)
                throw new ArgumentException("The isolated sample requires one to four Mixed hero lights.", nameof(heroes));
            Restore();
            roomRoot = root;
            mixedLights = (Light[])heroes.Clone();
            emissiveRenderers = emission == null ? Array.Empty<Renderer>() : (Renderer[])emission.Clone();
            onState = offState = null;
            powerOn = initiallyOn;
        }

        private void OnEnable()
        {
            if (Application.isPlaying && HasBothStates) SetPowered(powerOn);
        }

        private void OnDisable() => Restore();
        private void OnDestroy() => Restore();

        public bool SetPowered(bool on)
        {
            if (!isActiveAndEnabled || !roomRoot || !HasBothStates || owner && owner != this) return false;
            if (restoreState == null)
            {
                restoreState = ReadCurrent();
                restoreShadowmaskMode = QualitySettings.shadowmaskMode;
                restorePipeline = QualitySettings.renderPipeline;
                owner = this;
            }
            if (trialPipeline) QualitySettings.renderPipeline = trialPipeline;
            // Ordinary Shadowmask removes baked static casters from realtime maps:
            // probes cannot reproduce a wall/door shadow boundary on a moving character.
            QualitySettings.shadowmaskMode = ShadowmaskMode.DistanceShadowmask;
            Apply(on ? onState : offState);
            powerOn = on;
            return true;
        }

        public void Restore()
        {
            if (restoreState != null)
            {
                Apply(restoreState);
                QualitySettings.shadowmaskMode = restoreShadowmaskMode;
                QualitySettings.renderPipeline = restorePipeline;
                restoreState = null;
            }
            if (owner == this) owner = null;
        }

        private void OnGUI()
        {
            if (!Application.isPlaying || !showTrialControls || !HasBothStates) return;
            GUILayout.BeginArea(new Rect(12, 12, 230, 110), GUI.skin.box);
            GUILayout.Label("G01 baked lighting trial");
            if (GUILayout.Button(powerOn ? "Circuit ON - click to switch OFF" : "Circuit OFF - click to switch ON"))
                SetPowered(!powerOn);
            if (shadowWitness && GUILayout.Button("Move character / realtime shadow"))
            {
                var position = shadowWitness.position;
                position.x = position.x < 0 ? 1 : -1;
                shadowWitness.position = position;
            }
            GUILayout.EndArea();
        }

        private State ReadCurrent()
        {
            var data = LightmapSettings.lightmaps;
            var renderers = roomRoot.GetComponentsInChildren<MeshRenderer>(true);
            var state = new State
            {
                captured = true,
                maps = new Map[data.Length], lightmapsMode = LightmapSettings.lightmapsMode,
                probes = LightmapSettings.lightProbes,
                renderers = new RendererBinding[renderers.Length],
                lights = new MixedLightBinding[mixedLights.Length],
                emission = new MaterialBinding[emissiveRenderers.Length]
            };
            for (int i = 0; i < data.Length; i++)
                state.maps[i] = new Map { color = data[i].lightmapColor, direction = data[i].lightmapDir, shadowMask = data[i].shadowMask };
            // MeshRenderer only: characters/SkinnedMeshRenderers never get a baked index.
            for (int i = 0; i < renderers.Length; i++)
                state.renderers[i] = new RendererBinding { renderer = renderers[i], lightmapIndex = renderers[i].lightmapIndex, scaleOffset = renderers[i].lightmapScaleOffset };
            for (int i = 0; i < mixedLights.Length; i++)
            {
                var light = mixedLights[i];
                var output = light ? light.bakingOutput : default;
                state.lights[i] = new MixedLightBinding
                {
                    light = light, enabled = light && light.enabled, isBaked = output.isBaked,
                    bakeType = output.lightmapBakeType, mixedMode = output.mixedLightingMode,
                    occlusionMaskChannel = output.occlusionMaskChannel,
                    probeOcclusionLightIndex = output.probeOcclusionLightIndex
                };
            }
            for (int i = 0; i < emissiveRenderers.Length; i++)
            {
                var renderer = emissiveRenderers[i];
                state.emission[i] = new MaterialBinding { renderer = renderer, materials = renderer ? renderer.sharedMaterials : Array.Empty<Material>() };
            }
            return state;
        }

        private static void Apply(State state)
        {
            var maps = new LightmapData[state.maps.Length];
            for (int i = 0; i < maps.Length; i++)
                maps[i] = new LightmapData { lightmapColor = state.maps[i].color, lightmapDir = state.maps[i].direction, shadowMask = state.maps[i].shadowMask };
            LightmapSettings.lightmapsMode = state.lightmapsMode;
            LightmapSettings.lightmaps = maps;
            LightmapSettings.lightProbes = state.probes;
            foreach (var binding in state.renderers)
                if (binding.renderer)
                {
                    binding.renderer.lightmapIndex = binding.lightmapIndex;
                    binding.renderer.lightmapScaleOffset = binding.scaleOffset;
                }
            foreach (var binding in state.emission)
                if (binding.renderer) binding.renderer.sharedMaterials = binding.materials;
            foreach (var binding in state.lights) binding.Apply();
        }

#if UNITY_EDITOR
        /// <summary>
        /// Call after each completed bake, with the intended hero enable states and
        /// emission materials already assigned. Copies textures/probes so the next
        /// bake cannot overwrite this state. The optional folder must be under Assets.
        /// </summary>
        public void CaptureState(bool on, string outputFolder = null)
        {
            if (Application.isPlaying || restoreState != null || !roomRoot)
                throw new InvalidOperationException("Capture in Edit Mode with a configured room and no applied preview (call Restore first).");
            var state = ReadCurrent();
            if (state.maps.Length == 0 || !state.probes || state.probes.count == 0)
                throw new InvalidOperationException("Bake lightmaps and Light Probes before capturing the room.");
            if (on)
                foreach (var light in state.lights)
                    if (!light.light || !light.enabled || !light.isBaked || light.bakeType != LightmapBakeType.Mixed ||
                        light.mixedMode != MixedLightingMode.Shadowmask || light.occlusionMaskChannel < 0 || light.occlusionMaskChannel > 3 ||
                        light.light.shadows == LightShadows.None)
                        throw new InvalidOperationException("Each on-state hero must be an enabled, successfully baked Mixed Shadowmask light with realtime shadows and a valid mask channel.");
            if (!on)
                foreach (var light in state.lights)
                    if (light.enabled) throw new InvalidOperationException("Disable the hero lights before capturing the off state.");
            if (string.IsNullOrEmpty(outputFolder))
            {
                string scenePath = gameObject.scene.path;
                if (string.IsNullOrEmpty(scenePath)) throw new InvalidOperationException("Save the sample scene before capture.");
                outputFolder = System.IO.Path.GetDirectoryName(scenePath).Replace('\\', '/') + "/" + gameObject.scene.name + (on ? "_LightingOn" : "_LightingOff");
            }
            outputFolder = outputFolder.Replace('\\', '/').TrimEnd('/');
            if (!outputFolder.StartsWith("Assets/", StringComparison.Ordinal) || outputFolder.Contains(".."))
                throw new ArgumentException("Capture folder must be within Assets.", nameof(outputFolder));
            outputFolder = UnityEditor.AssetDatabase.GenerateUniqueAssetPath(outputFolder);
            CreateFolder(outputFolder);
            for (int i = 0; i < state.maps.Length; i++)
            {
                state.maps[i].color = CopyTexture(state.maps[i].color, outputFolder, i + "_Color");
                state.maps[i].direction = CopyTexture(state.maps[i].direction, outputFolder, i + "_Direction");
                state.maps[i].shadowMask = CopyTexture(state.maps[i].shadowMask, outputFolder, i + "_ShadowMask");
            }
            state.probes = Instantiate(state.probes);
            state.probes.name = on ? "Room On Probes" : "Room Off Probes";
            state.probes.hideFlags = HideFlags.None;
            UnityEditor.AssetDatabase.CreateAsset(state.probes, outputFolder + "/LightProbes.asset");
            if (on) onState = state; else offState = state;
            UnityEditor.EditorUtility.SetDirty(this);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
            UnityEditor.AssetDatabase.SaveAssets();
        }

        private static Texture2D CopyTexture(Texture2D texture, string folder, string name)
        {
            if (!texture) return null;
            string source = UnityEditor.AssetDatabase.GetAssetPath(texture);
            string path = folder + "/" + name + System.IO.Path.GetExtension(source);
            if (string.IsNullOrEmpty(source) || !UnityEditor.AssetDatabase.CopyAsset(source, path))
                throw new InvalidOperationException("Could not preserve baked texture: " + texture.name);
            return UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static void CreateFolder(string path)
        {
            if (UnityEditor.AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            CreateFolder(parent);
            UnityEditor.AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }
#endif
    }
}
