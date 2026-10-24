using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Unity.MP_FPS.Moon
{
    /// <summary>Bounded, cosmetic scree. Never modifies TerrainData or adds colliders.</summary>
    internal sealed class MoonRegolithDebris : IDisposable
    {
        // Two rings cover at least 40 m, beyond this layer's 36 m fade.
        private const float TileSize = 20;
        private const int Radius = 2;
        private const int MaximumFragmentsPerTile = 64;
        private readonly Transform parent;
        private readonly Terrain[] terrains;
        private readonly Material rock, ground;
        private readonly MoonRockMesh shapes = new(7319);
        private readonly Dictionary<Vector2Int, GameObject> tiles = new();
        private readonly List<Vector2Int> expired = new();
        private Camera camera;
        private float nextCamera;
        private static readonly int FragmentMode = Shader.PropertyToID("_FragmentMode");
        private static readonly int ProceduralRock = Shader.PropertyToID("_ProceduralRock");
        private static readonly int GroundColor = Shader.PropertyToID("_GroundColor");
        private static readonly int ReflectanceScale = Shader.PropertyToID("_ReflectanceScale");
        private static readonly int FragmentFadeRange = Shader.PropertyToID("_FragmentFadeRange");

        public MoonRegolithDebris(Transform parent, Terrain[] terrains, Material rock, Material ground)
        {
            this.parent = parent; this.terrains = terrains; this.rock = rock; this.ground = ground;
        }

        public void Tick()
        {
            if (Time.unscaledTime >= nextCamera)
            {
                camera = Camera.main;
                nextCamera = Time.unscaledTime + 1;
            }
            Tick(camera);
        }

        // Explicit camera also permits an Edit Mode preview of the actual runtime geometry.
        public void Tick(Camera view)
        {
            if (!view || !rock || !parent) return;
            var position = view.transform.position;
            var center = new Vector2Int(Mathf.FloorToInt(position.x / TileSize), Mathf.FloorToInt(position.z / TileSize));
            expired.Clear();
            foreach (var pair in tiles)
                if (Mathf.Abs(pair.Key.x - center.x) > Radius || Mathf.Abs(pair.Key.y - center.y) > Radius)
                    expired.Add(pair.Key);
            foreach (var key in expired) { Release(tiles[key]); tiles.Remove(key); }
            int built = 0;
            for (int ring = 0; ring <= Radius; ring++)
                for (int z = -ring; z <= ring; z++)
                    for (int x = -ring; x <= ring; x++)
                    {
                        if (Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)) != ring) continue;
                        var key = center + new Vector2Int(x, z);
                        if (tiles.ContainsKey(key)) continue;
                        tiles.Add(key, CreateTile(key));
                        if (++built == 2) return;
                    }
        }

        private GameObject CreateTile(Vector2Int key)
        {
            var random = new System.Random(unchecked(7319 ^ key.x * 73856093 ^ key.y * 19349663));
            var vertices = new List<Vector3>(); var uv = new List<Vector2>();
            var colors = new List<Color>(); var triangles = new List<int>();
            var origin = new Vector3(key.x * TileSize, 0, key.y * TileSize);
            int fragments = 0;
            for (int i = 0; i < 128 && fragments < MaximumFragmentsPerTile; i++)
            {
                float x = origin.x + (float)random.NextDouble() * TileSize;
                float z = origin.z + (float)random.NextDouble() * TileSize;
                Terrain terrain = Surface(x, z);
                if (!terrain) continue;
                var data = terrain.terrainData;
                float u = (x - terrain.transform.position.x) / data.size.x;
                float v = (z - terrain.transform.position.z) / data.size.z;
                // Do not scatter into authored mine/airlock openings.
                int hx = Mathf.Clamp((int)(u * data.holesResolution), 0, data.holesResolution - 1);
                int hz = Mathf.Clamp((int)(v * data.holesResolution), 0, data.holesResolution - 1);
                if (data.IsHole(hx, hz)) continue;
                var normal = data.GetInterpolatedNormal(u, v);
                float cluster = Mathf.PerlinNoise(x * .17f + 19, z * .17f + 73);
                if (normal.y < .65f || random.NextDouble() > Mathf.Lerp(.15f, .85f, cluster)) continue;
                float diameter = Mathf.Lerp(.025f, .3f, Mathf.Pow((float)random.NextDouble(), 2.7f));
                var point = new Vector3(x, terrain.transform.position.y + data.GetInterpolatedHeight(u, v), z);
                shapes.Append(random, point - origin - normal * diameter * .06f, normal, diameter,
                    (float)random.NextDouble(), vertices, uv, colors, triangles);
                fragments++;
            }
            var mesh = new Mesh { name = "Lunar cosmetic scree", indexFormat = IndexFormat.UInt32, hideFlags = HideFlags.DontSave };
            mesh.SetVertices(vertices); mesh.SetUVs(0, uv); mesh.SetColors(colors); mesh.SetTriangles(triangles, 0);
            if (vertices.Count > 0) { mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds(); }
            mesh.UploadMeshData(true);
            var tile = new GameObject($"Lunar scree {key.x},{key.y}") { hideFlags = HideFlags.DontSave };
            tile.transform.SetParent(parent, true); tile.transform.position = origin;
            tile.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = tile.AddComponent<MeshRenderer>(); renderer.sharedMaterial = rock;
            renderer.shadowCastingMode = ShadowCastingMode.On; renderer.receiveShadows = true;
            var properties = new MaterialPropertyBlock();
            properties.SetFloat(FragmentMode, 1); properties.SetFloat(ProceduralRock, 1);
            properties.SetColor(GroundColor, ground.GetColor("_Color"));
            properties.SetFloat(ReflectanceScale, ground.GetFloat("_ReflectanceScale"));
            properties.SetVector(FragmentFadeRange, new Vector4(26, 36, 0, 0));
            renderer.SetPropertyBlock(properties);
            return tile;
        }

        private Terrain Surface(float x, float z)
        {
            Terrain best = null;
            foreach (var terrain in terrains)
            {
                if (!terrain || !terrain.drawHeightmap || !terrain.gameObject.activeInHierarchy) continue;
                var p = terrain.transform.position; var size = terrain.terrainData.size;
                if (x < p.x || z < p.z || x >= p.x + size.x || z >= p.z + size.z) continue;
                if (!best || size.x < best.terrainData.size.x) best = terrain;
            }
            return best;
        }

        private static void Release(GameObject tile)
        {
            if (!tile) return;
            var mesh = tile.GetComponent<MeshFilter>().sharedMesh;
            // Hide immediately when a camera teleports; deferred destruction alone
            // would briefly leave the old tile set visible with the new one.
            tile.SetActive(false);
            if (Application.isPlaying) { Object.Destroy(mesh); Object.Destroy(tile); }
            else { Object.DestroyImmediate(mesh); Object.DestroyImmediate(tile); }
        }

        public void Dispose()
        {
            foreach (var tile in tiles.Values) Release(tile);
            tiles.Clear();
        }
    }
}
