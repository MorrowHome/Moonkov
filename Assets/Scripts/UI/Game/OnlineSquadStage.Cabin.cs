using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Unity.MP_FPS.Client
{
    public sealed partial class OnlineSquadStage
    {
        private readonly List<Material> m_CabinMaterials = new List<Material>();
        private readonly List<Mesh> m_CabinMeshes = new List<Mesh>();

        private void BuildObservationCabin()
        {
            var hull = CabinMaterial("Pressure hull", new Color32(32, 36, 44, 255), .2f, .35f);
            var frame = CabinMaterial("Window frame", new Color32(70, 80, 92, 255), .32f, .47f);
            var reveal = CabinMaterial("Window reveal", new Color32(44, 53, 65, 255), .20f, .31f);
            var seal = CabinMaterial("Recesses and gaskets", new Color32(16, 21, 29, 255), .08f, .24f);
            var deck = CabinMaterial("Deck", new Color32(40, 46, 55, 255), .24f, .31f);
            var bolt = CabinMaterial("Fasteners", new Color32(110, 123, 132, 255), .53f, .5f);
            var amber = FixtureMaterial("Amber guides", new Color32(223, 152, 75, 255));
            var warm = FixtureMaterial("Overhead lamps", new Color32(255, 225, 183, 255));
            var ice = FixtureMaterial("Sill indicators", new Color32(110, 166, 194, 255));

            BuildObservationWindow(hull, frame, reveal, seal, bolt);
            Block("Deck", new Vector3(0, -.09f, 1.7f), new Vector3(24, .18f, 7.6f), deck);
            Block("Window sill", new Vector3(0, .64f, -1.66f), new Vector3(6.55f, .12f, .31f), frame);
            Block("Sill recess", new Vector3(0, .54f, -1.65f), new Vector3(6.2f, .07f, .25f), seal);
            Block("Ceiling beam", new Vector3(0, 2.44f, -1.54f), new Vector3(8.4f, .20f, .43f), hull);

            foreach (float side in new[] { -1f, 1f })
            {
                float x = side * 2.57f;
                Block("Sill guide", new Vector3(x, .555f, -1.51f), new Vector3(.85f, .012f, .02f), amber);
                Block("Sill indicator", new Vector3(side * .47f, .554f, -1.515f), new Vector3(.23f, .009f, .02f), ice);
                Block("Access plate", new Vector3(x, .295f, -1.74f), new Vector3(1.29f, .35f, .13f), frame);
                Block("Vent recess", new Vector3(x, .295f, -1.666f), new Vector3(1.17f, .24f, .024f), seal);
                for (int i = -3; i <= 3; i++)
                    Block("Vent vane", new Vector3(x + i * .146f, .295f, -1.645f),
                        new Vector3(.09f, .16f, .025f), hull);

                Block("Overhead lamp housing", new Vector3(side * 1.83f, 2.31f, -1.44f),
                    new Vector3(1.79f, .12f, .24f), seal);
                Block("Overhead diffuser", new Vector3(side * 1.83f, 2.251f, -1.39f),
                    new Vector3(1.55f, .025f, .125f), warm);
                Block("Deck runner", new Vector3(side * 2.50f, .002f, .52f),
                    new Vector3(.37f, .005f, 4.35f), seal);
                Block("Recessed deck track", new Vector3(side * 2.30f, .002f, .52f),
                    new Vector3(.017f, .005f, 4.35f), frame);
                for (int i = 0; i < 3; i++)
                    Block("Deck guide", new Vector3(side * 2.50f, .007f, -.92f + i * .33f),
                        new Vector3(.15f, .005f, .065f), amber);
            }
            foreach (var position in SlotPositions)
                Block("Squad position marker", position + new Vector3(0, .004f, .29f),
                    new Vector3(.46f, .006f, .014f), frame);

            Directional("Warm cabin key", new Vector3(38, 151, 0), new Color(1, .90f, .78f), 1.22f, true);
            Directional("Neutral face fill", new Vector3(12, -140, 0), new Color(.82f, .90f, 1), .46f, false);
            Directional("Moonlight rim", new Vector3(24, 28, 0), new Color(.57f, .77f, 1), .97f, false);
            DeckLight(-1);
            DeckLight(1);
        }

        private void BuildObservationWindow(Material hull, Material frame, Material reveal, Material seal, Material bolt)
        {
            const float wallZ = -1.82f;
            var wall = WindowContour(20, -8, 18, .5f, .34f);
            var flange = WindowContour(3.63f, .52f, 2.36f, .58f, .34f);
            var bevel = WindowContour(3.49f, .63f, 2.27f, .52f, .31f);
            var rebate = WindowContour(3.22f, .78f, 2.13f, .45f, .26f);
            var aperture = WindowContour(3.16f, .83f, 2.08f, .42f, .23f);
            WindowBand("Pressure hull", wall, wallZ, flange, wallZ, hull);
            WindowBand("Raised flange", flange, wallZ, bevel, -1.72f, frame);
            WindowBand("Window reveal", bevel, -1.72f, rebate, -2.11f, reveal);
            WindowBand("Glazing gasket", rebate, -2.11f, aperture, -2.14f, seal);
            for (int i = 0; i < flange.Length; i++)
            {
                Vector2 position = Vector2.Lerp(flange[i], bevel[i], .48f);
                Block("Window fastener", new Vector3(position.x, position.y, -1.76f),
                    new Vector3(.042f, .042f, .033f), bolt);
            }

            // Keep the moon beyond the hull. The deep reveal masks it at every camera aspect.
            var moon = Resources.Load<Texture2D>("Moonkov/UI/Halos/MainRoom/Moon");
            if (moon == null) return;
            var vista = FixtureMaterial("Lunar vista", new Color(.62f, .69f, .80f));
            vista.SetTexture("_BaseMap", moon);
            // The source uses Clamp. Extend its dark sides past the aperture to hide the plane's edges.
            var plane = CabinMesh("Lunar vista",
                new[] { new Vector3(-1.5f, -.5f), new Vector3(1.5f, -.5f), new Vector3(1.5f, .5f), new Vector3(-1.5f, .5f) },
                new[] { 0, 1, 2, 0, 2, 3 });
            plane.uv = new[] { new Vector2(2, 0), new Vector2(-1, 0), new Vector2(-1, 1), new Vector2(2, 1) };
            var exterior = Surface("Moon beyond observation window", plane, vista);
            exterior.localPosition = new Vector3(-3.55f, -1.34f, -10);
            exterior.localScale = new Vector3(10.6f, 10.6f, 1);
        }

        private static Vector2[] WindowContour(float width, float bottom, float top, float cutX, float cutY)
        {
            return new[]
            {
                new Vector2(-width + cutX, bottom), new Vector2(width - cutX, bottom),
                new Vector2(width, bottom + cutY), new Vector2(width, top - cutY),
                new Vector2(width - cutX, top), new Vector2(-width + cutX, top),
                new Vector2(-width, top - cutY), new Vector2(-width, bottom + cutY)
            };
        }

        private void WindowBand(string name, Vector2[] outer, float outerZ, Vector2[] inner, float innerZ, Material material)
        {
            // Separate vertices give each chamfer its own normal.
            var vertices = new Vector3[outer.Length * 4];
            var triangles = new int[outer.Length * 6];
            for (int i = 0; i < outer.Length; i++)
            {
                int next = (i + 1) % outer.Length;
                int v = i * 4, t = i * 6;
                vertices[v] = new Vector3(outer[i].x, outer[i].y, outerZ);
                vertices[v + 1] = new Vector3(outer[next].x, outer[next].y, outerZ);
                vertices[v + 2] = new Vector3(inner[next].x, inner[next].y, innerZ);
                vertices[v + 3] = new Vector3(inner[i].x, inner[i].y, innerZ);
                triangles[t] = v;
                triangles[t + 1] = v + 1;
                triangles[t + 2] = v + 2;
                triangles[t + 3] = v;
                triangles[t + 4] = v + 2;
                triangles[t + 5] = v + 3;
            }
            Surface(name, CabinMesh(name, vertices, triangles), material);
        }

        private Mesh CabinMesh(string name, Vector3[] vertices, int[] triangles)
        {
            var mesh = new Mesh { name = "Squad " + name, hideFlags = HideFlags.HideAndDontSave };
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            m_CabinMeshes.Add(mesh);
            return mesh;
        }

        private Transform Surface(string name, Mesh mesh, Material material)
        {
            var surface = Child(name);
            surface.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = surface.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            return surface.transform;
        }

        private Material OwnMaterial(string name, Shader shader)
        {
            var material = new Material(shader) { name = "Squad " + name, hideFlags = HideFlags.HideAndDontSave };
            m_CabinMaterials.Add(material);
            return material;
        }

        private Material CabinMaterial(string name, Color color, float metal, float smoothness)
        {
            var material = OwnMaterial(name, Shader.Find("Universal Render Pipeline/Lit"));
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Metallic", metal);
            material.SetFloat("_Smoothness", smoothness);
            return material;
        }

        private Material FixtureMaterial(string name, Color color)
        {
            var material = OwnMaterial(name, Shader.Find("Universal Render Pipeline/Unlit"));
            material.SetColor("_BaseColor", color);
            return material;
        }

        private void Block(string name, Vector3 position, Vector3 scale, Material material)
        {
            var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = name;
            block.layer = StageLayer;
            block.transform.SetParent(m_Root.transform, false);
            block.transform.localPosition = position;
            block.transform.localScale = scale;
            var renderer = block.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            Destroy(block.GetComponent<Collider>());
        }

        private void Directional(string name, Vector3 angles, Color color, float intensity, bool shadows)
        {
            var light = Child(name).AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.localRotation = Quaternion.Euler(angles);
            light.color = color;
            light.intensity = intensity;
            light.cullingMask = 1 << StageLayer;
            light.shadows = shadows ? LightShadows.Soft : LightShadows.None;
        }

        private void DeckLight(float side)
        {
            var light = Child("Warm pool beneath cabin lamp").AddComponent<Light>();
            light.type = LightType.Spot;
            light.transform.localPosition = new Vector3(side * 1.83f, 2.23f, -1.37f);
            light.transform.localRotation = Quaternion.LookRotation(new Vector3(side * .22f, -1, .75f));
            light.color = new Color(1, .74f, .49f);
            light.intensity = 4.5f;
            light.range = 4.2f;
            light.spotAngle = 74;
            light.innerSpotAngle = 42;
            light.cullingMask = 1 << StageLayer;
        }

        private void ReleaseCabinAssets()
        {
            foreach (var material in m_CabinMaterials) Destroy(material);
            m_CabinMaterials.Clear();
            foreach (var mesh in m_CabinMeshes) Destroy(mesh);
            m_CabinMeshes.Clear();
        }
    }
}
