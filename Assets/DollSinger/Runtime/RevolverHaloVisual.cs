using UnityEngine;

namespace Unity.MP_FPS.DollSinger
{
    /// <summary>GameMaker RevControl/Revolver presentation, driven by gameplay ammo.</summary>
    public sealed class RevolverHaloVisual : MonoBehaviour
    {
        public SpriteRenderer centre;
        public SpriteRenderer[] chambers;
        public Sprite loadedSprite;
        public Sprite emptySprite;
        [Min(0.001f)] public float restingRadius = 0.10f;
        [Min(0.001f)] public float idleRadius = 0.06f;
        [Min(0.001f)] public float shotRadius = 0.16f;
        public Color lightColor = new Color(1f, 0.78f, 0.28f);
        [Min(0.01f)] public float idleCentreScale = 1f;
        [Range(0.01f, 1f)] public float aimedCentreScale = 0.08f;

        private int previousAmmo = 6;
        private bool wasReloading;
        private float radius;
        private float angle;
        private float targetAngle;
        private float reloadStartAngle;
        private float shotExpansionTime;
        private float aimBlend;
        private float reloadProgress;
        private bool networkEventsInitialized;
        private uint lastShotTick;
        private uint lastReloadTick;

        public float TargetRotationDegrees => targetAngle;

        public void SetAimBlend(float value)
        {
            aimBlend = Mathf.Clamp01(value);
            if (centre) centre.transform.localScale = Vector3.one * Mathf.Lerp(idleCentreScale, aimedCentreScale, aimBlend);
        }

        public void SetEquipped(bool equipped, int ammo)
        {
            if (gameObject.activeSelf == equipped) return;
            previousAmmo = Mathf.Clamp(ammo, 0, 6);
            angle = targetAngle = (6 - previousAmmo) * 60f;
            wasReloading = false;
            radius = 0f;
            shotExpansionTime = 0f;
            reloadProgress = 0f;
            networkEventsInitialized = false;
            gameObject.SetActive(equipped);
            RefreshChambers(previousAmmo);
        }

        public void SetState(int ammo, bool reloading, float reloadProgress)
        {
            if (!gameObject.activeSelf) return;
            ammo = Mathf.Clamp(ammo, 0, 6);
            if (ammo < previousAmmo) targetAngle += (previousAmmo - ammo) * 60f;
            if (reloading)
            {
                if (!wasReloading)
                {
                    reloadStartAngle = targetAngle;
                    this.reloadProgress = 0f;
                }
                // Original spinAmount=420: one complete turn plus the next chamber.
                this.reloadProgress = Mathf.Max(this.reloadProgress, Mathf.Clamp01(reloadProgress));
                targetAngle = reloadStartAngle + 420f * this.reloadProgress;
            }
            else if (wasReloading && ammo > previousAmmo)
                targetAngle = reloadStartAngle + 420f;
            previousAmmo = ammo;
            wasReloading = reloading;
            RefreshChambers(ammo);
        }

        public void SetNetworkState(int ammo, bool reloading, float progress, uint shotTick, uint reloadTick)
        {
            if (!gameObject.activeSelf) return;
            ammo = Mathf.Clamp(ammo, 0, 6);
            if (!networkEventsInitialized)
            {
                networkEventsInitialized = true;
                lastShotTick = shotTick;
                lastReloadTick = reloadTick;
                reloadStartAngle = targetAngle;
                reloadProgress = 0f;
            }
            bool newShot = IsNewer(shotTick, lastShotTick);
            bool newReload = IsNewer(reloadTick, lastReloadTick);
            if (newShot)
            {
                if (!reloading && !newReload && reloadProgress > 0f && reloadProgress < 1f)
                {
                    targetAngle = reloadStartAngle + 420f;
                    reloadProgress = 1f;
                }
                targetAngle += 60f;
                lastShotTick = shotTick;
            }
            if (newReload)
            {
                lastReloadTick = reloadTick;
                reloadStartAngle = targetAngle;
                reloadProgress = 0f;
            }
            // Snapshot reconciliation may replay the same reload with an older timer.
            // A tick starts the turn once; presentation progress only moves forwards.
            if (reloading && reloadTick != 0 && reloadTick == lastReloadTick &&
                !IsNewer(lastShotTick, lastReloadTick))
            {
                reloadProgress = Mathf.Max(reloadProgress, Mathf.Clamp01(progress));
                targetAngle = reloadStartAngle + 420f * reloadProgress;
            }
            else if (!newShot && !reloading && ammo > 0 && reloadTick == lastReloadTick &&
                     reloadProgress > 0f && reloadProgress < 1f)
            {
                targetAngle = reloadStartAngle + 420f;
                reloadProgress = 1f;
            }
            if (reloading && reloadTick == lastReloadTick && reloadProgress >= 1f)
                ammo = Mathf.Max(previousAmmo, ammo);
            previousAmmo = ammo;
            RefreshChambers(ammo);
        }

        private static bool IsNewer(uint tick, uint previous) => tick != 0 && (previous == 0 || (int)(tick - previous) > 0);

        public void PlayShot() => shotExpansionTime = 0.23f;

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            // Convert the source's per-frame /4 easing to a frame-rate-independent 60 Hz response.
            float radialBlend = 1f - Mathf.Pow(0.75f, dt * 60f);
            float spinBlend = 1f - Mathf.Pow(0.9f, dt * 60f);
            float layoutRadius = Mathf.Lerp(idleRadius, restingRadius, aimBlend);
            radius = Mathf.Lerp(radius, shotExpansionTime > 0f ? shotRadius : layoutRadius, radialBlend);
            shotExpansionTime = Mathf.Max(0f, shotExpansionTime - dt);
            angle = Mathf.Lerp(angle, targetAngle, spinBlend);
            if (chambers == null) return;
            for (int i = 0; i < chambers.Length; i++)
            {
                var chamber = chambers[i];
                if (!chamber) continue;
                float rotation = (i + 2) * 60f + angle;
                float placement = (rotation + 60f) * Mathf.Deg2Rad;
                chamber.transform.localPosition = new Vector3(Mathf.Cos(placement), Mathf.Sin(placement), 0f) * radius;
                chamber.transform.localRotation = Quaternion.Euler(0f, 0f, rotation);
            }
        }

        private void RefreshChambers(int ammo)
        {
            if (chambers == null) return;
            for (int i = 0; i < chambers.Length; i++)
                if (chambers[i]) chambers[i].sprite = i < ammo ? loadedSprite : emptySprite;
        }
    }
}
