using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.MP_FPS;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class RaidActionPropSetup
{
    private const string Folder = "Assets/Resources/Moonkov/Actions/Props/";
    private struct Vertex
    {
        public Vector3 p, n; public Vector2 uv;
        public static Vertex Lerp(Vertex a, Vertex b, float t) => new Vertex
        { p = Vector3.Lerp(a.p, b.p, t), n = Vector3.Lerp(a.n, b.n, t).normalized, uv = Vector2.Lerp(a.uv, b.uv, t) };
    }
    private sealed class Geometry
    {
        public readonly List<Vector3> positions = new List<Vector3>(), normals = new List<Vector3>();
        public readonly List<Vector2> uv = new List<Vector2>();
        public readonly List<int>[] triangles;
        public Geometry(int materials) => triangles = Enumerable.Range(0, materials).Select(_ => new List<int>()).ToArray();
        public void Add(int material, params Vertex[] polygon)
        {
            int start = positions.Count;
            foreach (var v in polygon) { positions.Add(v.p); normals.Add(v.n); uv.Add(v.uv); }
            for (int i = 1; i + 1 < polygon.Length; i++) triangles[material].AddRange(new[] { start, start + i, start + i + 1 });
        }
        public Mesh Mesh(string name)
        {
            var mesh = new Mesh { name = name, indexFormat = positions.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
            mesh.SetVertices(positions); mesh.SetNormals(normals); mesh.SetUVs(0, uv); mesh.subMeshCount = triangles.Length;
            for (int i = 0; i < triangles.Length; i++) mesh.SetTriangles(triangles[i], i);
            mesh.RecalculateBounds(); return mesh;
        }
    }
    [MenuItem("Tools/Moonkov/Actions/Build opened inventory prop variants")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Build outside Play.");
        Directory.CreateDirectory(Folder);
        foreach (string code in new[] { "water", "ration", "protein_bar", "energy_gel", "electrolyte", "med_gel", "trauma_kit",
            "medkit", "vitality_shot", "stamina_shot", "antidote_shot" }) BuildOne(code);
        AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
        // Canonical meshes and injector pieces come from the reviewed source.
        RaidActionBlenderImport.Import();
    }
    private static void BuildOne(string code)
    {
        var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            var root = new GameObject(code + " action prop"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
            var source = PhysicalItemVisuals.Create(code, root.transform);
            if (!source) throw new InvalidOperationException("Missing inventory model " + code);
            var filter = source.GetComponent<MeshFilter>(); var renderer = source.GetComponent<MeshRenderer>();
            var materials = renderer.sharedMaterials;
            var input = filter.sharedMesh; var vertices = input.vertices; var normals = input.normals; var uvs = input.uv;
            Matrix4x4 matrix = root.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
            Matrix4x4 normalMatrix = matrix.inverse.transpose;
            var points = new Vertex[vertices.Length];
            for (int i = 0; i < points.Length; i++) points[i] = new Vertex
            { p = matrix.MultiplyPoint3x4(vertices[i]), n = normalMatrix.MultiplyVector(normals[i]).normalized, uv = uvs.Length == vertices.Length ? uvs[i] : Vector2.zero };
            var bounds = new Bounds(points[0].p, Vector3.zero); foreach (var point in points) bounds.Encapsulate(point.p);
            bool injection = code == "medkit" || code.EndsWith("_shot"), water = code == "water";
            bool pouch = !injection && !water && code != "trauma_kit";
            float cut = bounds.max.y - (water ? .026f : .018f);
            var sealedMesh = new Geometry(materials.Length); var opened = new Geometry(materials.Length);
            var cap = new Geometry(materials.Length); var plunger = new Geometry(materials.Length);
            float openingY = 0, openingRadius = 0; int openingMaterial = 0;
            for (int s = 0; s < input.subMeshCount; s++)
            {
                var ids = input.GetTriangles(s);
                for (int i = 0; i < ids.Length; i += 3)
                {
                    var triangle = new[] { points[ids[i]], points[ids[i + 1]], points[ids[i + 2]] };
                    float y = triangle.Average(v => v.p.y);
                    if (injection)
                    {
                        (y > bounds.min.y + .15f ? plunger : opened).Add(s, triangle);
                    }
                    else if (water && materials[s].name.IndexOf("steel", StringComparison.OrdinalIgnoreCase) >= 0 && y > cut && triangle.All(v => v.n.y > .9f))
                    {
                        openingY = Mathf.Max(openingY, triangle.Max(v => v.p.y)); openingMaterial = s;
                        openingRadius = Mathf.Max(openingRadius, triangle.Max(v => new Vector2(v.p.x, v.p.z).magnitude));
                    }
                    else if (water && materials[s].name.IndexOf("dark", StringComparison.OrdinalIgnoreCase) >= 0 && y > cut)
                        cap.Add(s, triangle);
                    else if (pouch)
                    {
                        var below = Clip(triangle, cut);
                        if (below.Length >= 3) opened.Add(s, below);
                        var flipped=triangle.Select(v=>new Vertex{p=new Vector3(v.p.x,-v.p.y,v.p.z),n=v.n,uv=v.uv}).ToArray();
                        var above=Clip(flipped,-cut);for(int v=0;v<above.Length;v++)above[v].p.y=-above[v].p.y;
                        if(above.Length>=3)cap.Add(s,above);
                    }
                    else opened.Add(s, triangle);
                    sealedMesh.Add(s, triangle);
                }
            }
            UnityEngine.Object.DestroyImmediate(source);
            if (water && openingRadius > 0)
                for (int i = 0; i < 32; i++)
                {
                    float a = i * Mathf.PI * 2 / 32, b = (i + 1) * Mathf.PI * 2 / 32;
                    Vertex Ring(float angle, float radius) => new Vertex { p = new Vector3(Mathf.Cos(angle) * radius, openingY, Mathf.Sin(angle) * radius), n = Vector3.up };
                    opened.Add(openingMaterial, Ring(a, openingRadius), Ring(a, openingRadius * .72f), Ring(b, openingRadius * .72f), Ring(b, openingRadius));
                }
            var config = root.AddComponent<RaidActionProp>();
            GameObject Part(string name, Geometry geometry)
            {
                var child = new GameObject(name); child.transform.SetParent(root.transform, false);
                string path = Folder + code + "-" + name + ".asset";
                var mesh = geometry.Mesh(code + " " + name); var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (existing) { EditorUtility.CopySerialized(mesh, existing); UnityEngine.Object.DestroyImmediate(mesh); mesh = existing; }
                else AssetDatabase.CreateAsset(mesh, path);
                child.AddComponent<MeshFilter>().sharedMesh = mesh; child.AddComponent<MeshRenderer>().sharedMaterials = materials;
                return child;
            }
            if (injection)
            {
                config.Opened = Part("Body", opened); config.Plunger = Part("Plunger", plunger).transform;
                config.PlungerTravel = .022f;
                var needle = new Geometry(materials.Length);
                int steel = Array.FindIndex(materials, m => m.name.IndexOf("steel", StringComparison.OrdinalIgnoreCase) >= 0);
                if (steel < 0) steel = 0;
                for (int i = 0; i < 12; i++)
                {
                    float a = i * Mathf.PI * 2 / 12, b = (i + 1) * Mathf.PI * 2 / 12;
                    Vector3 na = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)), nb = new Vector3(Mathf.Cos(b), 0, Mathf.Sin(b));
                    Vertex Point(Vector3 n, float y) => new Vertex { p = n * .0006f + Vector3.up * (bounds.min.y + y), n = n, uv = Vector2.zero };
                    needle.Add(steel, Point(na, .001f), Point(nb, .001f), Point(nb, -.011f), Point(na, -.011f));
                    needle.Add(steel, Point(na, -.011f), Point(nb, -.011f), new Vertex { p = new Vector3(0, bounds.min.y - .014f, 0), n = (na + nb).normalized });
                }
                config.Needle = Part("Needle", needle); config.Needle.SetActive(false);
            }
            else if (water)
            {
                config.Opened = Part("Body", opened); config.Cap = Part("Cap", cap).transform;
            }
            else
            {
                config.Sealed = Part("Sealed", sealedMesh); config.Opened = Part("Opened", opened);
                config.Opened.SetActive(false);
                if(pouch){var strip=Part("TearStrip",cap);config.Parts=new[]{new RaidActionProp.Part{Name="Sealed",Transform=config.Sealed.transform},new RaidActionProp.Part{Name="Opened",Transform=config.Opened.transform},new RaidActionProp.Part{Name="TearStrip",Transform=strip.transform}};}
            }
            Transform Anchor(string name, Vector3 position)
            { var node = new GameObject(name).transform; node.SetParent(root.transform, false); node.localPosition = position; return node; }
            config.Grip = Anchor("Grip", new Vector3(0, bounds.min.y + bounds.size.y * .43f, 0));
            if(pouch)config.Grip.localPosition=new Vector3(bounds.center.x+bounds.size.x*.375f,bounds.center.y+bounds.size.y*.105f,bounds.center.z);
            config.Contact = Anchor(injection ? "NeedleTip" : "MouthContact", new Vector3(0, injection ? bounds.min.y - .014f : water && openingY > 0 ? openingY : cut, 0));
            PrefabUtility.SaveAsPrefabAsset(root, Folder + code + ".prefab");
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }
    private static Vertex[] Clip(Vertex[] input, float y)
    {
        var result = new List<Vertex>(4);
        for (int i = 0; i < input.Length; i++)
        {
            var a = input[i]; var b = input[(i + 1) % input.Length];
            bool insideA = a.p.y <= y, insideB = b.p.y <= y;
            if (insideA) result.Add(a);
            if (insideA != insideB) result.Add(Vertex.Lerp(a, b, (y - a.p.y) / (b.p.y - a.p.y)));
        }
        return result.ToArray();
    }
}
