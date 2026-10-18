using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Unity.MP_FPS
{
    // Applies only to gameplay-owned generated meshes. Never changes their guides or hitboxes.
    public sealed class AlienVoidAppearance : IDisposable
    {
        private readonly Transform m_Owner;
        private readonly AlienVoidStyle m_Style;
        private readonly List<Renderer> m_Renderers = new();
        private readonly List<Material> m_Slots = new(), m_Fluids = new();
        private readonly Dictionary<Material, Material> m_Replacements = new();
        private readonly HashSet<Material> m_Owned = new();
        private readonly List<Material> m_ChargeMaterials = new();
        private static readonly int Charge = Shader.PropertyToID("_VoidCharge");

        public AlienVoidAppearance(Transform owner)
        { m_Owner = owner; m_Style = Resources.Load<AlienVoidStyle>("Moonkov/AlienVoid"); }

        public void Refresh()
        {
            if (!m_Style) return;
            m_Renderers.Clear(); m_Owner.GetComponentsInChildren(true, m_Renderers);
            m_Fluids.Clear();
            foreach (var renderer in m_Renderers)
            {
                renderer.GetSharedMaterials(m_Slots);
                bool changed = false;
                for (int i = 0; i < m_Slots.Count; i++)
                {
                    var source = m_Slots[i];
                    if (!source || m_Owned.Contains(source)) continue;
                    string shader = source.shader.name;
                    if (shader == "SpiderLab/Umbra08 Particle Fluid Surface" || shader == "SpiderLab/Umbra08 Liquid Filaments")
                    {
                        if (!m_Fluids.Contains(source)) m_Fluids.Add(source);
                        source.SetFloat("_VoidAmount", 1);
                        continue;
                    }
                    if (shader != "Universal Render Pipeline/Lit" && shader != "Universal Render Pipeline/Unlit") continue;
                    if (!m_Replacements.TryGetValue(source, out var replacement))
                    {
                        bool detail = source.name.IndexOf("bone", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            source.name.IndexOf("edge", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            source.name.IndexOf("underside", StringComparison.OrdinalIgnoreCase) >= 0;
                        replacement = new Material(detail ? m_Style.Plate : m_Style.Skin)
                            { name = "Black liquid / " + source.name, hideFlags = HideFlags.DontSave };
                        m_Replacements.Add(source, replacement); m_Owned.Add(replacement); m_ChargeMaterials.Add(replacement);
                    }
                    m_Slots[i] = replacement; changed = true;
                }
                if (changed) renderer.SetSharedMaterials(m_Slots);
            }
        }

        public void Update(in Unity.MP_FPS.SpiderLab.LiquidReaction reaction)
        {
            float charge = (reaction.Charge + reaction.Burst * .75f) * reaction.Strength;
            foreach (var material in m_ChargeMaterials) if (material) material.SetFloat(Charge, charge);
            foreach (var material in m_Fluids) if (material) material.SetFloat(Charge, charge);
        }

        public void Dispose()
        {
            foreach (var material in m_Owned)
                if (material) { if (Application.isPlaying) Object.Destroy(material); else Object.DestroyImmediate(material); }
            m_Owned.Clear(); m_Replacements.Clear(); m_ChargeMaterials.Clear(); m_Fluids.Clear();
        }
    }
}
