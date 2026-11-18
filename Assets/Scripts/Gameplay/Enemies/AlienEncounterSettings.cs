// 地图的异形刷新配置。每个槽位记录物种、预制体和候选位置，可设成贴顶伏击。
// AlienEnemySpawnSystem 根据当前人数、时间和地图条件挑选槽位，遵守总数和单次刷新限制。
using System;
using UnityEngine;

namespace Unity.MP_FPS {
    /// <summary>场景里配置的远征遭遇。每个槽位选物种和世界坐标候选点，
    /// 服务器负责数量上限、冷却和单局生成预算。</summary>
    public sealed class AlienEncounterSettings : MonoBehaviour {
        [Serializable]
        public sealed class Spawn {
            public AlienCreatureProfile Profile;
            // 每个遭遇槽位同时指定玩法数值和 Ghost 预制体。服务端靠引用中的 GUID 找到已烘焙实体。
            public GhostSpawner.GhostReference Prefab = new();
            public Vector3 Position;
            [Range(-180, 180)] public float Yaw;

            [Tooltip("Spider encounter: attach at Position along SurfaceNormal and wait before pouncing.")]
            // 贴顶伏击有朝向和多个备选挂点，服务端逐个确认哪个满足附近和隐蔽条件。
            public bool Ambush;

            public Vector3 SurfaceNormal = Vector3.up;

            [Tooltip("Optional additional world-space anchors. Position remains a valid candidate.")]
            public Vector3[] CandidatePositions = Array.Empty<Vector3>();
        }

        public static AlienEncounterSettings Active { get; private set; }

        [Header("Population per loaded raid map")] [SerializeField]
        private bool enableAliens = true;

        [SerializeField] [Range(0, 16)] private int maximumAliens = 8;
        [SerializeField] [Range(1, 2)] private int maximumBosses = 1;
        [SerializeField] [Min(0)] private int maximumTotalSpawns = 24;
        [SerializeField] [Min(1)] private int spawnsPerEncounter = 3;
        [SerializeField] [Range(1, 8)] private int maximumSpawnsPerCheck = 2;

        [Header("Timing / seconds")] [SerializeField] [Min(0)]
        private float initialSpawnDelay = 3;

        [SerializeField] [Min(3)] private float respawnDelay = 90;
        [SerializeField] [Min(0)] private float respawnDelayJitter = 25;
        [SerializeField] [Min(1)] private float retryDelay = 8;

        [Header("Placement / metres")] [SerializeField] [Min(1)]
        private float minimumPlayerSpawnDistance = 18;

        [SerializeField] [Min(1)] private float activationDistance = 240;
        [SerializeField] [Min(0)] private float visibilityDistance = 80;
        [SerializeField] [Min(0)] private float candidateSearchRadius = 12;
        [SerializeField] [Range(1, 16)] private int candidateAttempts = 8;
        [SerializeField] [Range(0, 65)] private float maximumSpawnSlope = 45;
        [SerializeField] private Spawn[] encounters = Array.Empty<Spawn>();
        public bool Enabled => enableAliens;

        // 同时存活上限低时，列表靠后的物种仍应有机会被选中。
        // 槽数量和允许同时活几只是独立概念。保持所有槽位可被轮询，才能有不同物种轮番出现。
        public int Count => encounters?.Length ?? 0;
        public int MaximumAliens => Mathf.Clamp(maximumAliens, 0, 16);
        public int MaximumBosses => Mathf.Clamp(maximumBosses, 1, 2);
        // 总预算在一局内用完即停，同时存活上限只限制这一拍还可不可以生成新个体。
        public int MaximumTotalSpawns => Mathf.Max(0, maximumTotalSpawns);
        public int SpawnsPerEncounter => Mathf.Max(1, spawnsPerEncounter);
        public int MaximumSpawnsPerCheck => Mathf.Clamp(maximumSpawnsPerCheck, 1, 8);
        public float InitialSpawnDelay => Mathf.Max(0, initialSpawnDelay);
        public float RespawnDelay => Mathf.Max(3, respawnDelay);
        public float RespawnDelayJitter => Mathf.Max(0, respawnDelayJitter);
        public float RetryDelay => Mathf.Max(1, retryDelay);
        public float MinimumPlayerSpawnDistance => Mathf.Max(1, minimumPlayerSpawnDistance);
        public float ActivationDistance => Mathf.Max(MinimumPlayerSpawnDistance + 1, activationDistance);
        public float VisibilityDistance => Mathf.Max(0, visibilityDistance);
        public float CandidateSearchRadius => Mathf.Max(0, candidateSearchRadius);
        public int CandidateAttempts => Mathf.Clamp(candidateAttempts, 1, 16);
        public float MaximumSpawnSlope => Mathf.Clamp(maximumSpawnSlope, 0, 65);
        public Spawn this[int i] => encounters[i];
        private void OnEnable() { Active = this; }

        private void OnDisable() {
            if (Active == this) Active = null;
        }
    }
}