using UnityEngine;
using Unity.MP_FPS.DollSinger;

namespace Unity.MP_FPS.Client
{
    [CreateAssetMenu(menuName = "Moonkov/Menu Character Settings")]
    public sealed class MenuCharacterSettings : ScriptableObject
    {
        public GameObject VisualPrefab;
        [Header("Gameplay equipment appearance")]
        public DollSingerHaloAim HaloSource;
        public WeaponRegistry WeaponRegistry;
        [Header("Halo workshop preview sources")]
        public GameObject RifleHaloPreviewPrefab;
        public GameObject ShotgunHaloPreviewPrefab;
        public AnimationClip[] Motions;
        public AvatarMask HaloAimMask;
        public AvatarMask[] MotionMasks;
        public MenuCharacterMotion[] MotionOptions;
        public TextAsset GestureBake;
        public Vector2 LoopInterval = new Vector2(3f, 5f);
        [Range(0f, 45f)] public float TurnDegrees = 18f;
        public Color IdleHaloColor = new Color(1f, .28f, .52f, 1f);
        public Color AimedHaloColor = new Color(1f, .08f, .25f, 1f);
        [Tooltip("The model faces +Z; the menu camera faces it from +Z.")]
        public float Yaw = -12f;
        [Range(1f, 2.5f)] public float ResolutionScale = 2f;
        [Range(512, 3072)] public int MaximumResolution = 2048;
        [Min(.1f)] public float CrossfadeSeconds = .65f;
        public Vector2 IdleInterval = new Vector2(6f, 11f);

        public MenuCharacterMotion Motion(int index) => MotionOptions != null && index < MotionOptions.Length
            ? MotionOptions[index] : new MenuCharacterMotion { Loop = index == 0, Turn = index == 2 ? -1 : index == 3 ? 1 : 0 };
    }

    public enum MenuWeaponAction { Aim, Fire, Reload }

    [System.Serializable]
    public struct MenuCharacterMotion
    {
        public string Name;
        public bool Loop, PreserveHands, Tiptoe;
        public float HoldSeconds, Turn;
        public int Weapon;
        public MenuWeaponAction WeaponAction;
    }
}
