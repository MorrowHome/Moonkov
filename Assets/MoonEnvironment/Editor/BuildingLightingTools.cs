using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Unity.MP_FPS.Moon.Editor
{
    /// <summary>Calibrates imported fixture units and fixed influence volumes at authoring time.</summary>
    public static class BuildingLightingTools
    {
        [Serializable] public sealed class Result
        {
            public int fixtures, intensityClamped, fittedRanges;
            public float minimumRange = float.MaxValue, maximumIntensity;
        }

        public static Result Calibrate(GameObject root)
        {
            // Only walkable architectural surfaces establish mounting height.
            // Lamp housings, furniture and ceilings must not shorten a light's reach.
            var floors = root.GetComponentsInChildren<Collider>(true).Where(c => c.enabled && !c.isTrigger && c.gameObject.activeInHierarchy &&
                (c is TerrainCollider || IsFloor(c.name))).ToArray();
            UnityEngine.Physics.SyncTransforms();
            var result = new Result();
            foreach (var light in root.GetComponentsInChildren<Light>(true))
            {
                if (light.type != LightType.Spot) continue;
                var source = PrefabUtility.GetCorrespondingObjectFromOriginalSource(light);
                if (!source || PrefabUtility.GetPrefabAssetType(source) != PrefabAssetType.Model) continue;
                if (source.type != LightType.Spot && source.type != LightType.Rectangle && source.type != LightType.Disc) continue;
                Undo.RecordObject(light, "Calibrate imported fixture lighting");
                // Use the immutable FBX source, never multiply the already converted
                // intensity again. Native imported spots require the same unit conversion.
                float ceiling = Mathf.Clamp(source.intensity * .02f, 2, 24);
                if (light.intensity > ceiling) { light.intensity = ceiling; result.intensityClamped++; }
                light.shadows = LightShadows.Soft;
                light.shadowNearPlane = .05f;
                light.shadowBias = .03f;
                light.shadowNormalBias = .15f;
                if (source.type == LightType.Rectangle || source.type == LightType.Disc)
                {
                    // Broader penumbra at the edge of the fixture's cone; fixed in world space.
                    light.innerSpotAngle = Mathf.Min(light.innerSpotAngle, 80);
                    if (light.transform.forward.y < -.7f)
                    {
                        var ray = new Ray(light.transform.position, Vector3.down);
                        float mountingHeight = float.MaxValue;
                        foreach (var floor in floors)
                        {
                            if (!floor.bounds.IntersectRay(ray) || !floor.Raycast(ray, out var hit, 24) ||
                                hit.distance < .75f || hit.normal.y < .65f) continue;
                            mountingHeight = Mathf.Min(mountingHeight, hit.distance);
                        }
                        if (mountingHeight < float.MaxValue)
                        {
                            // Cover the existing outer cone footprint on the floor, plus
                            // a margin. Remove wasted reach into floors/rooms beyond it.
                            float reach = Mathf.Clamp(mountingHeight /
                                Mathf.Max(.2f, Mathf.Cos(light.spotAngle * .5f * Mathf.Deg2Rad)) * 1.15f, 8, 20);
                            if (reach < light.range) { light.range = reach; result.fittedRanges++; }
                        }
                    }
                }
                var data = light.GetComponent<UniversalAdditionalLightData>();
                if (!data) data = Undo.AddComponent<UniversalAdditionalLightData>(light.gameObject);
                Undo.RecordObject(data, "Set filtered fixture shadows");
                // The calibrated Light bias values are otherwise ignored by URP.
                data.usePipelineSettings = false;
                data.softShadowQuality = SoftShadowQuality.Low;
                var settings = new SerializedObject(data);
                settings.FindProperty("m_AdditionalLightsShadowResolutionTier").intValue = UniversalAdditionalLightData.AdditionalLightsShadowResolutionTierLow;
                settings.ApplyModifiedProperties();
                PrefabUtility.RecordPrefabInstancePropertyModifications(light);
                PrefabUtility.RecordPrefabInstancePropertyModifications(data);
                result.fixtures++;
                result.minimumRange = Mathf.Min(result.minimumRange, light.range);
                result.maximumIntensity = Mathf.Max(result.maximumIntensity, light.intensity);
            }
            return result;
        }

        private static bool IsFloor(string name)
        {
            name = name.ToLowerInvariant();
            return name.Contains("floor") || name.Contains("deck") || name.Contains("platform") || name.Contains("tread");
        }
    }
}
