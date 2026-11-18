// 光环读条使用的美术资源配置。HaloProgressRing 读取这里的花瓣、弧线、分格贴图和主颜色。
using UnityEngine;

namespace Unity.MP_FPS.Client {
    public sealed class HaloProgressArtwork : ScriptableObject {
        public Sprite Petal, OuterArc, InnerArc, Cell;
        public Color Color = new(1f, .18f, .42f, 1);
    }
}