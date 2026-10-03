using UnityEngine;

namespace Unity.MP_FPS.Client
{
    public sealed class MoonDisplaySettings : ScriptableObject
    {
        public Mesh Mesh;
        public Material Material;
        public Quaternion SourceOrientation = Quaternion.identity;
        [Min(.001f)] public float DaysPerSecond = .05f;
        [Range(256, 1536)] public int BackgroundResolution = 768;
        [Range(256, 2048)] public int ObserverResolution = 1280;
    }
}
