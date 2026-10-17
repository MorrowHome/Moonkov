using UnityEngine;
using UnityEngine.Rendering;

namespace Unity.MP_FPS.DollSinger
{
    /// <summary>Solid flat iris plates on the existing iris / capacitor animation.</summary>
    [DefaultExecutionOrder(110)]
    public sealed class RifleHaloVisual : MonoBehaviour
    {
        public const int Capacity = 30;
        public Material lineMaterial;
        public Color lightColor = new Color(1f, .18f, .42f);
        [Range(.3f, 1f)] public float idleScale = .68f;
        [Header("Halo size (sight dot stays small)")]
        [Min(.1f)] public float overallScale = 1.75f;
        [Min(.01f)] public float ringRadius = .135f;
        [Header("Shot response")]
        [Min(0f)] public float releaseExpansion = .019f;
        [Min(0f)] public float wristTravel = .016f;
        [Range(0f, 12f)] public float wristAngle = 5f;
        [Header("Flat ink artwork")]
        public Material artworkMaterial;
        public Sprite petalSprite, outerArcSprite, innerArcSprite, cellSprite;
        public SpriteRenderer[] petals, outerInk, innerInk, magazineInk;
        public SpriteRenderer chargeInk;
        public LineRenderer sight;

        private readonly Vector3[] sightPoints = new Vector3[17];
        private float aimBlend, elapsed, shotPulse, releaseSlow, releaseFast, pressure, lockFlash, rotation, reloadStartRotation, progress;
        private uint lastFeedbackTick;
        private int magazineCapacity = Capacity;
        public void SetMagazineCapacity(int capacity) => magazineCapacity = Mathf.Max(1, capacity);
        private int ammo = Capacity, startAmmo, targetAmmo = Capacity;
        private uint lastShot, lastReload;
        private bool initialized, reloading, localReloading;
        public int VisibleAmmo => Mathf.Clamp(reloading ? Mathf.FloorToInt(Mathf.Lerp(startAmmo, targetAmmo,
            Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.18f, .82f, progress)))) : ammo, 0, magazineCapacity);
        public float ReloadProgress => progress;
        public float RotorRotation => rotation;
        public bool IsReloading => reloading;
        public float ShotRelease => Mathf.Clamp01((releaseSlow - releaseFast) * 1.8f);
        public float ShotPressure => pressure;

        // Called by the asset installer only. Runtime only poses these fixed sprites.
        public void BuildGeometry()
        {
            petals = CreateInk("Iris plate", 6, petalSprite);
            outerInk = CreateInk("Outer bridge", 6, outerArcSprite);
            innerInk = CreateInk("Inner bracket", 3, innerArcSprite);
            magazineInk = CreateInk("Energy cell", Capacity, cellSprite);
            sight = CreateLine("Sight dot", 17, .0015f);
            chargeInk = CreateInk("Charge relay", 1, outerArcSprite)[0];
            ApplyLayout();
        }
        private SpriteRenderer[] CreateInk(string name, int count, Sprite sprite)
        {
            var ink = new SpriteRenderer[count];
            for (int i = 0; i < count; i++)
            {
                var child = new GameObject(name + " " + i); child.transform.SetParent(transform, false);
                ink[i] = child.AddComponent<SpriteRenderer>();
                ink[i].sprite = sprite; ink[i].sharedMaterial = artworkMaterial;
                ink[i].shadowCastingMode = ShadowCastingMode.Off; ink[i].receiveShadows = false;
            }
            return ink;
        }
        private LineRenderer CreateLine(string name, int points, float width)
        {
            var child = new GameObject(name); child.transform.SetParent(transform, false);
            var line = child.AddComponent<LineRenderer>(); line.useWorldSpace = false;
            line.sharedMaterial = lineMaterial; line.positionCount = points; line.widthMultiplier = width;
            line.numCapVertices = 2; line.numCornerVertices = 2; line.alignment = LineAlignment.TransformZ;
            line.shadowCastingMode = ShadowCastingMode.Off; line.receiveShadows = false;
            return line;
        }
        public void SetAimBlend(float value) { aimBlend = Mathf.Clamp01(value); }
        public void SetEquipped(bool equipped, int rounds)
        {
            if (gameObject.activeSelf == equipped) return;
            ammo = Mathf.Max(0, rounds); startAmmo = ammo; targetAmmo = ammo;
            rotation = reloadStartRotation = progress = elapsed = shotPulse = lockFlash = 0;
            releaseSlow = releaseFast = pressure = 0; lastFeedbackTick = 0;
            reloading = localReloading = initialized = false; lastShot = lastReload = 0;
            gameObject.SetActive(equipped); if (equipped) ApplyLayout();
        }
        public void SetNetworkState(int rounds, bool reload, float reloadProgress, uint shotTick, uint reloadTick, int reloadTarget)
        {
            if (!gameObject.activeSelf) return;
            rounds = Mathf.Max(0, rounds);
            if (!initialized) { lastShot = shotTick; lastReload = reloadTick; ammo = rounds; initialized = true; }
            if (Newer(lastShot, shotTick) || Newer(lastReload, reloadTick)) return;
            bool shot = Newer(shotTick, lastShot), newReload = Newer(reloadTick, lastReload);
            if (shot) { rotation += 360f / Capacity; PlayShot(shotTick); lastShot = shotTick; }
            if (newReload || reload && !reloading && progress == 0)
            { BeginReload(rounds, reloadTarget < 0 ? magazineCapacity : reloadTarget); lastReload = reloadTick; }
            if (reload && !Newer(lastShot, lastReload)) AdvanceReload(reloadProgress);
            if (!reload && reloading) { if (rounds > startAmmo) AdvanceReload(1); else reloading = false; }
            // Replayed snapshots for a completed reload must not briefly show an empty magazine.
            ammo = !shot && progress >= 1 && !Newer(lastShot, lastReload) ? Mathf.Max(rounds, targetAmmo) : rounds;
        }
        private static bool Newer(uint tick, uint previous) => tick != 0 && (previous == 0 || (int)(tick - previous) > 0);
        private void BeginReload(int rounds, int target)
        {
            startAmmo = rounds; targetAmmo = Mathf.Max(rounds, Mathf.Min(target, magazineCapacity));
            reloadStartRotation = rotation; progress = 0; reloading = true;
        }
        private void AdvanceReload(float value)
        {
            if (progress < 1 && value >= 1) lockFlash = 1;
            progress = Mathf.Max(progress, Mathf.Clamp01(value));
            rotation = reloadStartRotation + 420f * Mathf.SmoothStep(0, 1, progress);
            reloading = progress < 1;
            if (!reloading) ammo = targetAmmo;
        }
        public void SetLocalState(int rounds, bool reload, float reloadProgress, int reloadTarget = Capacity)
        {
            if (!reload && !localReloading && rounds < ammo) rotation += (ammo - rounds) * 360f / Capacity;
            if (reload && !localReloading) BeginReload(rounds, reloadTarget);
            if (reload) AdvanceReload(reloadProgress);
            else if (localReloading && rounds > startAmmo) AdvanceReload(1);
            else reloading = false;
            localReloading = reload; ammo = Mathf.Max(0, rounds);
        }
        public void PlayShot(uint shotTick = 0)
        {
            // Snapshot and predicted/RPC effects describe the same release. Never restart it.
            if (shotTick != 0 && !Newer(shotTick, lastFeedbackTick)) return;
            if (shotTick != 0) lastFeedbackTick = shotTick;
            shotPulse = 1;
            float impulse = Mathf.Min(1f, 2f - releaseSlow);
            releaseSlow += impulse;
            releaseFast += impulse;
            pressure = Mathf.Min(1f, pressure + .22f);
        }
        // Hands solve in LateUpdate after this frame's envelope has advanced.
        private void Update() => Step(Time.deltaTime);
        public void Step(float deltaTime)
        {
            elapsed += Mathf.Max(0, deltaTime); shotPulse *= Mathf.Exp(-Mathf.Max(0, deltaTime) * 24f);
            float dt = Mathf.Max(0, deltaTime);
            releaseSlow *= Mathf.Exp(-dt * 18f); releaseFast *= Mathf.Exp(-dt * 65f);
            pressure *= Mathf.Exp(-dt * 3.5f);
            lockFlash *= Mathf.Exp(-Mathf.Max(0, deltaTime) * 15f); ApplyLayout();
        }
        private static Vector3 Polar(float radius, float degrees, float z = 0)
        { float a = degrees * Mathf.Deg2Rad; return new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, z); }
        private static void PoseInk(SpriteRenderer ink, float radius, float angle, float scale, Color color, float alpha,
            float width = 1f, float length = 1f)
        {
            ink.transform.localPosition = Polar(radius * scale, angle);
            ink.transform.localRotation = Quaternion.Euler(0, 0, angle);
            ink.transform.localScale = new Vector3(scale * width, scale * length, 1);
            color.a = alpha; ink.color = color;
        }
        public void ApplyLayout()
        {
            if (petals == null || petals.Length != 6 || outerInk == null || innerInk == null || magazineInk == null) return;
            float scale = overallScale * Mathf.Lerp(idleScale, 1, aimBlend);
            float open = reloading ? Mathf.SmoothStep(0, 1, progress / .18f) * (1 - Mathf.SmoothStep(0, 1, (progress - .82f) / .18f)) : 0;
            float expansion = .046f * open + releaseExpansion * ShotRelease * (1 + pressure * .25f);
            float rotor = rotation + (reloading ? 0 : elapsed * 3f);
            float innerRotor = -rotation * .7f - elapsed * 5f;
            Color core = Color.Lerp(lightColor, Color.white, Mathf.Max(shotPulse * .45f, lockFlash * .65f));
            int visible = Mathf.CeilToInt((float)VisibleAmmo / magazineCapacity * magazineInk.Length);
            for (int i = 0; i < petals.Length; i++)
            {
                float angle = i * 60 + rotor;
                PoseInk(petals[i], ringRadius + .036f + expansion, angle, scale, core, 1f);
                PoseInk(outerInk[i], ringRadius + expansion * .65f, angle + 33.5f, scale, core, 1f);
            }
            for (int i = 0; i < innerInk.Length; i++)
            {
                PoseInk(innerInk[i], ringRadius - .045f - open * .012f - shotPulse * .008f,
                    innerRotor + i * 120 + 59, scale, core, 1f);
            }
            for (int i = 0; i < magazineInk.Length; i++)
            {
                // Six groups of five: exact ammo feedback without a dense picket-fence rim.
                float angle = i / 5 * 60f + i % 5 * 8f + 94f;
                magazineInk[i].enabled = i < visible;
                PoseInk(magazineInk[i], ringRadius - .021f + expansion * .4f, angle, scale,
                    core, 1f,
                    i % 5 == 0 ? .92f : .62f, .42f);
            }
            if (sight)
            {
                for (int i = 0; i < sightPoints.Length; i++)
                    sightPoints[i] = Polar(Mathf.Lerp(.012f, .0023f, aimBlend), 360f * i / (sightPoints.Length - 1));
                sight.SetPositions(sightPoints);
                var color = core; color.a = 1f;
                sight.startColor = sight.endColor = color;
            }
            if (chargeInk)
            {
                chargeInk.enabled = reloading;
                PoseInk(chargeInk, ringRadius + .040f + expansion, 108 - progress * 720, scale,
                    core, open, 1.5f);
            }
        }
    }
}
