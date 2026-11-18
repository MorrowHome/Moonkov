// 单把武器的配置资产。开火逻辑从这里读取射速、容量和能耗，弹道系统读取伤害、速度、重力和散布，表现层读取镜头等参数。
using UnityEngine;

namespace Unity.MP_FPS {
    public enum WeaponType {
        Hitscan,
        Projectile
    }

    public enum ReticleType {
        Cross,
        TCross,
        OpenCircular,
        CircularCross
    }

    [CreateAssetMenu(fileName = "NewWeaponData", menuName = "FPS Sample/Weapon Data")]
    public class WeaponData : ScriptableObject {
        [Header("General")] public string WeaponName = "Assault Rifle";
        // Hitscan 当拍用射线查询。Projectile 生成网络弹体，在之后的物理步里继续飞。
        public WeaponType Type = WeaponType.Hitscan;
        public ReticleType ReticleType = ReticleType.TCross;

        [Header("Firing Mechanics")] [Tooltip("Seconds between shots (legacy field name).")]
        // 历史字段名带 Ms，装填与开火规则实际按秒比较。
        public float CooldownInMs = 10f;

        [Min(0f)] [Tooltip("Seconds to ready a halo after equipping, independent of its firing interval.")]
        public float EquipTime = 0.22f;

        public float Damage = 15f;

        [Header("Damage over distance")] [Min(0f)]
        public float DamageFalloffStart;

        [Min(0f)] [Tooltip("Zero preserves full damage at every distance. Does not affect explosions.")]
        // 衰减从起点开始，到终点降到最低倍率。结束位置不大于开始位置时保留原始伤害。
        public float DamageFalloffEnd;

        [Range(0f, 1f)] public float MinimumDamageFraction = 1f;
        public bool Automatic = true;
        [Range(1, 8)] public int BurstCount = 1;
        [Min(.01f)] public float BurstInterval = .085f;
        [Min(.01f)] public float BurstCooldown = .28f;
        public bool AutoReloadWhenEmpty;

        [Header("Hitscan Properties")] [Tooltip("Max range for raycast-based weapons.")]
        public float HitscanRange = 100f;

        [Header("Ammo & Reloading")] public int MagazineSize = 30;
        public float ReloadTime = 2.0f; // 时间单位为秒。

        [Min(1)] [Tooltip("Battery energy spent per round added to this halo's magazine.")]
        // 发数是界面和武器容量的单位，电池和光环池里存能量，用这个数值来换算。
        public int EnergyPerRound = 1;

        [Header("Projectile Properties")] [Tooltip("The ghost prefab for the projectile to be spawned.")]
        public GhostSpawner.GhostReference ProjectileGhostPrefab;

        public GhostSpawner.GhostReference ProjectileHitVfxPrefab;
        public GhostSpawner.GhostReference MuzzleFlashVfxPrefab;
        public SoundDef WeaponFireSfx;
        public SoundDef WeaponReloadSfx;
        public SoundDef WeaponFireLayerSfx;
        public SoundDef WeaponImpactSfx;
        // 决定命中时是单体伤害还是范围爆炸。飞行碰撞仍能共用弹道逻辑。
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

    public enum ProjectileBehavior {
        DirectDamage,
        AreaOfEffect
    }
}