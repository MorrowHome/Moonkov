using System;
using UnityEngine;

namespace Unity.MP_FPS
{
    /// <summary>Authored raid encounters. Each slot selects a registered alien prefab
    /// and a real-world spawn point; the server owns creation and lifetime.</summary>
    public sealed class AlienEncounterSettings : MonoBehaviour
    {
        [Serializable] public sealed class Spawn
        {
            public AlienCreatureProfile Profile;
            public GhostSpawner.GhostReference Prefab = new GhostSpawner.GhostReference();
            public Vector3 Position;
            [Range(-180, 180)] public float Yaw;
        }
        public static AlienEncounterSettings Active { get; private set; }
        [SerializeField] private bool enableAliens = true;
        [SerializeField, Range(0, 16)] private int maximumAliens = 8;
        [SerializeField, Min(1)] private float minimumPlayerSpawnDistance = 8;
        [SerializeField] private Spawn[] encounters = Array.Empty<Spawn>();
        public bool Enabled => enableAliens;
        public int Count => Mathf.Min(maximumAliens, encounters.Length);
        public float MinimumPlayerSpawnDistance => minimumPlayerSpawnDistance;
        public Spawn this[int i] => encounters[i];
        private void OnEnable() { Active = this; }
        private void OnDisable() { if (Active == this) Active = null; }
    }
}
