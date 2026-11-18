// 短暂显示玩家加入和击杀消息。GameLeaderboard 把事件交到这里，提示按自己的时间显示并清掉。
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.MP_FPS.UI {
    [RequireComponent(typeof(UIDocument))]
    public class ActionFeed : MonoBehaviour {
        // 战绩系统经这个界面实例报告事件。消息队列维护短暂的展现，击杀统计仍由战绩系统掌握。
        public static ActionFeed Instance { get; private set; }

        // UXML 容器元素的名称。
        private const string KillFeedContainerName = "ActionFeedContainer";

        // 单条击杀消息所用的 USS 样式类。
        private const string KillFeedEntryClassName = "action-feed-entry";

        // 每条消息保留的毫秒数。
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

        // 入场提示按独立的事件加入队列，与击杀共用原来的展示区域。
        public void AnnouncePlayerJoined(string playerName) {
            if (_actionFeedContainer == null) return;
            var killLabel = new Label($"{playerName} joined");
            killLabel.AddToClassList(KillFeedEntryClassName); // 给元素应用 USS 样式。
            _actionFeedContainer.Add(killLabel);
            killLabel.schedule.Execute(() => {
                if (killLabel.parent == _actionFeedContainer) killLabel.RemoveFromHierarchy();
            }).StartingIn(MessageDurationMs);
        }

        // 击杀提示从已收到的玩家名字构造标签。把它加进界面后，只按显示时长管理，不参与伤害结算。
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