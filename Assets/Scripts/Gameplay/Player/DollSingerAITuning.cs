using UnityEngine;

namespace Unity.MP_FPS
{
    [CreateAssetMenu(menuName = "Moonkov/DollSinger PMC tuning")]
    public sealed class DollSingerAITuning : ScriptableObject
    {
        [Header("Perception and memory")]
        [Range(30, 100)] public float HalfViewAngle = 70;
        [Min(.1f)] public float RecognitionSeconds = .8f;
        [Min(1)] public float MemorySeconds = 18;
        [Min(.1f)] public float ReactionSeconds = .45f;
        [Min(1)] public float ShotSensorRange = 42;
        [Min(1)] public float FootstepSensorRange = 12;
        [Range(.2f, 1)] public float NightSightMultiplier = .65f;
        [Header("Combat")]
        [Min(2)] public float PreferredRange = 18;
        [Min(.1f)] public float SettledAimError = 1.2f;
        [Min(.1f)] public float FirstShotError = 3.5f;
        [Min(30)] public float TurnSpeed = 160;
        [Range(.1f, .8f)] public float RetreatHealthFraction = .32f;
        [Header("Raid objectives")]
        [Range(1, 8)] public int DesiredCaches = 3;
        [Min(.5f)] public float LootSearchSeconds = 2.5f;
        [Range(.2f, .95f)] public float DepartureFraction = .65f;
        [Header("Decision scheduling")]
        [Range(.1f, .5f)] public float DecisionInterval = .2f;
        [Min(.5f)] public float CommitmentSeconds = 1.6f;
        [Min(.5f)] public float CoverRescanSeconds = 1.5f;
    }
}
