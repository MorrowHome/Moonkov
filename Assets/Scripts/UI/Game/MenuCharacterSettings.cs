using UnityEngine;

namespace Unity.MP_FPS.Client
{
    [CreateAssetMenu(menuName = "Moonkov/Menu Character Settings")]
    public sealed class MenuCharacterSettings : ScriptableObject
    {
        public GameObject VisualPrefab;
        public AnimationClip[] Motions;
        public AvatarMask HaloAimMask;
        public AvatarMask[] MotionMasks;
        [Range(0f, 45f)] public float TurnDegrees = 18f;
        public Color IdleHaloColor = new Color(1f, .28f, .52f, 1f);
        public Color AimedHaloColor = new Color(1f, .08f, .25f, 1f);
        [Tooltip("The model faces +Z; the menu camera faces it from +Z.")]
        public float Yaw = -12f;
        [Range(1f, 2.5f)] public float ResolutionScale = 2f;
        [Range(512, 3072)] public int MaximumResolution = 2048;
        [Min(.1f)] public float CrossfadeSeconds = .65f;
        public Vector2 IdleInterval = new Vector2(6f, 11f);
    }
}
