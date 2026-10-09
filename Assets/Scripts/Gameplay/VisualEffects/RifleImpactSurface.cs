using UnityEngine;

namespace Unity.MP_FPS
{
    public enum RifleImpactKind : byte { Rock, Metal, Body }

    // Optional explicit surface override for authored structures and props.
    public sealed class RifleImpactSurface : MonoBehaviour
    {
        public RifleImpactKind Kind = RifleImpactKind.Rock;

        public static RifleImpactKind Classify(Collider collider)
        {
            if (collider == null) return RifleImpactKind.Rock;
            if (GhostGameObject.TryFindGhostGameObject(collider.gameObject, out var ghost) &&
                ghost.GetComponent<PlayerGhost>() != null) return RifleImpactKind.Body;
            var surface = collider.GetComponentInParent<RifleImpactSurface>();
            if (surface) return surface.Kind;
            if (collider is TerrainCollider) return RifleImpactKind.Rock;
            var material = collider.sharedMaterial;
            if (material && IsMetal(material.name)) return RifleImpactKind.Metal;
            var renderer = collider.GetComponent<Renderer>();
            return renderer && renderer.sharedMaterial && IsMetal(renderer.sharedMaterial.name)
                ? RifleImpactKind.Metal : RifleImpactKind.Rock;
        }

        private static bool IsMetal(string name) => name.IndexOf("metal", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("steel", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("alloy", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
