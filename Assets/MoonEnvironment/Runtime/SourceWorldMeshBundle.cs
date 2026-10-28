using UnityEngine;

namespace Unity.MP_FPS.Moon
{
    /// <summary>Binary mesh subassets preserve source vertex payload without huge text buffers.</summary>
    [PreferBinarySerialization]
    public sealed class SourceWorldMeshBundle : ScriptableObject
    {
        public Mesh[] meshes;
    }
}
