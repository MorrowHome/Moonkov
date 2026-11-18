// 给 EnergyGrenadeFeedback 选外壳网格、轨迹和火花材质，以及投掷、引爆的音效资源。调整外观和声音可以改这里引用的资源。
using UnityEngine;

namespace Unity.MP_FPS {
    public sealed class EnergyGrenadeStyle : ScriptableObject {
        public Mesh Sphere;
        public Material Orb, Ribbon, Spark;
        public SoundDef ThrowSound, ExplosionSound;
    }
}