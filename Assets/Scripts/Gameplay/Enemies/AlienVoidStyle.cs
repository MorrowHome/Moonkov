// 异形材质的统一资源入口。身体、甲片和液体攻击向同一份资产取材质，外观需要替换时可以集中配置。
using UnityEngine;

namespace Unity.MP_FPS {
    // 用一个 ScriptableObject 把皮肤、甲片和喷吐等材质关联起来。Resources 路径使用的是它的资产实例。
    public sealed class AlienVoidStyle : ScriptableObject {
        public Material Skin, Plate, Filament, Droplet;
        public Mesh Sphere;
    }
}