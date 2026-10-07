using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MP_FPS.Client
{
    // Layout assets own hierarchy and presentation; presenters only bind state and actions.
    internal static class TerminalLayout
    {
        internal static VisualElement Clone(string assetName, string elementName)
        {
            var tree = Resources.Load<VisualTreeAsset>("Moonkov/UI/" + assetName);
            if (tree == null) throw new InvalidOperationException("Missing Moonkov UI layout: " + assetName);
            var element = tree.CloneTree().Q<VisualElement>(elementName);
            if (element == null) throw new InvalidOperationException(assetName + " is missing UI binding: " + elementName);
            element.RemoveFromHierarchy();
            MoonkovLocalization.Bind(element);
            return element;
        }

        internal static void Populate(VisualElement parent, string assetName)
        {
            var page = Clone(assetName, "terminalPage");
            foreach (var child in page.Children().ToArray()) parent.Add(child);
        }
    }

    public sealed partial class TechFrame
    {
        public TechFrame() : this(0) { }
        [UxmlAttribute]
        public float Delay { get => m_Delay; set { m_Delay = value; MarkDirtyRepaint(); } }
    }

    public sealed partial class HazardStripes
    {
        public HazardStripes() : this(TerminalPalette.Yellow) { }
        [UxmlAttribute]
        public Color StripeColor { get => m_Color; set { m_Color = value; MarkDirtyRepaint(); } }
        [UxmlAttribute]
        public bool Moving { get => m_Moving; set { m_Moving = value; MarkDirtyRepaint(); } }
    }

    public sealed partial class ArcMeter
    {
        public ArcMeter() : this(0, TerminalPalette.Yellow) { }
        [UxmlAttribute]
        public float Value { get => m_Value; set { m_Value = Mathf.Clamp01(value); MarkDirtyRepaint(); } }
        [UxmlAttribute]
        public Color MeterColor { get => m_Color; set { m_Color = value; MarkDirtyRepaint(); } }
    }
}
