using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Unity.MP_FPS.Moon
{
    /// <summary>Opt-in material conversion for one environment hierarchy, with no asset mutation.</summary>
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class MoonLowSampleMaterialScope : MonoBehaviour
    {
        [SerializeField] private Shader lowSampleShader;
        private readonly Dictionary<Material, Material> replacements = new Dictionary<Material, Material>();
        private readonly Dictionary<Renderer, Material[]> originals = new Dictionary<Renderer, Material[]>();

        public void Configure(Shader shader)
        {
            Restore();
            lowSampleShader = shader;
            if (isActiveAndEnabled) Apply();
        }

        private void OnEnable() => Apply();

        private void Apply()
        {
            if (!lowSampleShader || !lowSampleShader.isSupported || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return;
            foreach (var renderer in GetComponentsInChildren<MeshRenderer>(true))
            {
                var source = renderer.sharedMaterials;
                var slots = (Material[])source.Clone();
                bool changed = false;
                for (int i = 0; i < slots.Length; i++)
                {
                    var material = slots[i];
                    if (!material || material.shader.name != "Universal Render Pipeline/Lit" || material.GetFloat("_Surface") > .5f) continue;
                    if (!replacements.TryGetValue(material, out var clone))
                    {
                        clone = new Material(material)
                        {
                            shader = lowSampleShader, hideFlags = HideFlags.HideAndDontSave,
                            name = material.name + " (low sample runtime)"
                        };
                        replacements.Add(material, clone);
                    }
                    slots[i] = clone;
                    changed = true;
                }
                if (!changed) continue;
                originals.Add(renderer, source);
                renderer.sharedMaterials = slots;
            }
        }

        private void OnDisable() => Restore();

        // Scene saves serialize original references, never the temporary materials.
        // The matching editor hook reapplies the scope after saving.
        public void Restore()
        {
            foreach (var pair in originals)
            {
                if (!pair.Key) continue;
                var slots = pair.Key.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < slots.Length && i < pair.Value.Length; i++)
                    if (pair.Value[i] && replacements.TryGetValue(pair.Value[i], out var clone) && slots[i] == clone)
                    { slots[i] = pair.Value[i]; changed = true; }
                if (changed) pair.Key.sharedMaterials = slots;
            }
            originals.Clear();
            foreach (var clone in replacements.Values) CoreUtils.Destroy(clone);
            replacements.Clear();
        }

        public void Refresh()
        {
            Restore();
            if (isActiveAndEnabled) Apply();
        }
    }
}
