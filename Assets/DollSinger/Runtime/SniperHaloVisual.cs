using UnityEngine;
using UnityEngine.Rendering;

namespace Unity.MP_FPS.DollSinger
{
    /// <summary>Five capacitors surrounding an unfolding, counter-rotating halo optic.</summary>
    [DefaultExecutionOrder(110)]
    public sealed class SniperHaloVisual : MonoBehaviour
    {
        public const int Capacity = 5;
        public Material lineMaterial;
        public Color lightColor = new Color(.25f, .8f, 1f);
        [Min(.1f)] public float overallScale = 1f;
        [Range(1f, 12f)] public float scopeMagnification = 6f;
        [Range(.35f, .8f)] public float scopeViewportFraction = .7f;
        public float AimedRadius => .175f * overallScale;
        public float ScopeScale(float distance, float fieldOfView) => scopeViewportFraction * Mathf.Max(.01f, distance)
            * Mathf.Tan(fieldOfView * .5f * Mathf.Deg2Rad) / Mathf.Max(.001f, AimedRadius);
        [Min(.001f)] public float lineWidth = .004f;
        public LineRenderer[] outerArcs, innerArcs, capacitors, guides;
        public LineRenderer sight, chargeArc;
        private readonly Vector3[] arc = new Vector3[33], segment = new Vector3[2], diamond = new Vector3[5];
        private float aim, phase, shotAge = 10, progress, lockPulse;
        private int ammo = Capacity, startAmmo, targetAmmo;
        private uint lastShot, lastReload;
        private bool initialized, reloading, localReloading;
        public bool IsReloading => reloading;
        public float ReloadProgress => progress;
        public int VisibleAmmo => Mathf.Clamp(reloading ? Mathf.FloorToInt(Mathf.Lerp(startAmmo, targetAmmo,
            Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.15f, .85f, progress)))) : ammo, 0, Capacity);
        public void BuildGeometry()
        {
            outerArcs = CreateLines("Optic outer", 4, 33, lineWidth);
            innerArcs = CreateLines("Optic inner", 4, 33, lineWidth * .6f);
            capacitors = CreateLines("Capacitor", Capacity, 5, lineWidth * 1.2f);
            guides = CreateLines("Range guide", 4, 2, lineWidth * .45f);
            sight = CreateLines("Sight dot", 1, 33, lineWidth * .45f)[0];
            chargeArc = CreateLines("Charge sweep", 1, 33, lineWidth * 1.4f)[0];
            ApplyLayout();
        }
        private LineRenderer[] CreateLines(string name, int count, int vertices, float width)
        {
            var lines = new LineRenderer[count];
            for (int i = 0; i < count; i++)
            {
                var child = new GameObject(name + " " + i); child.transform.SetParent(transform, false);
                var line = child.AddComponent<LineRenderer>(); line.sharedMaterial = lineMaterial;
                line.positionCount = vertices; line.widthMultiplier = width; line.useWorldSpace = false;
                line.alignment = LineAlignment.TransformZ; line.numCapVertices = line.numCornerVertices = 2;
                line.shadowCastingMode = ShadowCastingMode.Off; line.receiveShadows = false; lines[i] = line;
            }
            return lines;
        }
        public void SetAimBlend(float value) => aim = Mathf.Clamp01(value);
        public void SetEquipped(bool equipped, int rounds)
        {
            if (gameObject.activeSelf == equipped) return;
            ammo = startAmmo = targetAmmo = Mathf.Clamp(rounds, 0, Capacity);
            initialized = reloading = localReloading = false; lastShot = lastReload = 0;
            progress = phase = lockPulse = 0; shotAge = 10; gameObject.SetActive(equipped);
            if (equipped) ApplyLayout();
        }
        private static bool Newer(uint value, uint previous) => value != 0 && (previous == 0 || (int)(value - previous) > 0);
        public void SetNetworkState(int rounds, bool reload, float reloadProgress, uint shotTick, uint reloadTick, int target)
        {
            if (!gameObject.activeSelf) return;
            rounds = Mathf.Clamp(rounds, 0, Capacity);
            if (!initialized) { lastShot = shotTick; lastReload = reloadTick; ammo = rounds; initialized = true; }
            if (Newer(lastShot, shotTick) || Newer(lastReload, reloadTick)) return;
            bool shot = Newer(shotTick, lastShot), newReload = Newer(reloadTick, lastReload);
            if (shot) { PlayShot(); lastShot = shotTick; }
            if (newReload || reload && !reloading && progress == 0)
            { BeginReload(rounds, target < 0 ? Capacity : target); lastReload = reloadTick; }
            if (reload && !Newer(lastShot, lastReload)) AdvanceReload(reloadProgress);
            if (!reload && reloading) { if (rounds > startAmmo) AdvanceReload(1); else reloading = false; }
            ammo = !shot && progress >= 1 && !Newer(lastShot, lastReload) ? Mathf.Max(rounds, targetAmmo) : rounds;
        }
        private void BeginReload(int rounds, int target)
        { startAmmo = rounds; targetAmmo = Mathf.Clamp(target, rounds, Capacity); progress = 0; reloading = true; }
        private void AdvanceReload(float value)
        {
            if (progress < 1 && value >= 1) lockPulse = 1;
            progress = Mathf.Max(progress, Mathf.Clamp01(value)); reloading = progress < 1;
            if (!reloading) ammo = targetAmmo;
        }
        public void SetLocalState(int rounds, bool reload, float reloadProgress)
        {
            if (reload && !localReloading) BeginReload(rounds, Capacity);
            if (reload) AdvanceReload(reloadProgress);
            else if (localReloading && rounds > startAmmo) AdvanceReload(1);
            else reloading = false;
            localReloading = reload; ammo = Mathf.Clamp(rounds, 0, Capacity);
        }
        // Shot feedback moves only the optic's outer mechanism, keeping the sight fixed.
        public void PlayShot() => shotAge = 0;
        private void LateUpdate() => Step(Time.deltaTime);
        public void Step(float deltaTime)
        { float dt = Mathf.Max(0, deltaTime); phase += dt; shotAge += dt; lockPulse *= Mathf.Exp(-dt * 12); ApplyLayout(); }
        private static Vector3 Polar(float r, float angle, float z = 0)
        { float a = angle * Mathf.Deg2Rad; return new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, z); }
        private static void Tint(LineRenderer line, Color color, float alpha)
        { color.a = alpha; line.startColor = line.endColor = color; }
        private void Arc(LineRenderer line, float radius, float from, float degrees, float z)
        {
            for (int i = 0; i < arc.Length; i++) arc[i] = Polar(radius, from + degrees * i / (arc.Length - 1), z);
            line.SetPositions(arc);
        }
        public void ApplyLayout()
        {
            if (outerArcs == null || outerArcs.Length != 4) return;
            float unfold = Mathf.SmoothStep(0, 1, aim);
            float reloadOpen = reloading ? Mathf.Sin(progress * Mathf.PI) : 0;
            float kick = Mathf.Exp(-shotAge * 24);
            // A short unlock / rearward extraction / return cycle between single shots.
            float bolt = shotAge < .9f ? Mathf.Sin(Mathf.Clamp01((shotAge - .12f) / .78f) * Mathf.PI) : 0;
            float radius = Mathf.Lerp(.115f, .175f, unfold) * overallScale + .018f * reloadOpen;
            float turn = reloading ? progress * 540 : phase * 2 + bolt * 24;
            Color core = Color.Lerp(lightColor, Color.white, Mathf.Max(kick * .65f, lockPulse * .7f));
            for (int i = 0; i < 4; i++)
            {
                Arc(outerArcs[i], radius + kick * .008f, i * 90 + 8 + turn, 65, -.035f * unfold - .045f * bolt);
                Tint(outerArcs[i], core, .9f);
                Arc(innerArcs[i], radius * .83f, i * 90 + 11 - turn * .7f, 61, .030f * unfold);
                Tint(innerArcs[i], core, .6f);
                // Peripheral range marks leave the central target unobstructed.
                segment[0] = Polar(radius * .58f, i * 90);
                segment[1] = Polar(radius * .74f, i * 90);
                guides[i].SetPositions(segment); Tint(guides[i], core, unfold * .75f);
            }
            for (int i = 0; i < Capacity; i++)
            {
                float a = 90 + i * 72 + (reloading ? -progress * 360 : 0);
                Vector3 centre = Polar(radius + .023f + reloadOpen * .015f, a, -.012f * unfold);
                diamond[0] = centre + Polar(.012f, a); diamond[1] = centre + Polar(.007f, a + 90);
                diamond[2] = centre - Polar(.012f, a); diamond[3] = centre - Polar(.007f, a + 90); diamond[4] = diamond[0];
                capacitors[i].SetPositions(diamond); Tint(capacitors[i], core, i < VisibleAmmo ? .95f : .10f);
            }
            Arc(sight, Mathf.Lerp(.012f, .0018f, unfold), 0, 360, 0); Tint(sight, Color.white, .9f);
            chargeArc.enabled = reloading;
            Arc(chargeArc, radius + .039f, 90 - progress * 720, 40, 0); Tint(chargeArc, Color.white, reloadOpen * .9f);
        }
    }
}
