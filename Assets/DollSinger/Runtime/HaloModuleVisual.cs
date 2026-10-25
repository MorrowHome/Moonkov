using UnityEngine;
using UnityEngine.Rendering;

namespace Unity.MP_FPS.DollSinger
{
    // Each drawn module is a mirrored pair. Flat sprites share one atlas/material;
    // only biology uses the small reused mesh.
    public sealed class HaloModuleVisual : MonoBehaviour
    {
        private const string ArtworkPath = "Moonkov/HaloModules/ModuleArtwork";
        private static readonly HaloModule[] ArtworkModules = {
            HaloModule.Capacitor, HaloModule.ChargeBridge, HaloModule.MooncoreRecycler,
            HaloModule.FocusLens, HaloModule.FanArray, HaloModule.PulseController, HaloModule.HitMarker
        };
        private static readonly string[] ArtworkNames = {
            "halo_capacitor", "halo_charge_bridge", "halo_mooncore_recycler",
            "halo_focus_lens", "halo_fan_array", "halo_pulse_controller", "halo_hit_marker"
        };
        private static Sprite[] cachedArtwork;
        private static Material sharedArtworkMaterial;
        private static int artworkVersion;
        private readonly SpriteRenderer[] artworkRenderers = new SpriteRenderer[14];
        private readonly Vector3[] vertices = new Vector3[128];
        private readonly Color[] colors = new Color[128];
        private readonly int[] triangles = new int[192];
        private Mesh mesh;
        private MeshFilter meshFilter;
        private Material material;
        private MeshRenderer biologyRenderer;
        private HaloModule modules;
        private int capacity = 4, ammo = 4, target, pulseIndex, boundArtworkVersion = -1;
        private int vertexCount;
        private float aim, progress, elapsed, flash;
        private bool reloading, rifle, artworkReady, warnedArtwork;
        private RifleHaloVisual rifleBody;
        private ShotgunHaloVisual shotgunBody;
        private Sprite[] bodySprites;
        private Vector2[][] bodyOutlines;
        private float rifleRadius;
        private uint lastShot;
        private bool initialized;
        private static readonly Color Gold = new Color(1f, .74f, .28f);
        private static readonly Color Cyan = new Color(.3f, .94f, 1f);
        private static readonly Color Violet = new Color(.82f, .55f, 1f);

        public static void RefreshArtworkCache()
        {
            cachedArtwork = Resources.LoadAll<Sprite>(ArtworkPath);
            sharedArtworkMaterial = Resources.Load<Material>(ArtworkPath);
            artworkVersion++;
        }

        public static Sprite Artwork(HaloModule module)
        {
            if (cachedArtwork == null || cachedArtwork.Length == 0) RefreshArtworkCache();
            return FindArtwork(module);
        }

        public static Color ArtworkColor(HaloModule module)
        {
            switch (Slot(module))
            {
                case 1: return new Color32(0x66, 0xD9, 0xE8, 0xFF);
                case 2: return new Color32(0xC9, 0xAD, 0xFF, 0xFF);
                default: return new Color32(0xF6, 0xD7, 0x76, 0xFF);
            }
        }

        private static Sprite FindArtwork(HaloModule module)
        {
            if (cachedArtwork == null) return null;
            for (int i = 0; i < ArtworkModules.Length; i++)
            {
                if (ArtworkModules[i] != module) continue;
                for (int n = 0; n < cachedArtwork.Length; n++)
                    if (cachedArtwork[n] && cachedArtwork[n].name == ArtworkNames[i]) return cachedArtwork[n];
                return null;
            }
            return null;
        }

