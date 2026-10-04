using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MP_FPS
{
    /// <summary>
    /// Binds every UI Toolkit document in the project to the shared click / submit / hover
    /// sounds, so a selectable control can never be silent just because its screen forgot
    /// to opt in. Before this existed only the stash screen and the raid HUD were wired,
    /// which left the main menu, the pause menu and the post-death result screen mute.
    ///
    /// Only whole UIDocument roots are bound. A screen that binds one of its own child
    /// elements would produce a second callback for the same event, so the screens do not
    /// do that (see MoonkovAudio.BindUI).
    /// </summary>
    public sealed class MoonkovUIAudio : MonoBehaviour
    {
        // Documents appear when scenes load and when screens are created at runtime, so the
        // scan is cheap and periodic rather than per frame. A control only becomes clickable
        // after the user sees it, which is far longer than one interval.
        private const float ScanInterval = .25f;

        private float m_NextScan;

        private void Start()
        {
            Scan();
        }

        private void Update()
        {
            if (Time.unscaledTime < m_NextScan) return;
            Scan();
        }

        private void Scan()
        {
            m_NextScan = Time.unscaledTime + ScanInterval;
            MoonkovAudio.PruneBoundRoots();

            var documents = FindObjectsByType<UIDocument>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < documents.Length; i++)
            {
                var document = documents[i];
                if (document == null) continue;
                var root = document.rootVisualElement;
                if (root != null) MoonkovAudio.BindUI(root);
            }
        }
    }
}
