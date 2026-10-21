using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MP_FPS.Client
{
    /// <summary>Owner-only muzzle obstruction cue. It never captures gameplay/UI input.</summary>
    public sealed class HaloObstructionView : IDisposable
    {
        public VisualElement Element { get; }
        public HaloObstructionView(VisualElement host)
        {
            Element = new VisualElement { name = "haloMuzzleObstructed", pickingMode = PickingMode.Ignore };
            Element.style.position = Position.Absolute;
            Element.style.left = Element.style.top = Length.Percent(50);
            Element.style.marginLeft = Element.style.marginTop = -6;
            Element.style.width = Element.style.height = 12;
            Element.style.borderTopWidth = Element.style.borderBottomWidth = 1.5f;
            Element.style.borderLeftWidth = Element.style.borderRightWidth = 1.5f;
            var amber = new Color(.95f, .66f, .25f, .9f);
            Element.style.borderTopColor = Element.style.borderBottomColor = amber;
            Element.style.borderLeftColor = Element.style.borderRightColor = amber;
            Element.style.borderTopLeftRadius = Element.style.borderTopRightRadius = 6;
            Element.style.borderBottomLeftRadius = Element.style.borderBottomRightRadius = 6;
            var slash = new VisualElement { pickingMode = PickingMode.Ignore };
            slash.style.position = Position.Absolute; slash.style.left = 4; slash.style.top = -1;
            slash.style.width = 1.5f; slash.style.height = 11;
            slash.style.backgroundColor = amber;
            slash.style.rotate = new Rotate(new Angle(45, AngleUnit.Degree));
            Element.Add(slash); host.Add(Element); Present(false);
        }
        public void Present(bool obstructed) => Element.style.display = obstructed ? DisplayStyle.Flex : DisplayStyle.None;
        public void Dispose() => Element.RemoveFromHierarchy();
    }
}
