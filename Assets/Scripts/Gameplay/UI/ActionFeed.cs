// 短暂显示玩家加入和击杀消息。GameLeaderboard 把事件交到这里，提示按自己的时间显示并清掉。
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MP_FPS.UI {
    [RequireComponent(typeof(UIDocument))]
    public class ActionFeed : MonoBehaviour {
        public static ActionFeed Instance { get; private set; }

        // Name of the container element in your UXML
        private const string KillFeedContainerName = "ActionFeedContainer";

        // Name of the USS class for styling individual kill messages
        private const string KillFeedEntryClassName = "action-feed-entry";

        // Duration in milliseconds
        private const long MessageDurationMs = 4000;
        private VisualElement _rootElement;
        private VisualElement _actionFeedContainer;

        private void Awake() {
            if (Instance != null && Instance != this) {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void OnEnable() {
            _rootElement = GetComponent<UIDocument>().rootVisualElement;
            if (_rootElement == null) {
                Debug.LogError("KillFeedUI: Could not find root VisualElement.");
                return;
            }
            _actionFeedContainer = _rootElement.Q<VisualElement>(KillFeedContainerName);
            if (_actionFeedContainer == null) {
                Debug.LogError($"KillFeedUI: Could not find VisualElement named '{KillFeedContainerName}'.");
                return;
            }
        }

        private void LateUpdate() {
            if (_rootElement != null)
                _rootElement.style.display = GameSettings.Instance.GameState == GlobalGameState.InGame
                    ? DisplayStyle.None
                    : DisplayStyle.Flex;
        }

        public void AnnouncePlayerJoined(string playerName) {
            if (_actionFeedContainer == null) return;
            var killLabel = new Label($"{playerName} joined");
            killLabel.AddToClassList(KillFeedEntryClassName); // Apply USS style
            _actionFeedContainer.Add(killLabel);
            killLabel.schedule.Execute(() => {
                if (killLabel.parent == _actionFeedContainer) killLabel.RemoveFromHierarchy();
            }).StartingIn(MessageDurationMs);
        }

        public void AnnounceKill(string killer, string victim) {
            if (_actionFeedContainer == null) return;
            var killLabel = new Label($"{killer} killed {victim}");
            killLabel.AddToClassList(KillFeedEntryClassName);
            _actionFeedContainer.Add(killLabel);
            killLabel.schedule.Execute(() => {
                if (killLabel.parent == _actionFeedContainer) killLabel.RemoveFromHierarchy();
            }).StartingIn(MessageDurationMs);
        }
    }
}