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
            // Find the current ClientWorld, if any
            var clientWorld = GetClientWorld();
            // If the client world changed (new, destroyed, or swapped), (re)initialize
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
            var scores = LeaderboardManager.Instance.GetScores();

            // Sort scores: Kills descending, then Deaths ascending
            scores.Sort((a, b) => {
                // Primary sort: Kills descending
                int killComparison = b.Kills.CompareTo(a.Kills);
                if (killComparison != 0) return killComparison;
                // Secondary sort: Deaths ascending
                return a.Deaths.CompareTo(b.Deaths);
            });

            // Clear previous entries
            _items.Clear();

            // Add new entries
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