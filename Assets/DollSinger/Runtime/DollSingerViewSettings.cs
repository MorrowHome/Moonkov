using UnityEngine;

namespace Unity.MP_FPS.DollSinger
{
    /// <summary>Local player preferences shared by every deployment and perspective.</summary>
    public static class DollSingerViewSettings
    {
        public const float DefaultMouse = .15f, DefaultAim = 1f, DefaultFov = 75f;
        public const string MouseKey = "Moonkov.Look.Mouse", AimKey = "Moonkov.Look.Aim", FovKey = "Moonkov.Look.Fov";
        private static bool loaded;
        private static float mouse, aim, fov;
        public static int Revision { get; private set; }
        public static float MouseSensitivity { get { Load(); return mouse; } }
        public static float AimSensitivity { get { Load(); return aim; } }
        public static float WorldFieldOfView { get { Load(); return fov; } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Reload() { loaded = false; Revision++; }
        private static float Bounded(float value, float fallback, float min, float max)
            => Mathf.Clamp(float.IsFinite(value) ? value : fallback, min, max);
        private static void Load()
        {
            if (loaded) return;
            loaded = true;
            mouse = Bounded(PlayerPrefs.GetFloat(MouseKey, DefaultMouse), DefaultMouse, .02f, .6f);
            aim = Bounded(PlayerPrefs.GetFloat(AimKey, DefaultAim), DefaultAim, .1f, 2f);
            fov = Bounded(PlayerPrefs.GetFloat(FovKey, DefaultFov), DefaultFov, 40f, 100f);
        }
        public static void Set(float mouseSensitivity, float aimSensitivity, float worldFov)
        {
            Load();
            mouse = Bounded(mouseSensitivity, DefaultMouse, .02f, .6f);
            aim = Bounded(aimSensitivity, DefaultAim, .1f, 2f);
            fov = Bounded(worldFov, DefaultFov, 40f, 100f);
            PlayerPrefs.SetFloat(MouseKey, mouse);
            PlayerPrefs.SetFloat(AimKey, aim);
            PlayerPrefs.SetFloat(FovKey, fov);
            Revision++;
        }
        public static void Save() => PlayerPrefs.Save();
        public static void RestoreDefaults() { Set(DefaultMouse, DefaultAim, DefaultFov); Save(); }
    }
}
