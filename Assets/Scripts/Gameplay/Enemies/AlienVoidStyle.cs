// 异形材质的统一资源入口。身体、甲片和液体攻击向同一份资产取材质，外观需要替换时可以集中配置。
using UnityEngine;

namespace Unity.MP_FPS
{
    public sealed class AlienVoidStyle : ScriptableObject
    {
        public Material Skin, Plate, Filament, Droplet;
        public Mesh Sphere;
    }
}
