using UnityEngine.UIElements;

namespace Unity.MP_FPS.Client
{
    public sealed partial class QuickSlotBindings
    {
        public static bool Typing(VisualElement element)
        {
            for (; element != null; element = element.parent)
                if (element is TextField || element is IntegerField || element is FloatField ||
                    element.ClassListContains("unity-base-text-field") || element.ClassListContains("unity-text-input")) return true;
            return false;
        }
    }
}
