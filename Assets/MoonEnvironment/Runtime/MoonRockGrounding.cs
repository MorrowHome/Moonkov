using UnityEngine;

namespace Unity.MP_FPS.Moon
{
    // Per-instance authoring data survives scene saves; shader blocks are rebuilt
    // on enable, so no material copy or terrain query is needed each frame.
    [ExecuteAlways]
    public sealed class MoonRockGrounding : MonoBehaviour
    {
        [SerializeField] Vector4 groundPlane;
        [SerializeField] float blendHeight = 0.1f;
        [SerializeField] float reflectance = 1;
        [SerializeField,Range(0,1)] float freshness = 0.5f;
        [SerializeField] Quaternion sourceRotation;
        [SerializeField] bool initialized;
        static readonly int PlaneId = Shader.PropertyToID("_GroundPlane");
        static readonly int HeightId = Shader.PropertyToID("_BlendHeight");
        static readonly int VariationId = Shader.PropertyToID("_RockVariation");
        static readonly int FreshnessId = Shader.PropertyToID("_RockFreshness");

        public Quaternion SourceRotation => initialized ? sourceRotation : transform.rotation;
        public void SetFreshness(float value) { freshness=Mathf.Clamp01(value);Apply(); }

        public void Configure(Vector4 plane, float height, float variation)
        {
            if (!initialized) { sourceRotation = transform.rotation; initialized = true; }
            groundPlane = plane; blendHeight = height; reflectance = variation;
            Apply();
        }

        void OnEnable() => Apply();

        void Apply()
        {
            if (!initialized) return;
            var block = new MaterialPropertyBlock();
            foreach (var renderer in GetComponentsInChildren<MeshRenderer>(true))
            {
                renderer.GetPropertyBlock(block);
                block.SetVector(PlaneId, groundPlane);
                block.SetFloat(HeightId, blendHeight);
                block.SetFloat(VariationId, reflectance);
                block.SetFloat(FreshnessId, freshness);
                renderer.SetPropertyBlock(block);
                block.Clear();
            }
        }

        void OnDisable()
        {
            var block = new MaterialPropertyBlock();
            foreach (var renderer in GetComponentsInChildren<MeshRenderer>(true))
            {
                renderer.GetPropertyBlock(block);
                // Disable just this feature while preserving other overrides.
                block.SetVector(PlaneId, new Vector4(0,1,0,100000));
                block.SetFloat(HeightId, 0.001f); block.SetFloat(VariationId, 1);
                block.SetFloat(FreshnessId, 0.5f);
                renderer.SetPropertyBlock(block); block.Clear();
            }
        }
    }
}
