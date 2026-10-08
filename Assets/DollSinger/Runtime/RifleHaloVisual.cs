using UnityEngine;
using UnityEngine.Rendering;

namespace Unity.MP_FPS.DollSinger
{
    /// <summary>Six-blade iris, counter-rotating rings and a thirty-cell capacitor magazine.</summary>
    [DefaultExecutionOrder(110)]
    public sealed class RifleHaloVisual : MonoBehaviour
    {
        public const int Capacity = 30;
        public Material lineMaterial;
        public Color lightColor = new Color(1f, .18f, .42f);
        [Range(.3f, 1f)] public float idleScale = .68f;
        [Header("Halo size (sight dot stays small)")]
        [Min(.1f)] public float overallScale = 1.35f;
        [Min(.001f)] public float lineWidth = .0055f;
        [Min(.01f)] public float ringRadius = .135f;
        public LineRenderer[] blades, outerArcs, innerArcs, cells;
        public LineRenderer sight, chargeSweep;

        private readonly Vector3[] bladePoints = new Vector3[5], arcPoints = new Vector3[17], cellPoints = new Vector3[2];
        private float aimBlend, elapsed, shotPulse, lockFlash, rotation, reloadStartRotation, progress;
        private int ammo = Capacity, startAmmo, targetAmmo = Capacity;
        private uint lastShot, lastReload;
        private bool initialized, reloading, localReloading;
        public int VisibleAmmo => Mathf.Clamp(reloading ? Mathf.FloorToInt(Mathf.Lerp(startAmmo, targetAmmo,
            Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.18f, .82f, progress)))) : ammo, 0, Capacity);
        public float ReloadProgress => progress;
        public float RotorRotation => rotation;
        public bool IsReloading => reloading;

        // Called by the asset installer only. Runtime reuses these fixed renderers and point buffers.
        public void BuildGeometry()
        {
            blades = CreateLines("Blade", 6, 5, lineWidth);
            outerArcs = CreateLines("Outer ring", 6, 17, lineWidth);
            innerArcs = CreateLines("Inner ring", 3, 17, lineWidth * .65f);
            cells = CreateLines("Energy cell", Capacity, 2, lineWidth * 1.2f);
            sight = CreateLine("Sight dot", 17, lineWidth * .6f);
            chargeSweep = CreateLine("Charge relay", 17, lineWidth * 1.5f);
            ApplyLayout();
        }
        private LineRenderer[] CreateLines(string name, int count, int points, float width)
        {
            var lines = new LineRenderer[count];
            for (int i = 0; i < count; i++) lines[i] = CreateLine(name + " " + i, points, width);
            return lines;
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
            ammo = Mathf.Clamp(rounds, 0, Capacity); startAmmo = ammo; targetAmmo = ammo;
            rotation = reloadStartRotation = progress = elapsed = shotPulse = lockFlash = 0;
            reloading = localReloading = initialized = false; lastShot = lastReload = 0;
            gameObject.SetActive(equipped); if (equipped) ApplyLayout();
        }
        public void SetNetworkState(int rounds, bool reload, float reloadProgress, uint shotTick, uint reloadTick, int reloadTarget)
        {
            if (!gameObject.activeSelf) return;
            rounds = Mathf.Clamp(rounds, 0, Capacity);
            if (!initialized) { lastShot = shotTick; lastReload = reloadTick; ammo = rounds; initialized = true; }
            if (Newer(lastShot, shotTick) || Newer(lastReload, reloadTick)) return;
            bool shot = Newer(shotTick, lastShot), newReload = Newer(reloadTick, lastReload);
            if (shot) { rotation += 360f / Capacity; PlayShot(); lastShot = shotTick; }
            if (newReload || reload && !reloading && progress == 0)
            { BeginReload(rounds, reloadTarget < 0 ? Capacity : reloadTarget); lastReload = reloadTick; }
            if (reload && !Newer(lastShot, lastReload)) AdvanceReload(reloadProgress);
            if (!reload && reloading) { if (rounds > startAmmo) AdvanceReload(1); else reloading = false; }
            // Replayed snapshots for a completed reload must not briefly show an empty magazine.
            ammo = !shot && progress >= 1 && !Newer(lastShot, lastReload) ? Mathf.Max(rounds, targetAmmo) : rounds;
        }
        private static bool Newer(uint tick, uint previous) => tick != 0 && (previous == 0 || (int)(tick - previous) > 0);
        private void BeginReload(int rounds, int target)
        {
            startAmmo = rounds; targetAmmo = Mathf.Clamp(target, rounds, Capacity);
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
            localReloading = reload; ammo = Mathf.Clamp(rounds, 0, Capacity);
        }
        public void PlayShot() { shotPulse = 1; }
        private void LateUpdate() => Step(Time.deltaTime);
        public void Step(float deltaTime)
        {
            elapsed += Mathf.Max(0, deltaTime); shotPulse *= Mathf.Exp(-Mathf.Max(0, deltaTime) * 24f);
            lockFlash *= Mathf.Exp(-Mathf.Max(0, deltaTime) * 15f); ApplyLayout();
        }
        private static Vector3 Polar(float radius, float degrees, float z = 0)
        { float a = degrees * Mathf.Deg2Rad; return new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, z); }
        private static void Tint(LineRenderer line, Color color, float alpha)
        { color.a = alpha; line.startColor = line.endColor = color; }
        private void Arc(LineRenderer line, float radius, float from, float span, float z = 0)
        {
            if (!line) return;
            for (int i = 0; i < arcPoints.Length; i++) arcPoints[i] = Polar(radius, from + span * i / (arcPoints.Length - 1), z);
            line.SetPositions(arcPoints);
        }
        public void ApplyLayout()
        {
            if (blades == null || outerArcs == null || innerArcs == null || cells == null) return;
            float scale = overallScale * Mathf.Lerp(idleScale, 1, aimBlend);
            float open = reloading ? Mathf.SmoothStep(0, 1, progress / .18f) * (1 - Mathf.SmoothStep(0, 1, (progress - .82f) / .18f)) : 0;
            float expansion = .046f * open + .010f * shotPulse;
            float rotor = rotation + (reloading ? 0 : elapsed * 3f);
            float innerRotor = -rotation * .7f - elapsed * 5f;
            Color core = Color.Lerp(lightColor, Color.white, Mathf.Max(shotPulse * .45f, lockFlash * .65f));
            int visible = VisibleAmmo;
            for (int i = 0; i < blades.Length; i++)
            {
                float angle = i * 60 + rotor;
                bladePoints[0] = Polar((ringRadius + expansion) * scale, angle - 12);
                bladePoints[1] = Polar((ringRadius + .054f + expansion) * scale, angle - 7);
                bladePoints[2] = Polar((ringRadius + .073f + expansion) * scale, angle + 1, open * .024f);
                bladePoints[3] = Polar((ringRadius + .034f + expansion) * scale, angle + 12);
                bladePoints[4] = bladePoints[0];
                blades[i].SetPositions(bladePoints); Tint(blades[i], core, .75f);
                Arc(outerArcs[i], (ringRadius + expansion * .65f) * scale, angle + 17, 33, -open * .020f);
                Tint(outerArcs[i], core, .8f);
            }
            for (int i = 0; i < innerArcs.Length; i++)
            {
                Arc(innerArcs[i], (ringRadius - .045f - open * .012f) * scale, innerRotor + i * 120 + 20, 78, open * .018f);
                Tint(innerArcs[i], Color.Lerp(lightColor, Color.white, .35f), .45f);
            }
            for (int i = 0; i < cells.Length; i++)
            {
                float angle = i * 360f / Capacity + 90;
                float r = (ringRadius + .018f + expansion * .4f) * scale;
                cellPoints[0] = Polar(r, angle); cellPoints[1] = Polar(r + .012f * scale, angle);
                cells[i].SetPositions(cellPoints); Tint(cells[i], i < visible ? core : lightColor, i < visible ? .95f : .09f);
            }
            Arc(sight, Mathf.Lerp(.012f, .0023f, aimBlend), 0, 360);
            if (sight) Tint(sight, Color.Lerp(lightColor, Color.white, aimBlend * .65f), .95f);
            if (chargeSweep)
            {
                chargeSweep.enabled = reloading;
                Arc(chargeSweep, (ringRadius + .040f + expansion) * scale, 90 - progress * 720, 36);
                Tint(chargeSweep, Color.Lerp(lightColor, Color.white, .65f), .9f * open);
            }
        }
    }
}
