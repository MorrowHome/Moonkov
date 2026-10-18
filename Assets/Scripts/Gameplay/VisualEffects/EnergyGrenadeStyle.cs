using UnityEngine;

namespace Unity.MP_FPS
{
    public sealed class EnergyGrenadeStyle : ScriptableObject
    {
        public Mesh Sphere;
        public Material Orb, Ribbon, Spark;
        public SoundDef ThrowSound, ExplosionSound;
    }
}
