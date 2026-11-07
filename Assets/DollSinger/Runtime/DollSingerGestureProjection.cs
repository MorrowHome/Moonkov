using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Unity.MP_FPS.DollSinger
{
    /// <summary>One fixed eye projection for readable gestures, independent of optical zoom.</summary>
    public sealed class DollSingerGestureProjection : IDisposable
    {
        public const float FieldOfView = 110f;
        public Camera Camera { get; }
        public bool Active { get; private set; }
        private readonly Camera owner;
        private readonly SkinnedMeshRenderer source, renderer;
        private readonly List<Camera> stack;
        private readonly float[] weights;

        public DollSingerGestureProjection(Camera owner, SkinnedMeshRenderer source, Mesh body)
        {
            this.owner = owner;
            this.source = source;
            int layer = LayerMask.NameToLayer("FirstPersonOverlay");
            if (!owner || layer < 0 || !(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset)) return;
            stack = owner.GetUniversalAdditionalCameraData().cameraStack;
            if (stack == null) return;
            // Share the connected owner mesh and live bones. No arm cuts, detached
            // wrists, copied morph payload, or second animation evaluation.
            var item = new GameObject("FirstPersonGestureBody (Runtime)") { hideFlags = HideFlags.DontSave, layer = layer };
            item.transform.SetParent(source.transform, false);
            renderer = item.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = body;
            renderer.sharedMaterials = source.sharedMaterials;
            renderer.bones = source.bones;
            renderer.rootBone = source.rootBone;
            renderer.localBounds = source.localBounds;
            renderer.quality = source.quality;
            renderer.renderingLayerMask = source.renderingLayerMask;
            renderer.updateWhenOffscreen = true;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = source.receiveShadows;
            renderer.forceRenderingOff = true;
            weights = new float[body.blendShapeCount];
            for (int i = 0; i < weights.Length; i++) weights[i] = float.NaN;
            var lens = new GameObject("FirstPersonGestureProjection (Runtime)") { hideFlags = HideFlags.DontSave };
            lens.transform.SetParent(owner.transform, false);
            Camera = lens.AddComponent<Camera>();
            Camera.enabled = false;
            Camera.cullingMask = 1 << layer;
            Camera.allowHDR = owner.allowHDR;
            Camera.allowMSAA = owner.allowMSAA;
            var overlay = Camera.GetUniversalAdditionalCameraData();
            overlay.renderType = CameraRenderType.Overlay;
            // Same eye and depth mapping: walls still occlude the presented body.
            // URP exposes this serialized setting as a getter-only property.
            JsonUtility.FromJsonOverwrite("{\"m_ClearDepth\":false}", overlay);
            overlay.renderPostProcessing = false;
            overlay.volumeLayerMask = 0;
        }

        public void Present(bool active, float blend, float baseFieldOfView)
        {
            if (!Camera) return;
            Active = active && source && source.enabled && owner.isActiveAndEnabled;
            Camera.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            float tangent = Mathf.Tan(Mathf.Max(FieldOfView, baseFieldOfView) * .5f * Mathf.Deg2Rad);
            // Preserve horizontal room on narrower displays; never crop a palm
            // merely because the user switches from widescreen to 4:3.
            tangent *= Mathf.Max(1, (16f / 9) / owner.aspect);
            Camera.fieldOfView = Mathf.Lerp(owner.fieldOfView, 2 * Mathf.Atan(tangent) * Mathf.Rad2Deg, blend);
            Camera.nearClipPlane = owner.nearClipPlane;
            Camera.farClipPlane = owner.farClipPlane;
            Camera.rect = owner.rect;
            Camera.aspect = owner.aspect;
            Camera.enabled = Active;
            if (Active && !stack.Contains(Camera)) stack.Add(Camera);
            else if (!Active) stack.Remove(Camera);
            renderer.forceRenderingOff = true;
            if (!Active) return;
            for (int i = 0; i < weights.Length; i++)
            {
                float weight = source.GetBlendShapeWeight(i);
                if (weights[i] == weight) continue;
                weights[i] = weight;
                renderer.SetBlendShapeWeight(i, weight);
            }
        }

        public void BeforeCamera(Camera camera)
        {
            if (renderer) renderer.forceRenderingOff = !Active || camera != Camera || source.forceRenderingOff;
        }

        public void AfterCamera() { if (renderer) renderer.forceRenderingOff = true; }

        public void Dispose()
        {
            Active = false;
            if (Camera) { stack.Remove(Camera); Camera.enabled = false; Destroy(Camera.gameObject); }
            if (renderer) { renderer.forceRenderingOff = true; Destroy(renderer.gameObject); }
        }

        private static void Destroy(UnityEngine.Object item)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(item);
            else UnityEngine.Object.DestroyImmediate(item);
        }
    }
}