        public void Configure(bool isRifle)
        {
            rifle = isRifle;
            rifleBody = GetComponentInParent<RifleHaloVisual>(true);
            shotgunBody = GetComponentInParent<ShotgunHaloVisual>(true);
            if (mesh) return;
            for (int i = 0, t = 0; i < vertices.Length; i += 4, t += 6)
            { triangles[t] = i; triangles[t + 1] = i + 1; triangles[t + 2] = i + 2; triangles[t + 3] = i; triangles[t + 4] = i + 2; triangles[t + 5] = i + 3; }
            mesh = new Mesh { name = "Flat halo biology geometry", hideFlags = HideFlags.DontSave };
            mesh.MarkDynamic();
            material = new Material(Shader.Find("DollSinger/RifleHaloLine")) { hideFlags = HideFlags.DontSave };
            material.SetFloat("_Gain", 1.2f);
            meshFilter = gameObject.AddComponent<MeshFilter>(); meshFilter.sharedMesh = mesh;
            biologyRenderer = gameObject.AddComponent<MeshRenderer>(); biologyRenderer.sharedMaterial = material;
            biologyRenderer.shadowCastingMode = ShadowCastingMode.Off; biologyRenderer.receiveShadows = false;
            var bodySprite = shotgunBody ? shotgunBody.shell : rifleBody && rifleBody.petals != null && rifleBody.petals.Length > 0 ? rifleBody.petals[0] : null;
            for (int i = 0; i < artworkRenderers.Length; i++)
            {
                var child = new GameObject(ArtworkNames[i / 2] + (i % 2 == 0 ? " right" : " left")); child.layer = gameObject.layer;
                child.transform.SetParent(transform, false);
                var renderer = child.AddComponent<SpriteRenderer>(); artworkRenderers[i] = renderer;
                renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
                renderer.sortingLayerID = bodySprite ? bodySprite.sortingLayerID : 0;
                renderer.sortingOrder = bodySprite ? bodySprite.sortingOrder + 1 : 1;
                child.SetActive(false);
            }
            BindArtwork(); Rebuild();
        }

        private void BindArtwork()
        {
            if (cachedArtwork == null) RefreshArtworkCache();
            artworkReady = sharedArtworkMaterial && sharedArtworkMaterial.shader && sharedArtworkMaterial.shader.name == "DollSinger/RifleHaloInk";
            bool complete = artworkReady;
            for (int i = 0; i < artworkRenderers.Length; i++)
            {
                var renderer = artworkRenderers[i];
                if (!renderer) continue;
                renderer.sprite = FindArtwork(ArtworkModules[i / 2]);
                renderer.flipX = i % 2 != 0;
                renderer.sharedMaterial = artworkReady ? sharedArtworkMaterial : null;
                if (!renderer.sprite) complete = false;
            }
            boundArtworkVersion = artworkVersion;
            if (!complete && !warnedArtwork)
            {
                Debug.LogWarning("Halo module artwork is unavailable. Prepare the named sprite slices and the shared RifleHaloInk material before rendering modules.", this);
                warnedArtwork = true;
            }
            if (complete) warnedArtwork = false;
        }

        public void SetState(ushort mask, int rounds, int magazine, bool reload, float chargeProgress, int targetAmmo, uint tick, int burstIndex = 0)
        {
            modules = (HaloModule)mask; ammo = rounds; capacity = Mathf.Max(1, magazine);
            reloading = reload; progress = Mathf.Clamp01(chargeProgress); target = Mathf.Max(rounds, targetAmmo);
            if (lastShot != tick && tick != 0 && (lastShot == 0 || (int)(tick - lastShot) > 0))
            { if (initialized) flash = 1f; lastShot = tick; pulseIndex = Mathf.Clamp(burstIndex, 0, 2); }
            initialized = true;
            if (!Application.isPlaying) Rebuild();
        }
        public void SetAimBlend(float value) => aim = Mathf.Clamp01(value);
        private void LateUpdate() => Step(Time.deltaTime);
        public void Step(float dt)
        { elapsed += Mathf.Max(0, dt); flash *= Mathf.Exp(-Mathf.Max(0, dt) * 11f); Rebuild(); }

