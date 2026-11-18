// 把库存里的物品代码转成对应的模型预制体或图标。需要显示实物的地方查这里，共用外观的物品通过 ArtCode 复用同一资源。
using UnityEngine;
using Unity.MP_FPS.Inventory;

namespace Unity.MP_FPS {
    // Shared authored asset lookup; presentation never owns loot or collision authority.
    public static class PhysicalItemVisuals {
        public static bool IsPhysical(string code) {
            var definition = InventoryCatalog.Get(code);
            return definition != null && definition.Kind != ItemKind.Root && definition.Kind != ItemKind.LongGun &&
                   definition.Kind != ItemKind.Pistol && !code.StartsWith("halo_", System.StringComparison.Ordinal);
        }

        public static GameObject Create(string code, Transform parent, bool centered = false) {
            if (!IsPhysical(code)) return null;
            var prefab = Prefab(code);
            if (!prefab) return null;
            var instance = Object.Instantiate(prefab, parent, false);
            if (centered) {
                var renderers = instance.GetComponentsInChildren<Renderer>();
                if (renderers.Length > 0) {
                    var bounds = renderers[0].bounds;
                    foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
                    instance.transform.position -= bounds.center - instance.transform.position;
                }
            }
            return instance;
        }

        public static GameObject Prefab(string code) {
            return IsPhysical(code) ? Resources.Load<GameObject>("Moonkov/Items/Prefabs/" + ArtCode(code)) : null;
        }

        public static Texture2D Icon(string code) {
            return IsPhysical(code) ? Resources.Load<Texture2D>("Moonkov/Items/Icons/" + ArtCode(code)) : null;
        }

        private static string ArtCode(string code) {
            return code switch {
                ResearchQuestRules.Sample or ResearchQuestRules.SampleKit => "helium3_sample",
                ResearchQuestRules.Package => "black_box",
                ResearchQuestRules.NadirRecords => ResearchQuestRules.Tablet,
                ResearchQuestRules.Credentials => ResearchQuestRules.Flash,
                ResearchQuestRules.OriginalArchive => "mc00_archive_drive",
                _ => code
            };
        }
    }
}