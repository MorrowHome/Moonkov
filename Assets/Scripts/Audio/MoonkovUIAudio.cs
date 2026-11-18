// GameManager 加上的界面声音入口。定时找出新出现的 UI Toolkit 文档，把共享点击声和本地化绑定到根节点，两个扫描间新建的按钮也能走根节点事件。
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MP_FPS {
    /// <summary>
    /// Binds every UI Toolkit document in the project to the shared click / submit / hover
    /// sounds, so a selectable control can never be silent just because its screen forgot
    /// to opt in. Before this existed only the stash screen and the raid HUD were wired,
    /// which left the main menu, the pause menu and the post-death result screen mute.
    ///
    /// Roots observe input to bind each button's activation callback. Subtree bindings
    /// are safe: MoonkovAudio installs the same sound callback only once per button.
    /// </summary>
    public sealed class MoonkovUIAudio : MonoBehaviour {
        // Documents appear when scenes load and when screens are created at runtime, so the
        // scan is periodic rather than per frame; delegated input also covers new buttons
        // added to an already-bound document between scans.
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