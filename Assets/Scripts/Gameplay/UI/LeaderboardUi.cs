// 通用玩法的记分板。打开时从 LeaderboardManager 取名单，先按击杀数排，再以死亡数打破平分。经典爆破有自己的分队记分板。
using System.Collections.Generic;
using Gameplay.Leaderboard;
using Unity.Entities;
using Unity.NetCode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Unity.MP_FPS.UI {
    public class LeaderboardUi : MonoBehaviour {
        public VisualTreeAsset ScoreItemTemplate;
        private EntityManager _entityManager;
        private World _currentClientWorld;
        private EntityQuery _inGameQuery;
        private VisualElement _rootElement;
        private ListView _listView;
        private readonly List<ScoreUiPlayerInfo> _items = new();
        private float _timer;

        private void Awake() {
            var uiDocument = GetComponent<UIDocument>();
            _rootElement = uiDocument.rootVisualElement;
            _rootElement.style.display = DisplayStyle.None;
            _listView = _rootElement.Q<ListView>("score-items");
            _listView.itemsSource = _items;
            _listView.makeItem = () => ScoreItemTemplate.Instantiate();
            _listView.bindItem = (element, i) => {
                var player = _items[i];
                var nameLabel = element.Q<Label>("name");
                var killsLabel = element.Q<Label>("kills");
                var deathsLabel = element.Q<Label>("deaths");
                nameLabel.text = player.PlayerName;
                killsLabel.text = player.Kills.ToString();
                deathsLabel.text = player.Deaths.ToString();
            };
            _listView.fixedItemHeight = 20;
            _listView.selectionType = SelectionType.None;
            _listView.RefreshItems();
        }

        private void OnEnable() {
            GameInput.Actions.UI.ShowLeaderboard.started += OnShowLeaderboard;
            GameInput.Actions.UI.ShowLeaderboard.canceled += OnHideLeaderboard;
        }

        private void OnDisable() {
            GameInput.Actions.UI.ShowLeaderboard.started -= OnShowLeaderboard;
            GameInput.Actions.UI.ShowLeaderboard.canceled -= OnHideLeaderboard;
        }

        private void OnShowLeaderboard(InputAction.CallbackContext context) {
            if (MoonRaidMap.Active != null) return;
            _rootElement.style.display = DisplayStyle.Flex;
        }

        private void OnHideLeaderboard(InputAction.CallbackContext context) {
            _rootElement.style.display = DisplayStyle.None;
        }

        private void InitializeClientWorld(World clientWorld) {
            if (clientWorld != null) {
                _entityManager = clientWorld.EntityManager;
                _inGameQuery = _entityManager.CreateEntityQuery(ComponentType.ReadOnly<NetworkStreamInGame>(),
                    ComponentType.ReadOnly<NetworkStreamConnection>());
                _rootElement.style.display = DisplayStyle.None;
            } else { _entityManager = default; }
            _currentClientWorld = clientWorld;
        }

        private static World GetClientWorld() {
            const string clientWorldName = "ClientWorld";
            for (int i = 0; i < World.All.Count; i++)
                if (World.All[i].Name == clientWorldName && World.All[i].IsCreated) {
                    var clientWorld = World.All[i];
                    return clientWorld;
                }
            return null;
        }

        private void Update() {
            // 查找当前的 ClientWorld。
            var clientWorld = GetClientWorld();
            // 创建、销毁或切换客户端 World 后都重新建查询。
            if (clientWorld != _currentClientWorld) InitializeClientWorld(clientWorld);
            bool worldIsInitialized = _currentClientWorld != null;
            if (!worldIsInitialized) {
                _rootElement.style.display = DisplayStyle.None;
                return;
            }
            bool isInGame = _inGameQuery.CalculateEntityCount() != 0;
            if (!isInGame) {
                _rootElement.style.display = DisplayStyle.None;
                return;
            }
            if (_rootElement.style.display == DisplayStyle.None) return;
            _timer += Time.deltaTime;
            if (_timer < 0.5f) return;
            _timer = 0.0f;
            UpdateLeaderboardUI();
        }

        private void UpdateLeaderboardUI() {
            if (LeaderboardManager.Instance == null) return;
            // 记分板直接读已有战绩。打开界面只将当前结果换成列表数据，不重新统计对局。
            var scores = LeaderboardManager.Instance.GetScores();

            // 按击杀数从多到少排，再按死亡数从少到多排。
            // 先以击杀数排高低，击杀数相同时死亡少的在前。玩家姓名与网络 ID 用原来的记录。
            scores.Sort((a, b) => {
                // 先比较击杀数，从多到少。
                int killComparison = b.Kills.CompareTo(a.Kills);
                if (killComparison != 0) return killComparison;
                // 击杀数相同时比较死亡数，从少到多。
                return a.Deaths.CompareTo(b.Deaths);
            });

            // 清掉之前的列表项。
            _items.Clear();

            // 将排序后的记录加到列表。
            // 将记录转为列表行供 UI Toolkit 复用。视图的显示顺序由刚才的排序决定。
            foreach (var score in scores)
                _items.Add(new ScoreUiPlayerInfo(score.NetworkId, score.PlayerName.ToString(), score.Kills,
                    score.Deaths));
            _listView.RefreshItems();
        }
    }

    public class ScoreUiPlayerInfo {
        public int PlayerId;
        public string PlayerName;
        public int Kills;
        public int Deaths;

        public ScoreUiPlayerInfo(int id, string name, int kills, int deaths) {
            PlayerId = id;
            PlayerName = name;
            Kills = kills;
            Deaths = deaths;
        }
    }
}