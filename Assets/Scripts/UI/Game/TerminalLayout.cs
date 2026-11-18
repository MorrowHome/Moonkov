// 终端视图从资源中的 UXML 模板复制界面，或把模板内容填进已有节点。这里是多个页面共用的加载入口。
using System;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MP_FPS.Client {
    // Layout assets own hierarchy and presentation; presenters only bind state and actions.
    internal static class TerminalLayout {
        // 统一从终端资源目录取模板。调用方只给资源相对名称和希望拿到的根元素。
        internal static VisualElement Clone(string assetName, string elementName) {
            var tree = Resources.Load<VisualTreeAsset>("Moonkov/UI/" + assetName);
            if (tree == null) throw new InvalidOperationException("Missing Moonkov UI layout: " + assetName);
            return Clone(tree, elementName);
        }

        // 已经加载过的模板可以反复克隆。每次得到新的控件实例，多个容器之间不会移动同一份节点。
        internal static VisualElement Clone(VisualTreeAsset tree, string elementName) {
            if (tree == null) throw new InvalidOperationException("Missing Moonkov UI component: " + elementName);
            var element = tree.CloneTree().Q<VisualElement>(elementName);
            if (element == null)
                throw new InvalidOperationException(tree.name + " is missing UI binding: " + elementName);
            element.RemoveFromHierarchy();
            MoonkovLocalization.Bind(element);
            return element;
        }

        // 切页时把内容装到已有父节点。导航和浮窗层在外面保留，只替换正文区域。
        internal static void Populate(VisualElement parent, string assetName) {
            var page = Clone(assetName, "terminalPage");
            foreach (var child in page.Children().ToArray()) parent.Add(child);
        }
    }

    public sealed partial class TechFrame {
        public TechFrame() : this(0) { }

        [UxmlAttribute]
        public float Delay {
            get => m_Delay;
            set {
                m_Delay = value;
                MarkDirtyRepaint();
            }
        }
    }

    public sealed partial class HazardStripes {
        public HazardStripes() : this(TerminalPalette.Yellow) { }

        [UxmlAttribute]
        public Color StripeColor {
            get => m_Color;
            set {
                m_Color = value;
                MarkDirtyRepaint();
            }
        }

        [UxmlAttribute]
        public bool Moving {
            get => m_Moving;
            set {
                m_Moving = value;
                MarkDirtyRepaint();
            }
        }
    }

    public sealed partial class ArcMeter {
        public ArcMeter() : this(0, TerminalPalette.Yellow) { }

        [UxmlAttribute]
        public float Value {
            get => m_Value;
            set {
                m_Value = Mathf.Clamp01(value);
                MarkDirtyRepaint();
            }
        }

        [UxmlAttribute]
        public Color MeterColor {
            get => m_Color;
            set {
                m_Color = value;
                MarkDirtyRepaint();
            }
        }
    }
}