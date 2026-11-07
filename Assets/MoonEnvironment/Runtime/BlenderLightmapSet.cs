using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

namespace Unity.MP_FPS.Moon
{
    /// <summary>Applies scene-linear, non-directional lightmaps baked against the imported mesh UVs.</summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class BlenderLightmapSet : MonoBehaviour
    {
        [Serializable]
        public struct Binding
        {
            public MeshRenderer renderer;
            public int atlas;
        }

        [SerializeField] private Texture2D[] irradiance = Array.Empty<Texture2D>();
        [SerializeField] private Binding[] bindings = Array.Empty<Binding>();
        [SerializeField] private LightProbes probes;
        private LightmapData[] previousMaps;
        private LightmapsMode previousMode;
        private LightProbes previousProbes;

        public void Configure(Texture2D[] maps, Binding[] renderers, LightProbes characterProbes)
        {
            Restore();
            irradiance = maps;
            bindings = renderers;
            probes = characterProbes;
            if (isActiveAndEnabled) Apply();
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
#if UNITY_EDITOR
            EditorSceneManager.sceneOpened += OnSceneOpened;
            EditorSceneManager.activeSceneChangedInEditMode += OnActiveSceneChanged;
#endif
            Apply();
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene == gameObject.scene) Apply();
        }

#if UNITY_EDITOR
        private void OnSceneOpened(Scene scene, OpenSceneMode mode)
        {
            if (scene == gameObject.scene) Apply();
        }

        private void OnActiveSceneChanged(Scene previous, Scene current)
        {
            if (current == gameObject.scene) Apply();
        }
#endif

        private void Apply()
        {
            if (irradiance.Length == 0 || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return;
            var maps = new LightmapData[irradiance.Length];
            for (int i = 0; i < maps.Length; i++)
            {
                if (!irradiance[i]) throw new InvalidOperationException($"Missing Blender lightmap {i}.");
                maps[i] = new LightmapData { lightmapColor = irradiance[i] };
            }
            foreach (var binding in bindings)
                if (!binding.renderer || binding.atlas < 0 || binding.atlas >= maps.Length)
                    throw new InvalidOperationException("Invalid Blender lightmap renderer binding.");
            if (previousMaps == null)
            {
                previousMaps = LightmapSettings.lightmaps;
                previousMode = LightmapSettings.lightmapsMode;
                previousProbes = LightmapSettings.lightProbes;
            }
            LightmapSettings.lightmapsMode = LightmapsMode.NonDirectional;
            LightmapSettings.lightmaps = maps;
            if (probes) LightmapSettings.lightProbes = probes;
            foreach (var binding in bindings)
            {
                binding.renderer.lightmapIndex = binding.atlas;
                binding.renderer.lightmapScaleOffset = new Vector4(1, 1, 0, 0);
            }
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
#if UNITY_EDITOR
            EditorSceneManager.sceneOpened -= OnSceneOpened;
            EditorSceneManager.activeSceneChangedInEditMode -= OnActiveSceneChanged;
#endif
            Restore();
        }

        private void Restore()
        {
            if (previousMaps == null) return;
            var current = LightmapSettings.lightmaps;
            if (current.Length == irradiance.Length && current.Length != 0 && current[0].lightmapColor == irradiance[0])
            {
                LightmapSettings.lightmapsMode = previousMode;
                LightmapSettings.lightmaps = previousMaps;
            }
            if (probes && LightmapSettings.lightProbes == probes) LightmapSettings.lightProbes = previousProbes;
            previousMaps = null;
            previousProbes = null;
        }
    }
}
