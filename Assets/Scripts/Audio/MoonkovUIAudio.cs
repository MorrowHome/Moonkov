// GameManager 加上的界面声音入口。定时找出新出现的 UI Toolkit 文档，把共享点击声和本地化绑定到根节点，两个扫描间新建的按钮也能走根节点事件。
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MP_FPS {
    /// <summary>
    /// 给项目里所有 UI Toolkit 文档绑定共用的点击、确认和悬停声，
    /// 每个可选控件都能得到声音，无需各页面单独接线。
    /// 此前只有仓库和战局 HUD 配了这些声音，
    /// 因此主菜单、暂停和战后结算页没有按钮音。
    ///
    /// 从根节点观察输入，为各按钮注册自己的激活回调。绑定子树也可以，
    /// 因为 MoonkovAudio 对一个按钮只装一次相同回调。
    /// </summary>
    public sealed class MoonkovUIAudio : MonoBehaviour {
        // 场景加载或运行中创建页面时都可能出现新文档，
        // 因此按间隔扫描即可。在两次扫描间，
        // 已绑定文档新加的按钮也可以由输入事件接入。
        private const float ScanInterval = .25f;
        private float m_NextScan;
        private void Start() { Scan(); }

        private void Update() {
            // 菜单与暂停时仍应能更新按钮声，所以定时用不受时间缩放影响的时钟。定期扫一次就足够发现新打开的文档。
            if (Time.unscaledTime < m_NextScan) return;
            Scan();
        }

        private void Scan() {
            m_NextScan = Time.unscaledTime + ScanInterval;
            // 先清已经离开面板的监听，再找当前和暂时隐藏的文档。新页切出时就已有点击声和本地化。
            MoonkovAudio.PruneBoundRoots();
            var documents = FindObjectsByType<UIDocument>(FindObjectsInactive.Include);
            for (int i = 0; i < documents.Length; i++) {
                var document = documents[i];
                if (document == null) continue;
                var root = document.rootVisualElement;
                if (root == null) continue;
                MoonkovAudio.BindUI(root);
                MoonkovLocalization.Bind(root);
            }
        }
    }
}