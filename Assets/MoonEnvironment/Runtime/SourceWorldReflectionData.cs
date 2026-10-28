using System;
using UnityEngine;

namespace Unity.MP_FPS.Moon
{
    [PreferBinarySerialization]
    public sealed class SourceWorldReflectionData : ScriptableObject
    {
        [Serializable] public sealed class Probe
        {
            public Vector3 position, boxMin, boxMax;
            public int importance;
            public bool sunVisible;
            public float[] sphericalHarmonics, characterSphericalHarmonics;
        }
        public string sourceSha256;
        public Cubemap[] cubemaps;
        public Probe[] probes;
    }
}
