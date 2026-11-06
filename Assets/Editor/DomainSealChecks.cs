using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

/// <summary>Inspect the six new seals on each delivered skin, one preview actor at a time.</summary>
public static class DomainSealChecks
{
    [Serializable] private class Bake { public string[] bones; public Motion[] motions; }
    [Serializable] private class Motion
    {
        public int weapon, fireFrames, reloadFrames;
        public float pitch;
        public float[] pose, fire, reload;
        public Motion[] aimPoses;
    }
    private const string Output = "LocalData/JujutsuGestureReview/UnitySkins";
    private static readonly int[] Weapons = { 6, 7, 9, 10, 12, 13 };

    [MenuItem("Tools/Moonkov/Check Domain Seals On Three Skins")]
    public static void Run()
    {
        if (BuildPipeline.isBuildingPlayer || EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Run domain seal checks in idle Edit Mode.");
        Directory.CreateDirectory(Output);
        int samples = 0, sceneCount = EditorSceneManager.previewSceneCount;
        foreach (string name in new[] { "DollSinger", "HatsuneMiku", "Shian" })
        {
            var scene = EditorSceneManager.NewPreviewScene();
            GameObject root = null;
            try
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/{name}/Prefabs/{name}Player.prefab");
                root = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                foreach (var b in root.GetComponentsInChildren<MonoBehaviour>(true)) b.enabled = false;
                foreach (var c in root.GetComponentsInChildren<Camera>(true)) c.enabled = false;
                foreach (var c in root.GetComponentsInChildren<Collider>(true)) c.enabled = false;
                var animator = root.GetComponentInChildren<Animator>(true);
                animator.runtimeAnimatorController = null;
                animator.Rebind();
                var bones = root.GetComponentsInChildren<Transform>(true).GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
                string path = $"Assets/{name}/Animation/" + (name == "DollSinger" ? "Blender/" : "") + "WeaponGestures.json";
                var data = JsonUtility.FromJson<Bake>(File.ReadAllText(path));
                var controlled = data.bones.Select(n => bones[n]).ToArray();
                var positions = controlled.Select(t => t.localPosition).ToArray();
                var rotations = controlled.Select(t => t.localRotation).ToArray();
                foreach (int weapon in Weapons)
                {
                    var m = data.motions[weapon - 1];
                    foreach (var variant in new[] { m }.Concat(m.aimPoses ?? Array.Empty<Motion>()))
                    foreach (string mode in new[] { "pose", "fire", "reload" })
                    {
                        var values = mode == "pose" ? variant.pose : mode == "fire" ? variant.fire : variant.reload;
                        int frames = values.Length / (controlled.Length * 4);
                        foreach (int frame in new[] { 0, frames / 3, frames - 1 }.Distinct())
                        {
                            for (int i = 0; i < controlled.Length; i++)
                            {
                                int p = (frame * controlled.Length + i) * 4;
                                var q = new Quaternion(values[p], values[p + 1], values[p + 2], values[p + 3]);
                                if (!float.IsFinite(q.x + q.y + q.z + q.w) || Mathf.Abs(Quaternion.Dot(q, q) - 1) > .001f)
                                    throw new InvalidOperationException(name + " invalid rotation " + controlled[i].name);
                                controlled[i].SetLocalPositionAndRotation(positions[i], q);
                            }
                            foreach (var t in controlled)
                                if (!float.IsFinite(t.position.x + t.position.y + t.position.z))
                                    throw new InvalidOperationException(name + " nonfinite skin transform");
                            if (variant == m && mode == "pose")
                            {
                                Vector3 center = (bones["手首.L"].position + bones["手首.R"].position) * .5f + Vector3.up * .055f;
                                foreach (string angle in new[] { "front", "side", "left" })
                                    Capture(root, scene, center, name + "-" + weapon + "-" + angle, angle);
                            }
                            samples++;
                        }
                    }
                }
                for (int i = 0; i < controlled.Length; i++) controlled[i].SetLocalPositionAndRotation(positions[i], rotations[i]);
            }
            finally
            {
                if (root) Object.DestroyImmediate(root);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }
        if (EditorSceneManager.previewSceneCount != sceneCount) throw new InvalidOperationException("Preview scene leaked.");
        File.WriteAllText(Output + "/result.json", "{\"passed\":true,\"rigs\":3,\"seals\":6,\"samples\":" + samples + ",\"networkSession\":false}");
        Debug.Log("Domain seal skin checks passed: " + samples + "; preview captures require visual review.");
    }

    private static void Capture(GameObject root, UnityEngine.SceneManagement.Scene scene, Vector3 center, string name, string angle)
    {
        var skins = root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r => r.enabled && r.gameObject.activeInHierarchy).ToArray();
        var meshes = new Mesh[skins.Length];
        var captures = new GameObject[skins.Length];
        var oldOff = skins.Select(r => r.forceRenderingOff).ToArray();
        var cameraObject = new GameObject("Domain seal review camera");
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraObject, scene);
        var camera = cameraObject.AddComponent<Camera>();
        camera.scene = scene; camera.enabled = false; camera.fieldOfView = 38;
        camera.nearClipPlane = .01f; camera.farClipPlane = 10;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.14f, .15f, .17f);
        camera.transform.position = center + (angle == "front" ? new Vector3(0, .015f, .60f) : angle == "side" ? new Vector3(.45f, .025f, .45f) : new Vector3(-.45f, .025f, .45f));
        camera.transform.LookAt(center);
        camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
        var lightObject = new GameObject("Domain seal review light");
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(lightObject, scene);
        lightObject.transform.rotation = Quaternion.Euler(35, -30, 0);
        var light = lightObject.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.5f;
        var target = new RenderTexture(800, 650, 24);
        var pixels = new Texture2D(800, 650, TextureFormat.RGB24, false);
        var previous = RenderTexture.active;
        try
        {
            for (int i = 0; i < skins.Length; i++)
            {
                meshes[i] = new Mesh(); skins[i].BakeMesh(meshes[i], true);
                captures[i] = new GameObject("Frozen domain skin"); captures[i].transform.SetParent(skins[i].transform, false);
                captures[i].AddComponent<MeshFilter>().sharedMesh = meshes[i];
                captures[i].AddComponent<MeshRenderer>().sharedMaterials = skins[i].sharedMaterials;
                skins[i].forceRenderingOff = true;
            }
            camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); pixels.Apply();
            File.WriteAllBytes(Output + "/" + name + ".png", pixels.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous; camera.targetTexture = null;
            for (int i = 0; i < skins.Length; i++)
            {
                skins[i].forceRenderingOff = oldOff[i];
                if (captures[i]) Object.DestroyImmediate(captures[i]);
                if (meshes[i]) Object.DestroyImmediate(meshes[i]);
            }
            Object.DestroyImmediate(pixels); target.Release(); Object.DestroyImmediate(target);
            Object.DestroyImmediate(cameraObject); Object.DestroyImmediate(lightObject);
        }
    }
}
