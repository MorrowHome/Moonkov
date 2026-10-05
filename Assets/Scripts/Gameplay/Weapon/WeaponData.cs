using UnityEngine;

namespace Unity.MP_FPS
{
    public enum WeaponType
    {
        Hitscan,
        Projectile
    }

    public enum ReticleType
    {
        Cross,
        TCross,
        OpenCircular,
        CircularCross
    }

    [CreateAssetMenu(fileName = "NewWeaponData", menuName = "FPS Sample/Weapon Data")]
    public class WeaponData : ScriptableObject
    {
        [Header("General")] public string WeaponName = "Assault Rifle";
        public WeaponType Type = WeaponType.Hitscan;
        public ReticleType ReticleType = ReticleType.TCross;

        [Header("Firing Mechanics")] [Tooltip("Seconds between shots (legacy field name).")]
        public float CooldownInMs = 10f;

        public float Damage = 15f;
        public bool Automatic = true;
        public bool AutoReloadWhenEmpty;

        [Header("Hitscan Properties")] [Tooltip("Max range for raycast-based weapons.")]
        public float HitscanRange = 100f;

        [Header("Ammo & Reloading")] public int MagazineSize = 30;
        public float ReloadTime = 2.0f; // Time in seconds
        [Min(1), Tooltip("Battery energy spent per round added to this halo's magazine.")]
        public int EnergyPerRound = 1;

        [Header("Projectile Properties")] [Tooltip("The ghost prefab for the projectile to be spawned.")]
        public GhostSpawner.GhostReference ProjectileGhostPrefab;

        public GhostSpawner.GhostReference ProjectileHitVfxPrefab;
        public GhostSpawner.GhostReference MuzzleFlashVfxPrefab;
        public SoundDef WeaponFireSfx;
        public SoundDef WeaponReloadSfx;
        public SoundDef WeaponFireLayerSfx;
        public SoundDef WeaponImpactSfx;

        public ProjectileBehavior Behavior = ProjectileBehavior.DirectDamage;
        public float AoeRadius = 5f;
        public float ProjectileSpeed = 30f;
        [Range(1, 32)] public int PelletCount = 1;
        [Range(0f, 30f)] public float SpreadDegrees;
        [Tooltip("Downward acceleration in m/s²; lunar gravity is 1.62. Zero keeps legacy rockets straight.")]
        public float ProjectileGravity;
        [Min(0.001f)] public float ProjectileRadius = 0.01f;
        [Min(0.1f)] public float ProjectileLifetime = 5f;
        public bool ShowProjectileBody;
    }

    public enum ProjectileBehavior
    {
        DirectDamage,
        AreaOfEffect
    }
}
