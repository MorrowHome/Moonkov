// 给 EnergyGrenadeFeedback 选外壳网格、轨迹和火花材质，以及投掷、引爆的音效资源。调整外观和声音可以改这里引用的资源。
using UnityEngine;

namespace Unity.MP_FPS {
    public sealed class EnergyGrenadeStyle : ScriptableObject {
        public Mesh Sphere;
        // 弹体、尾迹和爆炸火花各有各的材质。表现系统从这里一起取得声音资源，方便替换某一层而保持其他层一致。
        public Material Orb, Ribbon, Spark;
        public SoundDef ThrowSound, ExplosionSound;
    }
}