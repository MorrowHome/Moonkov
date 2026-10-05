using UnityEngine;

namespace Unity.MP_FPS.DollSinger
{
    /// <summary>ShotControl's four-round shell frames and four moving ShotDot strokes.</summary>
    public sealed class ShotgunHaloVisual : MonoBehaviour
    {
        public SpriteRenderer shell;
        public SpriteRenderer[] dots;
        public Sprite[] shellFrames;
        public Color lightColor = new Color(.32f, .82f, .34f);
        [Min(.001f)] public float pixelSize = .001f;
        [Min(.01f)] public float idleScale = .82f;
        private float aimBlend, innerRadius = -50f, outerScale = 1.5f, shotTime, progress;
        private uint lastShot, lastReload;
        private int ammo = 4;
        private int reloadTarget = 4;
        private bool reloading, initialized;

        public void SetAimBlend(float value) => aimBlend = Mathf.Clamp01(value);
        public void SetEquipped(bool equipped, int rounds)
        {
            if (gameObject.activeSelf == equipped) return;
            ammo = Mathf.Clamp(rounds, 0, 4); shotTime = 0; progress = 0; initialized = false;
            innerRadius = -50f; outerScale = 1.5f; reloading = false;
            gameObject.SetActive(equipped); Refresh();
        }
        public void SetNetworkState(int rounds, bool reload, float reloadProgress, uint shotTick, uint reloadTick, int targetAmmo = -1)
        {
            if (!gameObject.activeSelf) return;
            if (!initialized) { lastShot = shotTick; lastReload = reloadTick; initialized = true; }
            if (Newer(lastShot, shotTick) || Newer(lastReload, reloadTick)) return;
            if (Newer(shotTick, lastShot)) { PlayShot(); lastShot = shotTick; }
            if (Newer(reloadTick, lastReload)) { progress = 0; lastReload = reloadTick; }
            if (reload) reloadTarget = Mathf.Clamp(targetAmmo < 0 ? 4 : targetAmmo, rounds, 4);
            if (reload) progress = Mathf.Max(progress, Mathf.Clamp01(reloadProgress));
            else if (rounds == reloadTarget && lastReload != 0) progress = 1f;
            if (reload && progress >= 1f) rounds = reloadTarget;
            reloading = reload && progress < 1f; ammo = Mathf.Clamp(rounds, 0, 4); Refresh();
        }
        private static bool Newer(uint tick, uint previous) => tick != 0 && (previous == 0 || (int)(tick - previous) > 0);
        public void PlayShot() => shotTime = .12f;
        public void SetLocalState(int rounds, bool reload, float reloadProgress)
        { ammo = Mathf.Clamp(rounds, 0, 4); reloadTarget = 4; reloading = reload; progress = reloadProgress; Refresh(); }
        private void Refresh()
        {
            int visibleAmmo = reloading ? Mathf.FloorToInt(Mathf.Lerp(ammo, reloadTarget, progress)) : ammo;
            if (shell && shellFrames?.Length == 5) shell.sprite = shellFrames[4 - Mathf.Clamp(visibleAmmo, 0, 4)];
        }
        private void LateUpdate()
        {
            float blend = 1f - Mathf.Pow(shotTime > 0f || reloading ? .5f : .75f, Time.deltaTime * 60f);
            float innerTarget = reloading ? -50f : shotTime > 0 ? 125f : Mathf.Lerp(-50f, 0f, aimBlend);
            float outerTarget = reloading ? 1.5f : shotTime > 0 ? 1.3f : Mathf.Lerp(1.5f, 1f, aimBlend);
            innerRadius = Mathf.Lerp(innerRadius, innerTarget, blend);
            outerScale = Mathf.Lerp(outerScale, outerTarget, blend);
            shotTime = Mathf.Max(0f, shotTime - Time.deltaTime);
            ApplyLayout();
        }
        public void ApplyLayout()
        {
            float scale = Mathf.Lerp(idleScale, 1f, aimBlend);
            if (shell) shell.transform.localScale = Vector3.one * outerScale * scale;
            if (dots == null) return;
            for (int i = 0; i < dots.Length; i++)
            {
                if (!dots[i]) continue;
                float angle = i * 90f;
                // GML lengthdir_y uses -sin in screen space; Unity's upward Y restores +sin.
                float placement = (angle + 135f) * Mathf.Deg2Rad;
                dots[i].transform.localPosition = new Vector3(Mathf.Cos(placement), Mathf.Sin(placement), -.001f) * innerRadius * pixelSize * scale;
                dots[i].transform.localRotation = Quaternion.Euler(0, 0, angle);
                dots[i].transform.localScale = Vector3.one * scale;
            }
        }
    }
}
