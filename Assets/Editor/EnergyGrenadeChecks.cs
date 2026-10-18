using System;
using System.IO;
using Unity.MP_FPS;
using Unity.MP_FPS.Inventory;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class EnergyGrenadeChecks
{
    private static int s_Count;
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); s_Count++; }

    [MenuItem("Moonkov/Energy Grenade/Run Focused Checks")]
    public static void Run()
    {
        s_Count = 0;
        var graph = InventoryGraph.Create(false);
        Check(graph.AddSupply(EnergyGrenadeRules.Code, 2) == InventoryError.None, "Starter grenades fit carried inventory");
        var grenade = graph.Items.Find(i => i.Code == EnergyGrenadeRules.Code);
        // Explicit placement tests accessibility independently of FindSpace's preferred container.
        grenade.Parent = InventoryCatalog.Pockets; grenade.Region = "1"; grenade.X = grenade.Y = 0;
        int version = graph.Version;
        Check(EnergyGrenadeRules.Take(graph, grenade.Id, version - 1) == InventoryError.Stale && grenade.Quantity == 2, "Stale request cannot spend");
        Check(EnergyGrenadeRules.Take(graph, grenade.Id, version) == InventoryError.None && grenade.Quantity == 1 && graph.Version == version + 1, "Throw spends exactly one and increments version");
        Check(EnergyGrenadeRules.Take(graph, grenade.Id, version) == InventoryError.Stale && grenade.Quantity == 1, "Replay cannot spend twice");
        grenade.Parent = graph.Equipped("Backpack").Id;
        Check(EnergyGrenadeRules.Take(graph, grenade.Id, graph.Version) == InventoryError.Inaccessible && grenade.Quantity == 1, "Backpack throw rejected");
        grenade.Parent = "stash";
        Check(!EnergyGrenadeRules.Accessible(graph, grenade), "Stash throw rejected");
        grenade.Parent = graph.Equipped("ChestRig").Id;
        Check(EnergyGrenadeRules.Take(graph, grenade.Id, graph.Version) == InventoryError.None && graph.Find(grenade.Id) == null, "Equipped rig last item removed");
        Check(EnergyGrenadeRules.DamageAt(0) == 480 && EnergyGrenadeRules.DamageAt(1.5f) == 480 &&
            EnergyGrenadeRules.DamageAt(6) == 0 && EnergyGrenadeRules.DamageAt(float.NaN) == 0 &&
            EnergyGrenadeRules.DamageAt(4) < EnergyGrenadeRules.DamageAt(2), "Blast falloff and finite boundaries");
        Check(ShopCatalog.Find(EnergyGrenadeRules.Code)?.BuyDust == 12, "Supplier offer exists");
        var style = Resources.Load<EnergyGrenadeStyle>("Moonkov/EnergyGrenade");
        Check(style && style.Orb && style.Ribbon && style.Spark && style.Sphere && style.ThrowSound && style.ExplosionSound, "Build referenced visuals and pooled sounds");
        Check(!ShaderUtil.ShaderHasError(style.Orb.shader), "Energy shader compiled");

        var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            var physics = scene.GetPhysicsScene();
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            SceneManager.MoveGameObjectToScene(wall, scene);
            wall.transform.position = new Vector3(0, 1, 2); wall.transform.localScale = new Vector3(10, 10, .08f);
            var target = new GameObject("Blast target"); SceneManager.MoveGameObjectToScene(target, scene);
            target.transform.position = new Vector3(0,1,4);
            Physics.SyncTransforms();
            Vector3 position = new(0,1,0), velocity = Vector3.forward * EnergyGrenadeRules.Speed;
            bool resting = false; var hits = new RaycastHit[16];
            EnergyGrenadePhysics.Step(physics, ref position, ref velocity, ref resting, .3f, null, ref hits);
            Check(position.z < 1.9f && velocity.z < 0, "Swept grenade bounces off thin wall during long frame");
            Check(!EnergyGrenadePhysics.ClearSight(physics, new Vector3(0,1,0), target.transform.position, target.transform), "Cover blocks blast");
            wall.SetActive(false); Physics.SyncTransforms();
            Check(EnergyGrenadePhysics.ClearSight(physics, new Vector3(0,1,0), target.transform.position, target.transform), "Uncovered blast reaches target");
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
        Debug.Log("Energy grenade focused checks: " + s_Count + " passed (inventory, falloff, assets, swept bounce, cover). No Play/Host-client acceptance implied.");
    }

    public static void RenderPreview(string path)
    {
        var scene = EditorSceneManager.NewPreviewScene();
        RenderTexture previous = RenderTexture.active;
        var texture = new RenderTexture(1000, 600, 24);
        var image = new Texture2D(1000,600,TextureFormat.RGB24,false);
        try
        {
            var root = new GameObject("Energy grenade preview"); SceneManager.MoveGameObjectToScene(root, scene);
            var effect = root.AddComponent<EnergyGrenadeFeedback>();
            var cameraObject = new GameObject("Preview camera"); SceneManager.MoveGameObjectToScene(cameraObject, scene);
            var camera = cameraObject.AddComponent<Camera>(); camera.scene = scene;
            camera.backgroundColor = new Color(.012f,.023f,.04f); camera.clearFlags = CameraClearFlags.SolidColor;
            camera.targetTexture = texture; camera.fieldOfView = 45;
            camera.transform.position = new Vector3(0,.25f,-1.8f); camera.transform.LookAt(Vector3.zero);
            effect.Preview(false, 1.1f);
            camera.Render(); RenderTexture.active = texture; image.ReadPixels(new Rect(0,0,1000,600),0,0); image.Apply();
            Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllBytes(path, image.EncodeToPNG());
            effect.Preview(true,.22f);
            camera.transform.position = new Vector3(0,3,-14); camera.transform.LookAt(Vector3.zero);
            camera.Render(); RenderTexture.active = texture; image.ReadPixels(new Rect(0,0,1000,600),0,0); image.Apply();
            File.WriteAllBytes(Path.ChangeExtension(path,null)+"-blast.png", image.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous;
            foreach (var root in scene.GetRootGameObjects())
                foreach (var camera in root.GetComponentsInChildren<Camera>()) camera.targetTexture = null;
            EditorSceneManager.ClosePreviewScene(scene);
            texture.Release(); Object.DestroyImmediate(texture); Object.DestroyImmediate(image);
        }
    }
}
