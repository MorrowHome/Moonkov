// 按武器选择枪响音层、命中声和镜头后坐等表现参数。角色网络表现和 RifleImpactFeedback 从同一资源拿这些配置。
using UnityEngine;

namespace Unity.MP_FPS {
    // 这里只播放表现。武器的伤害、散布和弹道判定仍由 WeaponData 与玩法系统处理。
    [System.Serializable]
    public sealed class HaloWeaponFeedbackProfile {
        // 一声枪响可以拆成主体、短促起音和尾音。连续开火时依据武器配置调整音层重叠和音高。
        public SoundDef FireBody, FireSnap, FireTail;
        public SoundDef RockImpact, MetalImpact, BodyImpact, HitConfirm, KillConfirm;

        [Header("Per-weapon shot response (zero keeps the existing response)")] [Min(0f)]
        public float RecoilPitch, RecoilYaw, GestureReleaseSeconds;

        [Range(0f, 1f)] public float RecoilReturnFraction = 1f;
        [Min(0f)] public float RecoilHoldSeconds;
        [Min(.1f)] public float RecoilSettleSpeed = 13f;

        [Header("Shot mix")] [Range(.01f, .20f)]
        public float BodyCrossfade = .045f;

        [Range(.01f, .2f)] public float TailCrossfade = .09f;
        [Range(.6f, 1f)] public float SustainedBodyGain = .9f;
        [Range(0f, 100f)] public float BurstPitchRise = 35f;
        [Min(.1f)] public float FragmentScale = 1f, FragmentSpeed = 1f, FragmentCount = 1f;
        [Range(0f, .25f)] public float SurfaceSoundInterval;
        public bool ImpactFlash;
        public Color HitColor = Color.white;
        [Range(.06f, .4f)] public float HitDuration = .14f, KillDuration = .26f;
        [Range(1f, 3f)] public float HitLineWidth = 1.5f;
        [Range(8f, 13f)] public float HitOuterRadius = 10f;
    }

    [System.Serializable]
    public sealed class WeaponFeedbackEntry {
        public uint WeaponId;
        public HaloWeaponFeedbackProfile Profile;
    }

    [CreateAssetMenu(menuName = "Moonkov/Rifle Feedback")]
    public sealed class RifleFeedbackLibrary : ScriptableObject {
        public Material ImpactMaterial;
        public SoundDef RockImpact, MetalImpact, BodyImpact;
        public SoundDef HitConfirm, KillConfirm;
        public HaloWeaponFeedbackProfile Rifle, Revolver, Shotgun, Sniper;
        public WeaponFeedbackEntry[] WeaponProfiles;

        public HaloWeaponFeedbackProfile ForWeapon(uint weaponId) {
            // 先找这个武器的专用条目。没配专用时按枪型取通用反馈，旧资源也能正常播放。
            if (WeaponProfiles != null)
                foreach (var entry in WeaponProfiles)
                    if (entry != null && entry.WeaponId == weaponId && entry.Profile != null)
                        return entry.Profile;
            return BaseForWeapon(weaponId);
        }

        // 兼容旧版资源，也供安装工具重建视觉和撞击表现的默认值。
        public HaloWeaponFeedbackProfile BaseForWeapon(uint weaponId) {
            return weaponId switch {
                DollSingerWeapons.Halo => Rifle,
                DollSingerWeapons.Revolver => Revolver,
                DollSingerWeapons.Shotgun => Shotgun,
                DollSingerWeapons.Sniper => Sniper,
                DollSingerWeapons.Cicada => Rifle,
                DollSingerWeapons.Trine => Rifle,
                DollSingerWeapons.Aegis => Rifle,
                DollSingerWeapons.Vesper => Sniper,
                DollSingerWeapons.Thorn => Shotgun,
                DollSingerWeapons.Sundown => Shotgun,
                DollSingerWeapons.Rondo => Rifle,
                DollSingerWeapons.Tempest => Shotgun,
                DollSingerWeapons.Nocturne => Sniper,
                _ => null
            };
        }
    }
}