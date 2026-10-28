using UnityEngine;

namespace Unity.MP_FPS.Moon
{
    /// <summary>The fixed source environment. This asset owns no gameplay power state.</summary>
    [PreferBinarySerialization]
    [CreateAssetMenu(menuName = "Moon/Source World Lighting")]
    public sealed class SourceWorldLightingData : ScriptableObject
    {
        public string sourceSha256;
        public string statisticsRevision, characterProbeRevision;
        public Texture2D[] irradiance;
        public Texture2DArray surfaceStatistics;
        public LightProbes characterProbes;
        public Material displayTransform;
        public Cubemap fallbackReflection;
        public SourceWorldReflectionData reflections;
        public Color linearBackground = new Color(.0024f, .003f, .0048f, 1);
        public Vector3 towardSun;
        public Color linearSunColor = new Color(.8f, .88f, 1, 1);
        public float sourceSunEnergy = 2.2f;
        [Range(1, 16)] public int specularSamples = 4;
        [Min(1)] public float diffuseSamples = 64;
    }
}
