using UnityEngine;

namespace Unity.MP_FPS.DollSinger
{
    /// <summary>Authored sprite assemblies; state comes exclusively from the equipped weapon.</summary>
    public sealed class ArsenalHaloVisual : MonoBehaviour
    {
        public DollSingerHaloAim.HaloWeapon weapon;
        public SpriteRenderer core, accent;
        public SpriteRenderer[] nodes = System.Array.Empty<SpriteRenderer>();
        public Color lightColor = new Color(1f, .89f, .63f);
        public bool sequenceNodes;
        public float magnification, recoil = .018f;
        private bool equipped, reloading, synchronized;
        private float progress, aim, shotAge = 10, phase;
        private int ammo, capacity, burstIndex;
        private uint lastShot, lastReload;
        public bool IsReloading => reloading;
        public float ReloadProgress => progress;
        public int Ammo => ammo;
        public uint LastShot => lastShot;
        public void SetEquipped(bool value)
        {
            if (equipped == value) return;
            equipped = value; gameObject.SetActive(value);
            if (!value) { shotAge = 10; lastShot = lastReload = 0; reloading = false; progress = 0; synchronized = false; }
        }
        public void SetAimBlend(float value) => aim = value;
        public void SetNetworkState(int count, int magazine, bool reload, float charge, uint shot, uint reloadTick, int target, int sequence)
        {
            ammo = Mathf.Max(0, count); capacity = Mathf.Max(1, magazine); burstIndex = sequence;
            if (reloadTick != lastReload) { lastReload = reloadTick; progress = 0; }
            reloading = reload; progress = reload ? Mathf.Max(progress, Mathf.Clamp01(charge)) : 0;
            if (!synchronized) { synchronized = true; lastShot = shot; } else PlayShot(shot);
        }
        public void PlayShot(uint tick)
        {
            if (!equipped || tick == 0 || (lastShot != 0 && unchecked((int)(tick-lastShot)) <= 0)) return;
            // Initial synchronization establishes a baseline, never invents a shot.
            shotAge = 0;
            lastShot = tick;
        }
        private void Update()
        {
            shotAge += Time.deltaTime; phase += Time.deltaTime;
            float pulse = Mathf.Exp(-shotAge * (weapon == DollSingerHaloAim.HaloWeapon.Cicada ? 36 : 16));
            float loaded = reloading ? progress : (float)ammo / capacity;
            var tint = Color.white * (ammo == 0 && !reloading ? .28f : 1f + pulse * .18f); tint.a = 1;
            if (core) { core.color = tint; core.transform.localScale = Vector3.one * (1 + pulse * .035f); }
            if (core && weapon == DollSingerHaloAim.HaloWeapon.Tempest) core.transform.localRotation=Quaternion.Euler(0,0,-phase*8);
            float rotation = weapon == DollSingerHaloAim.HaloWeapon.Tempest ? phase*35+pulse*15 : weapon == DollSingerHaloAim.HaloWeapon.Aegis ? phase * 9 + pulse * 6 :
                weapon == DollSingerHaloAim.HaloWeapon.Rondo ? (burstIndex==0?1:-1)*pulse*12 :
                weapon == DollSingerHaloAim.HaloWeapon.Thorn ? pulse * 4 : weapon == DollSingerHaloAim.HaloWeapon.Trine ? 0 : Mathf.Sin(phase) * 1.5f;
            if (accent)
            {
                accent.color = Color.Lerp(Color.gray * .35f, tint, .65f + loaded * .35f);
                accent.transform.localRotation = Quaternion.Euler(0, 0, rotation);
                accent.transform.localScale = Vector3.one * (reloading ? .78f + .22f * progress : 1 + pulse * .09f);
                accent.transform.localPosition = Vector3.back * .002f;
                if (weapon == DollSingerHaloAim.HaloWeapon.Cicada) accent.transform.localPosition += Vector3.right * (Mathf.Sin(phase * 45) * pulse * .009f);
                if (weapon == DollSingerHaloAim.HaloWeapon.Sundown) accent.transform.localPosition += Vector3.down * (.07f * pulse);
            }
            for (int i = 0; i < nodes.Length; i++) if (nodes[i])
            {
                bool lit = (i + .5f) / nodes.Length <= loaded;
                float sequential = (weapon == DollSingerHaloAim.HaloWeapon.Trine || sequenceNodes) && i == burstIndex ? pulse : 0;
                nodes[i].color = lit ? lightColor * (1.2f + sequential * 2) : lightColor * .2f;
            }
            transform.localPosition = Vector3.back * (pulse * recoil);
            transform.localRotation = Quaternion.Euler(0, 0, (weapon == DollSingerHaloAim.HaloWeapon.Aegis ? 3 : 1) * pulse);
        }
    }
}
