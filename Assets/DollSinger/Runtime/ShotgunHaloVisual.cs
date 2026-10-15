using UnityEngine;

namespace Unity.MP_FPS.DollSinger
{
    /// <summary>ShotControl's four-round shell frames and four moving ShotDot strokes.</summary>
    [DefaultExecutionOrder(110)]
    public sealed class ShotgunHaloVisual : MonoBehaviour
    {
        public SpriteRenderer shell;
        public SpriteRenderer[] dots;
        public Sprite[] shellFrames;
        public Color lightColor = new Color(.32f, .82f, .34f);
        [Min(.001f)] public float pixelSize = .001f;
        [Min(.01f)] public float idleScale = .82f;
        [Header("Two-hand release")]
        [Min(0f)] public float wristTravel = .030f;
        [Range(0f, 15f)] public float wristAngle = 6f;
        private HaloShotResponse response;
        public float ShotRelease => response.Release;
        public bool IsReloading => reloading;
        public float ReloadProgress => progress;
        private float aimBlend, innerRadius = -50f, outerScale = 1.5f, shotTime, progress;
        private uint lastShot, lastReload;
        private int capacity = 4;
        private HaloModule modules;
        public void SetModuleProfile(byte mask, int magazine)
        { modules = (HaloModule)mask; capacity = Mathf.Max(1, magazine); }
        private int ammo = 4;
        private int reloadTarget = 4;
        private bool reloading, initialized;

        public void SetAimBlend(float value) => aimBlend = Mathf.Clamp01(value);
        public void SetEquipped(bool equipped, int rounds)
        {
            if (gameObject.activeSelf == equipped) return;
            ammo = Mathf.Max(0, rounds); shotTime = 0; progress = 0; initialized = false;
            innerRadius = -50f; outerScale = 1.5f; reloading = false;
            response.Reset();
            gameObject.SetActive(equipped); Refresh();
        }
        public void SetNetworkState(int rounds, bool reload, float reloadProgress, uint shotTick, uint reloadTick, int targetAmmo = -1)
        {
            if (!gameObject.activeSelf) return;
            if (!initialized) { lastShot = shotTick; lastReload = reloadTick; initialized = true; }
            if (Newer(lastShot, shotTick) || Newer(lastReload, reloadTick)) return;
            if (Newer(shotTick, lastShot)) { PlayShot(shotTick); lastShot = shotTick; }
            if (Newer(reloadTick, lastReload)) { progress = 0; lastReload = reloadTick; }
            if (reload) reloadTarget = Mathf.Max(rounds, Mathf.Min(targetAmmo < 0 ? capacity : targetAmmo, capacity));
            if (reload) progress = Mathf.Max(progress, Mathf.Clamp01(reloadProgress));
            else if (rounds == reloadTarget && lastReload != 0) progress = 1f;
            if (reload && progress >= 1f) rounds = reloadTarget;
            reloading = reload && progress < 1f; ammo = Mathf.Max(0, rounds); Refresh();
        }
        private static bool Newer(uint tick, uint previous) => tick != 0 && (previous == 0 || (int)(tick - previous) > 0);
        public void PlayShot(uint shotTick = 0) { if (response.Play(shotTick)) shotTime = .12f; }
        public void SetLocalState(int rounds, bool reload, float reloadProgress)
        { ammo = Mathf.Max(0, rounds); reloadTarget = capacity; reloading = reload; progress = reloadProgress; Refresh(); }
        private void Refresh()
        {
            int visibleAmmo = reloading ? Mathf.FloorToInt(Mathf.Lerp(ammo, reloadTarget, progress)) : ammo;
            if (shell && shellFrames?.Length == 5) shell.sprite = shellFrames[4 - Mathf.Clamp(Mathf.CeilToInt(4f * visibleAmmo / capacity), 0, 4)];
        }
        private void Update() => Step(Time.deltaTime);
        public void Step(float deltaTime)
        {
            float dt = Mathf.Max(0, deltaTime);
            response.Step(dt, 9f, 50f);
            float blend = 1f - Mathf.Pow(shotTime > 0f || reloading ? .5f : .75f, dt * 60f);
            float innerTarget = reloading ? -50f : shotTime > 0 ? 125f : Mathf.Lerp(-50f, 0f, aimBlend);
            float outerTarget = reloading ? 1.5f : shotTime > 0 ? 1.3f : Mathf.Lerp(1.5f, 1f, aimBlend);
            innerRadius = Mathf.Lerp(innerRadius, innerTarget, blend);
            outerScale = Mathf.Lerp(outerScale, outerTarget, blend);
            shotTime = Mathf.Max(0f, shotTime - dt);
            ApplyLayout();
        }
        public void ApplyLayout()
        {
            float scale = Mathf.Lerp(idleScale, 1f, aimBlend);
            float shapeScale = (modules & HaloModule.FocusLens) != 0 ? Mathf.Lerp(1f, .82f, aimBlend) :
                (modules & HaloModule.FanArray) != 0 ? 1.12f : 1f;
            if (shell) shell.transform.localScale = Vector3.one * outerScale * scale * shapeScale;
            if (dots == null) return;
            for (int i = 0; i < dots.Length; i++)
            {
                if (!dots[i]) continue;
                float angle = i * 90f;
                // GML lengthdir_y uses -sin in screen space; Unity's upward Y restores +sin.
                float placement = (angle + 135f) * Mathf.Deg2Rad;
                dots[i].transform.localPosition = new Vector3(Mathf.Cos(placement), Mathf.Sin(placement), -.001f) * innerRadius * pixelSize * scale * shapeScale;
                dots[i].transform.localRotation = Quaternion.Euler(0, 0, angle);
                dots[i].transform.localScale = Vector3.one * scale;
            }
        }
    }
}
