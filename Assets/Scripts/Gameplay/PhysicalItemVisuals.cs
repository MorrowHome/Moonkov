using UnityEngine;
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS
{
    // Shared authored asset lookup; presentation never owns loot or collision authority.
    public static class PhysicalItemVisuals
    {
        public static bool IsPhysical(string code)
        {
            var definition = InventoryCatalog.Get(code);
            return definition != null && definition.Kind != ItemKind.Root &&
                definition.Kind != ItemKind.LongGun && definition.Kind != ItemKind.Pistol &&
                !code.StartsWith("halo_", System.StringComparison.Ordinal);
        }

        public static GameObject Create(string code, Transform parent, bool centered = false)
        {
            if (!IsPhysical(code)) return null;
            var prefab = Resources.Load<GameObject>("Moonkov/Items/Prefabs/" + code);
            if (!prefab) return null;
            var instance = Object.Instantiate(prefab, parent, false);
            if (centered)
            {
                var renderers = instance.GetComponentsInChildren<Renderer>();
                if (renderers.Length > 0)
                {
                    var bounds = renderers[0].bounds;
                    foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
                    instance.transform.position -= bounds.center - instance.transform.position;
                }
            }
            return instance;
        }

        public static Texture2D Icon(string code) => IsPhysical(code)
            ? Resources.Load<Texture2D>("Moonkov/Items/Icons/" + code) : null;
    }
}
