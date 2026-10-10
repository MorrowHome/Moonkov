using UnityEngine;

namespace Unity.MP_FPS
{
    // Presentation only: weapon damage, spread and projectile authority remain in WeaponData.
    [System.Serializable]
    public sealed class HaloWeaponFeedbackProfile
    {
        public SoundDef FireBody, FireSnap, FireTail;
        public SoundDef RockImpact, MetalImpact, BodyImpact, HitConfirm, KillConfirm;
        [Min(.1f)] public float FragmentScale = 1f, FragmentSpeed = 1f, FragmentCount = 1f;
        [Range(0f, .25f)] public float SurfaceSoundInterval;
        public bool ImpactFlash;
        public Color HitColor = Color.white;
        [Range(.06f, .4f)] public float HitDuration = .14f, KillDuration = .26f;
        [Range(1f, 3f)] public float HitLineWidth = 1.5f;
        [Range(8f, 13f)] public float HitOuterRadius = 10f;
    }

    [CreateAssetMenu(menuName = "Moonkov/Rifle Feedback")]
    public sealed class RifleFeedbackLibrary : ScriptableObject
    {
        public Material ImpactMaterial;
        public SoundDef RockImpact, MetalImpact, BodyImpact;
        public SoundDef HitConfirm, KillConfirm;
        public HaloWeaponFeedbackProfile Revolver, Shotgun, Sniper;

        public HaloWeaponFeedbackProfile ForWeapon(uint weaponId) => weaponId switch
        {
            DollSingerWeapons.Revolver => Revolver,
            DollSingerWeapons.Shotgun => Shotgun,
            DollSingerWeapons.Sniper => Sniper,
            _ => null // Keep the existing rifle mix and feedback as the baseline.
        };
    }
}