        private static int Slot(HaloModule module) => module == HaloModule.HitMarker ? 2 :
            module == HaloModule.FocusLens || module == HaloModule.FanArray || module == HaloModule.PulseController ? 1 : 0;
        private static Vector2 ArtworkSize(HaloModule module)
        {
            switch (module)
            {
                case HaloModule.Capacitor: return new Vector2(.14f, .12f);
                case HaloModule.HitMarker: return new Vector2(.12f, .10f);
                default: return new Vector2(.16f, .10f);
            }
        }
        private void UpdateArtwork()
        {
            if (boundArtworkVersion != artworkVersion) BindArtwork();
            float diameter = BodyRadius(90f) * 2f;
            int occupied = 0;
            int visible = reloading ? Mathf.FloorToInt(Mathf.Lerp(ammo, target, progress)) : ammo;
            float bodyAlpha = rifleBody ? rifleBody.lightColor.a : shotgunBody ? shotgunBody.lightColor.a : 1f;
            for (int i = 0; i < ArtworkModules.Length; i++)
            {
                var renderer = artworkRenderers[i * 2];
                var mirror = artworkRenderers[i * 2 + 1];
                if (!renderer || !mirror) continue;
                var module = ArtworkModules[i]; int slot = Slot(module);
                var mainColor = ArtworkColor(module);
                bool selected = (modules & module) != 0 && (occupied & (1 << slot)) == 0;
                if (selected) occupied |= 1 << slot;
                if (!selected || !artworkReady || !renderer.sprite || !mirror.sprite)
                { SetPairActive(renderer, mirror, false); continue; }
                var bounds = renderer.sprite.bounds;
                if (bounds.size.x <= 0 || bounds.size.y <= 0)
                { SetPairActive(renderer, mirror, false); continue; }
                var size = ArtworkSize(module) * diameter;
                float scale = Mathf.Min(size.x / bounds.size.x, size.y / bounds.size.y);
                float brightness = 1f, pulseScale = 1f + flash * .035f;
                float wave = .5f + .5f * Mathf.Sin(elapsed * 9f + progress * Mathf.PI * 2f);
                if (module == HaloModule.Capacitor) brightness = Mathf.Lerp(.60f, 1f, Mathf.Clamp01((float)visible / capacity));
                else if (module == HaloModule.ChargeBridge || module == HaloModule.MooncoreRecycler)
                {
                    if (reloading) { brightness = .88f + wave * .12f; pulseScale += wave * .025f; }
                }
                else if (module == HaloModule.PulseController)
                { brightness = .90f + flash * .10f; pulseScale = 1f + flash * .07f; }
                else if (module == HaloModule.FanArray) pulseScale = 1f + flash * .05f;
                // Right-hand anchors: supply at 3, firing at 1, utility at 5.
                // Reflect the complete pose and artwork to obtain 9, 11 and 7.
                float angle = slot == 0 ? 0f : slot == 1 ? 60f : 300f;
                float poseScale = scale * pulseScale;
                float bodyRadius = BodyRadius(angle);
                float tangent = module == HaloModule.PulseController ? (pulseIndex - 1) * bodyRadius * flash * .01f : 0;
                float halfWidth = bounds.size.x * poseScale * .5f;
                float halfHeight = bounds.size.y * poseScale * .5f;
                float anchor = bodyRadius + halfHeight + diameter * .012f;
                // Match the approved head-worn concept's readable solid accents.
                // Every corner, including animation, stays within 1.30 times the body radius.
                float envelope = bodyRadius * 1.30f;
                float tangentExtent = halfWidth + Mathf.Abs(tangent);
                float maxAnchor = Mathf.Sqrt(Mathf.Max(0, envelope * envelope - tangentExtent * tangentExtent)) - halfHeight;
                anchor = Mathf.Min(anchor, maxAnchor);
                var rotation = Quaternion.Euler(0, 0, angle - 90f);
                var center = rotation * (bounds.center * poseScale);
                var position = Local(anchor, angle, tangent, 0) - center;
                position.z = -.0045f;
                renderer.transform.SetLocalPositionAndRotation(position, rotation);
                renderer.transform.localScale = Vector3.one * poseScale;
                renderer.color = new Color(mainColor.r * brightness, mainColor.g * brightness, mainColor.b * brightness, bodyAlpha);
                mirror.transform.SetLocalPositionAndRotation(new Vector3(-position.x, position.y, position.z), Quaternion.Euler(0, 0, 90f - angle));
                mirror.transform.localScale = renderer.transform.localScale;
                mirror.color = renderer.color;
                SetPairActive(renderer, mirror, true);
            }
        }

