using UnityEngine;

namespace Unity.MP_FPS
{
    [CreateAssetMenu(menuName = "Moonkov/Rifle Feedback")]
    public sealed class RifleFeedbackLibrary : ScriptableObject
    {
        public Material ImpactMaterial;
        public SoundDef RockImpact, MetalImpact, BodyImpact;
        public SoundDef HitConfirm, KillConfirm;
    }
}
