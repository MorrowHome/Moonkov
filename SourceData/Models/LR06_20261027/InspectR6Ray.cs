using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class LR06InspectR6Ray
{
    public static object Run()
    {
        const string path = "Assets/MoonEnvironment/Variants/LR06FullPreview/Scenes/LR06_Full_Display.unity";
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || SceneManager.GetSceneByPath(path).isLoaded) throw new Exception("Editor busy");
        var old = SceneManager.GetActiveScene(); string oldPath = old.path; bool oldDirty = old.isDirty;
        bool oldBackfaces = Physics.queriesHitBackfaces;
        var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
        try
        {
            if (SceneManager.GetActiveScene() != scene) SceneManager.SetActiveScene(scene);
            var roots = scene.GetRootGameObjects();
            var filter = roots.SelectMany(r => r.GetComponentsInChildren<MeshFilter>(true))
                .Single(f => f.sharedMesh && AssetDatabase.GetAssetPath(f.sharedMesh).EndsWith("/Map_07.fbx"));
            var renderer = filter.GetComponent<MeshRenderer>();
            var camera = roots.SelectMany(r => r.GetComponentsInChildren<Camera>(true)).Single();
            camera.transform.position = new Vector3(1840,255,2255); camera.transform.LookAt(new Vector3(1873,240,2298));
            camera.aspect = 960f / 600f;
            var go = new GameObject("Temporary R6 ray inspection"); SceneManager.MoveGameObjectToScene(go, scene);
            go.transform.position = filter.transform.position; go.transform.rotation = filter.transform.rotation; go.transform.localScale = filter.transform.lossyScale;
            var collider = go.AddComponent<MeshCollider>(); collider.sharedMesh = filter.sharedMesh;
            Physics.SyncTransforms();
            object Ray(float x, float y, bool backfaces)
            {
                Physics.queriesHitBackfaces = backfaces;
                var ray = camera.ViewportPointToRay(new Vector3(x,y));
                if (!collider.Raycast(ray, out var hit, 5000)) return new { x, y, backfaces, hit = false };
                int offset = 0, slot = -1;
                for (int i=0; i<filter.sharedMesh.subMeshCount; i++)
                {
                    int count = (int)filter.sharedMesh.GetIndexCount(i)/3;
                    if (hit.triangleIndex < offset + count) { slot = i; break; }
                    offset += count;
                }
                var material = slot >= 0 && slot < renderer.sharedMaterials.Length ? renderer.sharedMaterials[slot] : null;
                return new { x, y, backfaces, hit = true, location = new[]{hit.point.x,hit.point.y,hit.point.z},
                    normal = new[]{hit.normal.x,hit.normal.y,hit.normal.z}, hit.triangleIndex, slot,
                    uv = new[]{hit.textureCoord.x,hit.textureCoord.y},
                    material = material ? AssetDatabase.GetAssetPath(material) : null,
                    baseTexture = material ? AssetDatabase.GetAssetPath(material.GetTexture("_BaseMap")) : null };
            }
            var report = new { rays = new[] { Ray(.5f,.5f,false), Ray(.45f,.48f,false), Ray(.55f,.42f,false), Ray(.5f,.5f,true) }, original = oldPath };
            var json = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "Newtonsoft.Json").GetType("Newtonsoft.Json.JsonConvert");
            var serialized = (string)json.GetMethod("SerializeObject", new[] { typeof(object) }).Invoke(null, new object[] { report });
            System.IO.File.WriteAllText("Library/BuildValidation/LR06PBR/UnityR6Rays.json", serialized);
            return report;
        }
        finally
        {
            Physics.queriesHitBackfaces = oldBackfaces;
            if (old.IsValid() && SceneManager.GetActiveScene() != old) SceneManager.SetActiveScene(old);
            if (scene.IsValid()) EditorSceneManager.CloseScene(scene, true);
            if (old.path != oldPath || old.isDirty != oldDirty) throw new Exception("Original scene changed");
        }
    }
}