        private static void SetPairActive(SpriteRenderer right, SpriteRenderer left, bool active)
        {
            if (right.gameObject.activeSelf != active) right.gameObject.SetActive(active);
            if (left.gameObject.activeSelf != active) left.gameObject.SetActive(active);
        }

        private static Vector3 Polar(float radius, float angle)
        { float a = angle * Mathf.Deg2Rad; return new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, -.003f); }
        private void Line(Vector3 a, Vector3 b, float width, Color color)
        {
            if (vertexCount + 4 > vertices.Length) return;
            Vector3 normal = Vector3.Cross((b - a).normalized, Vector3.forward) * width * .5f;
            int n = vertexCount; vertices[n] = a + normal; vertices[n + 1] = b + normal; vertices[n + 2] = b - normal; vertices[n + 3] = a - normal;
            for (int i = 0; i < 4; i++) colors[n + i] = color;
            vertexCount += 4;
        }
        private static Vector3 Local(float radius, float angle, float tangent, float outward)
        {
            float a = angle * Mathf.Deg2Rad;
            return Polar(radius + outward, angle) + new Vector3(-Mathf.Sin(a), Mathf.Cos(a), 0) * tangent;
        }
        private static Color Fade(Color color, float alpha) { color.a = alpha; return color; }
        private void Plate(float radius, float angle, float tangent, float outward, float halfWidth, float halfHeight, float width, Color color)
        {
            var a = Local(radius, angle, tangent - halfWidth, outward - halfHeight);
            var b = Local(radius, angle, tangent + halfWidth, outward - halfHeight);
            var c = Local(radius, angle, tangent + halfWidth, outward + halfHeight);
            var d = Local(radius, angle, tangent - halfWidth, outward + halfHeight);
            Line(a, b, width, color); Line(b, c, width, color); Line(c, d, width, color); Line(d, a, width, color);
        }
        private void Diamond(float radius, float angle, float size, float width, Color color)
        {
            var a = Local(radius, angle, 0, size); var b = Local(radius, angle, size, 0);
            var c = Local(radius, angle, 0, -size); var d = Local(radius, angle, -size, 0);
            Line(a, b, width, color); Line(b, c, width, color); Line(c, d, width, color); Line(d, a, width, color);
        }
        private float BodyRadius(float angle = 150f)
        {
            if (rifleBody)
            {
                if (rifleRadius > 0) return rifleRadius;
                float scale = rifleBody.overallScale * Mathf.Lerp(rifleBody.idleScale, 1f, aim);
                return (rifleBody.ringRadius + .055f) * scale;
            }
            float a = angle * Mathf.Deg2Rad;
            float x = Mathf.Abs(Mathf.Cos(a)), y = Mathf.Abs(Mathf.Sin(a));
            float shotgunEdge = Mathf.Max(.210f / Mathf.Max(x, y), .260f / (x + y));
            if (shotgunBody && shotgunBody.shell) return shotgunEdge * Mathf.Abs(shotgunBody.shell.transform.localScale.x);
            return rifle ? .3325f * Mathf.Lerp(.68f, 1f, aim) : shotgunEdge * Mathf.Lerp(.82f, 1f, aim) * Mathf.Lerp(1.5f, 1f, aim);
        }

