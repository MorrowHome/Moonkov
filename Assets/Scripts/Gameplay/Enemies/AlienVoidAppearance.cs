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
        private readonly Color m_Skin, m_Plate, m_Tissue;
        private readonly List<Renderer> m_Renderers = new();
        private readonly List<Material> m_Slots = new(), m_Fluids = new();
        private readonly Dictionary<Material, Material> m_Replacements = new();
        private readonly HashSet<Material> m_Owned = new();
        private readonly List<Material> m_ChargeMaterials = new();
        private static readonly int Charge = Shader.PropertyToID("_VoidCharge");

        public AlienVoidAppearance(Transform owner, AlienSpecies species)
        {
            m_Owner = owner; m_Style = Resources.Load<AlienVoidStyle>("Moonkov/AlienVoid");
            Palette(species, out m_Skin, out m_Plate, out m_Tissue);
        }

        private static void Palette(AlienSpecies species, out Color skin, out Color plate, out Color tissue)
        {
            skin = new Color(.23f, .28f, .30f); plate = new Color(.56f, .53f, .44f); tissue = new Color(.31f, .12f, .10f);
            switch (species)
            {
                case AlienSpecies.Snake:
                    skin = new Color(.30f, .31f, .21f); plate = new Color(.62f, .57f, .39f); break;
                case AlienSpecies.Caterpillar:
                    skin = new Color(.48f, .43f, .35f); plate = new Color(.67f, .61f, .45f); tissue = new Color(.37f, .19f, .16f); break;
                case AlienSpecies.Leech:
                    skin = new Color(.28f, .14f, .17f); plate = new Color(.50f, .34f, .30f); tissue = new Color(.42f, .17f, .16f); break;
                case AlienSpecies.Slime:
                    skin = new Color(.28f, .34f, .18f); plate = new Color(.52f, .55f, .30f); tissue = new Color(.38f, .23f, .12f); break;
                case AlienSpecies.FaceHugger:
                    skin = new Color(.45f, .36f, .29f); plate = new Color(.67f, .59f, .43f); tissue = new Color(.38f, .18f, .16f); break;
                case AlienSpecies.StiltWalker:
                    skin = new Color(.40f, .43f, .41f); plate = new Color(.67f, .65f, .55f); tissue = new Color(.25f, .12f, .14f); break;
                case AlienSpecies.Roots:
                    skin = new Color(.29f, .26f, .22f); plate = new Color(.57f, .53f, .43f); break;
                case AlienSpecies.Tentacle:
                    skin = new Color(.25f, .22f, .30f); plate = new Color(.52f, .48f, .53f); break;
            }
        }
        private void ApplyPalette(Material material)
        {
            if (material.HasProperty("_SkinColor")) material.SetColor("_SkinColor", m_Skin);
            material.SetColor("_CarapaceColor", m_Plate);
            material.SetColor("_TissueColor", m_Tissue);
        }

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
                        ApplyPalette(source);
                        continue;
                    }
                    if (shader != "Universal Render Pipeline/Lit" && shader != "Universal Render Pipeline/Unlit") continue;
                    if (!m_Replacements.TryGetValue(source, out var replacement))
                    {
                        bool detail = source.name.IndexOf("bone", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            source.name.IndexOf("edge", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            source.name.IndexOf("underside", StringComparison.OrdinalIgnoreCase) >= 0;
                        replacement = new Material(detail ? m_Style.Plate : m_Style.Skin)
                            { name = "Layered tissue / " + source.name, hideFlags = HideFlags.DontSave };
                        ApplyPalette(replacement);
                        replacement.SetColor("_BaseColor", detail ? m_Plate : m_Skin);
                        replacement.SetFloat("_Smoothness", detail ? .38f : .66f);
                        replacement.SetFloat("_Metallic", .02f);
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
