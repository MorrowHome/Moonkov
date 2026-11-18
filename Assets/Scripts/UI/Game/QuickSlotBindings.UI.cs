// 判断玩家是否正在界面的文本输入框里输入。局内路由用它屏蔽数字快捷栏等按键，避免打字时误切武器。
using UnityEngine.UIElements;

namespace Unity.MP_FPS.Client {
    public sealed partial class QuickSlotBindings {
        public static bool Typing(VisualElement element) {
            // 获得焦点的可能是复合输入框内部的一小层，需要沿父节点向上找。数字框里的数字键也不该触发切槽。
            for (; element != null; element = element.parent)
                if (element is TextField || element is IntegerField || element is FloatField ||
                    element.ClassListContains("unity-base-text-field") || element.ClassListContains("unity-text-input"))
                    return true;
            return false;
        }
    }
}