        private void MeasureRifleRadius()
        {
            rifleRadius = 0;
            if (!rifleBody || rifleBody.petals == null) return;
            var petals = rifleBody.petals;
            if (bodySprites == null || bodySprites.Length != petals.Length)
            {
                bodySprites = new Sprite[petals.Length];
                bodyOutlines = new Vector2[petals.Length][];
            }
            var modelToModule = Matrix4x4.TRS(transform.localPosition, transform.localRotation, transform.localScale).inverse;
            float radiusSquared = 0;
            for (int i = 0; i < petals.Length; i++)
            {
                var petal = petals[i];
                if (!petal || !petal.sprite) continue;
                if (bodySprites[i] != petal.sprite)
                {
                    bodySprites[i] = petal.sprite;
                    // Sprite.vertices returns a copy. Cache the actual drawn outline,
                    // rather than allocating it on each frame or guessing an edge width.
                    bodyOutlines[i] = petal.sprite.vertices;
                }
                var part = petal.transform;
                var pose = part.parent == transform.parent ? modelToModule * Matrix4x4.TRS(part.localPosition, part.localRotation, part.localScale) :
                    transform.worldToLocalMatrix * part.localToWorldMatrix;
                var outline = bodyOutlines[i];
                for (int v = 0; v < outline.Length; v++)
                {
                    var vertex = outline[v];
                    if (petal.flipX) vertex.x = -vertex.x;
                    if (petal.flipY) vertex.y = -vertex.y;
                    var point = pose.MultiplyPoint3x4(vertex);
                    radiusSquared = Mathf.Max(radiusSquared, point.x * point.x + point.y * point.y);
                }
            }
            rifleRadius = Mathf.Sqrt(radiusSquared);
        }
        private void Rebuild()
        {
            if (!mesh) return;
            MeasureRifleRadius();
            UpdateArtwork(); vertexCount = 0;
            float radius = BodyRadius(), unit = radius * .10f, width = radius * .0064f;
            // Biology keeps its existing independent research/effect presentation.
            if ((modules & HaloModule.BiologicalCapacity) != 0)
                for (int i = 0; i < 2; i++) Plate(radius, 90f, (i == 0 ? -.4f : .4f) * unit, 0, unit * .15f, unit * .25f, width, Fade(Gold, .3f));
            if ((modules & HaloModule.BiologicalCharging) != 0)
                for (int i = 0; i < 2; i++) Line(Local(radius, 210f, (i == 0 ? -.25f : .25f) * unit, -unit * .35f), Local(radius, 210f, (i == 0 ? -.25f : .25f) * unit, unit * .35f), width, Fade(Cyan, .3f));
            if ((modules & HaloModule.BiologicalStability) != 0)
                for (int side = -1; side <= 1; side += 2) Line(Local(radius, 330f, side * unit * .45f, unit * .2f), Local(radius, 330f, side * unit * .2f, -unit * .2f), width, Fade(Cyan, .3f));
            if ((modules & (HaloModule.BiologicalResearch | HaloModule.AlienSurge | HaloModule.FoodEffectMask)) != 0)
            {
                float pulse = .75f + .15f * Mathf.Sin(elapsed * 3) + flash * .1f;
                var effect = (AlienFoodEffect)((ushort)modules >> 12);
                Color bio = effect == AlienFoodEffect.Regeneration || effect == AlienFoodEffect.OxygenReserve ? new Color(.4f, 1, .6f) :
                    effect == AlienFoodEffect.VenomPower || effect == AlienFoodEffect.ContractilePower ? new Color(1, .4f, .3f) : Violet;
                for (int i = 0; i < 3; i++) Diamond(radius - unit * .6f, 90f + i * 120f, unit * .17f, width, Fade(bio, pulse * .42f));
            }
            if (vertexCount == 0)
            {
                if (biologyRenderer.enabled) { mesh.Clear(false); biologyRenderer.enabled = false; }
                return;
            }
            mesh.Clear(false);
            mesh.SetVertices(vertices, 0, vertexCount); mesh.SetColors(colors, 0, vertexCount);
            mesh.SetTriangles(triangles, 0, vertexCount / 4 * 6, 0, true);
            biologyRenderer.enabled = vertexCount > 0;
        }
        private void OnDestroy()
        {
            for (int i = 0; i < artworkRenderers.Length; i++)
                if (artworkRenderers[i]) DestroyOwned(artworkRenderers[i].gameObject);
            DestroyOwned(biologyRenderer); DestroyOwned(meshFilter);
            DestroyOwned(mesh); DestroyOwned(material);
        }

        private static void DestroyOwned(Object owned)
        {
            if (!owned) return;
            if (Application.isPlaying) Destroy(owned); else DestroyImmediate(owned);
        }
    }
}
