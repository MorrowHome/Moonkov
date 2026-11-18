// 角色预览用的资源配置。定义模型、动作、光环、武器数据和相机渲染规格，MenuCharacterView 及改装台都从这里取美术来源。
using UnityEngine;
using Unity.MP_FPS.DollSinger;

namespace Unity.MP_FPS.Client {
    [CreateAssetMenu(menuName = "Moonkov/Menu Character Settings")]
    public sealed class MenuCharacterSettings : ScriptableObject {
        // 视觉预览用这些模型与动画。角色战斗时的服务端状态不放在这份资产里。
        public GameObject VisualPrefab;

        [Header("Gameplay equipment appearance")]
        public DollSingerHaloAim HaloSource;

        public WeaponRegistry WeaponRegistry;

        [Header("Halo workshop preview sources")]
        // 改装台的预览用单独的光环来源。它可以演示模块而无需启动实际玩家。
        public GameObject RifleHaloPreviewPrefab;

        public GameObject ShotgunHaloPreviewPrefab;
        public AnimationClip[] Motions;
        public AvatarMask HaloAimMask;
        public AvatarMask[] MotionMasks;
        public MenuCharacterMotion[] MotionOptions;
        public TextAsset GestureBake;
        public Vector2 LoopInterval = new(3f, 5f);
        [Range(0f, 45f)] public float TurnDegrees = 18f;
        public Color IdleHaloColor = new(1f, .28f, .52f, 1f);
        public Color AimedHaloColor = new(1f, .08f, .25f, 1f);

        [Tooltip("The model faces +Z; the menu camera faces it from +Z.")]
        public float Yaw = -12f;

        [Range(1f, 2.5f)] public float ResolutionScale = 2f;
        [Range(512, 3072)] public int MaximumResolution = 2048;
        [Min(.1f)] public float CrossfadeSeconds = .65f;
        public Vector2 IdleInterval = new(6f, 11f);

        // 资源缺少某条动作的配置时用默认动作参数。播放器仍可以用 Clip，减少旧资源的迁移要求。
        public MenuCharacterMotion Motion(int index) {
            return MotionOptions != null && index < MotionOptions.Length
                ? MotionOptions[index]
                : new MenuCharacterMotion { Loop = index == 0, Turn = index == 2 ? -1 : index == 3 ? 1 : 0 };
        }
    }

    public enum MenuWeaponAction {
        Aim,
        Fire,
        Reload
    }

    [System.Serializable]
    public struct MenuCharacterMotion {
        public string Name;
        public bool Loop, PreserveHands, Tiptoe;
        public float HoldSeconds, Turn;
        public int Weapon;
        public MenuWeaponAction WeaponAction;
    }
}