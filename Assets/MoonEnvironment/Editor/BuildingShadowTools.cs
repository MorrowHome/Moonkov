using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Unity.MP_FPS.Moon.Editor
{
    /// <summary>
    /// Authoring-only shadow simplification for modular imported buildings.
    /// Keep structural occluders and all visible geometry/colliders independently editable.
    /// No camera-distance changes and no runtime hierarchy scanning are introduced.
    /// </summary>
    public static class BuildingShadowTools
    {
        private static readonly string[] StructureNames =
        {
            "wall", "floor", "ceiling", "roof", "door", "window", "pane", "pressure",
            "partition", "bulkhead", "shell", "foundation", "stair", "tread", "hatch",
            "gate", "shutter", "column", "pillar", "beam"
        };
        private static readonly string[] ThinDetailNames =
        {
            "panelseam", "panel seam", "tread edge", "fastener strip", "route_dash",
            "route dash", "paint stripe", "painted marking", "decal"
        };

        [Serializable] public sealed class Result
        {
            public int renderers, castersBefore, castersAfter, smallDetailsChanged, thinDetailsChanged;
        }

        public static Result Optimize(GameObject root)
        {
            if (!root) throw new ArgumentNullException(nameof(root));
            var result = new Result();
            foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                result.renderers++;
                if (renderer.shadowCastingMode == ShadowCastingMode.Off) continue;
                result.castersBefore++;
                result.castersAfter++;
                // Shadow-only occlusion helpers and non-imported gameplay objects
                // are intentional authoring, not decorative FBX parts to simplify.
                if (renderer.shadowCastingMode == ShadowCastingMode.ShadowsOnly) continue;
                var source = PrefabUtility.GetCorrespondingObjectFromOriginalSource(renderer);
                if (!source || PrefabUtility.GetPrefabAssetType(source) != PrefabAssetType.Model) continue;
                Vector3 size = renderer.bounds.size;
                float largest = Mathf.Max(size.x, size.y, size.z);
                float smallest = Mathf.Min(size.x, size.y, size.z);
                float middle = size.x + size.y + size.z - largest - smallest;
                string name = renderer.name.ToLowerInvariant();
                // Narrow, explicitly named cosmetic strips can be several metres
                // long. Size alone must never classify a wall or floor as a detail.
                bool thinDetail = largest <= 8 && middle <= .15f && ContainsAny(name, ThinDetailNames);
                // Thin machinery/trim can be long while contributing only a tiny
                // shadow silhouette. Keep large props and every structural name.
                bool smallDetail = largest <= 8 && largest * middle <= 2 && !ContainsAny(name, StructureNames);
                if (!thinDetail && !smallDetail) continue;
                Undo.RecordObject(renderer, "Remove decorative building shadow caster");
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                result.castersAfter--;
                if (thinDetail) result.thinDetailsChanged++; else result.smallDetailsChanged++;
            }
            return result;
        }

        private static bool ContainsAny(string name, string[] words)
        {
            foreach (string word in words) if (name.Contains(word)) return true;
            return false;
        }
    }
}
