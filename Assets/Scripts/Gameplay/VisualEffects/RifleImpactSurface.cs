// 把碰到的 Collider 归为岩石、金属或角色等表面。先识别角色，再读取手工设置的表面标记。其余情况按碰撞体和材质判断，命中和擦碰音效都会用到。
using UnityEngine;

namespace Unity.MP_FPS {
    public enum RifleImpactKind : byte {
        Rock,
        Metal,
        Body
    }

    // 场景中手工制作的结构与道具可显式指定表面类型。
    public sealed class RifleImpactSurface : MonoBehaviour {
        public RifleImpactKind Kind = RifleImpactKind.Rock;

        public static RifleImpactKind Classify(Collider collider) {
            if (collider == null) return RifleImpactKind.Rock;
            // 先沿碰撞体查角色对应的 Ghost。找到玩家后直接用身体命中反馈，衣料材质名不会让这发被认成墙。
            if (GhostGameObject.TryFindGhostGameObject(collider.gameObject, out var ghost) &&
                ghost.GetComponent<PlayerGhost>() != null)
                return RifleImpactKind.Body;
            var surface = collider.GetComponentInParent<RifleImpactSurface>();
            if (surface) return surface.Kind;
            if (collider is TerrainCollider) return RifleImpactKind.Rock;
            // 当没有手工标记时，先从物理材料找金属，再检查渲染材料。地形及没有金属证据的表面用岩石。
            var material = collider.sharedMaterial;
            if (material && IsMetal(material.name)) return RifleImpactKind.Metal;
            var renderer = collider.GetComponent<Renderer>();
            return renderer && renderer.sharedMaterial && IsMetal(renderer.sharedMaterial.name)
                ? RifleImpactKind.Metal
                : RifleImpactKind.Rock;
        }

        private static bool IsMetal(string name) {
            return name.IndexOf("metal", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("steel", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                   // 兼容 MC00 原材质名里的 aluminium 和 aluminum 两种拼写。
                   name.IndexOf("alumin", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("alloy", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}