// 本地枪口受阻时摆在准星中央的提示。调用方给出遮挡结果，这里只切换图案显示，鼠标事件可以穿过它。
using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MP_FPS.Client {
    /// <summary>仅本地操作者可见的枪口遮挡提示。游戏与 UI 输入仍由原控件处理。</summary>
    public sealed class HaloObstructionView : IDisposable {
        public VisualElement Element { get; }

        public HaloObstructionView(VisualElement host) {
            // 遮挡提示与准星叠在一起。它本身不接触发事件，显示时也不会影响鼠标归属。
            Element = new VisualElement { name = "haloMuzzleObstructed", pickingMode = PickingMode.Ignore };
            Element.style.position = Position.Absolute;
            // 用百分比找到准星中心，再用半个图标大小补偿左上角位置。不同分辨率可继续居中。
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
            slash.style.position = Position.Absolute;
            slash.style.left = 4;
            slash.style.top = -1;
            slash.style.width = 1.5f;
            slash.style.height = 11;
            slash.style.backgroundColor = amber;
            slash.style.rotate = new Rotate(new Angle(45, AngleUnit.Degree));
            Element.Add(slash);
            host.Add(Element);
            Present(false);
        }

        public void Present(bool obstructed) {
            Element.style.display = obstructed ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public void Dispose() { Element.RemoveFromHierarchy(); }
    }
}