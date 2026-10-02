using System;
using Unity.MP_FPS;
using Unity.MP_FPS.DollSinger;
using Unity.NetCode;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class DollSingerNetworkSetup
{
    public const string PrefabPath = "Assets/DollSinger/Prefabs/DollSingerNetworkPlayer.prefab";
    private const string ResourcesScenePath = "Assets/Scenes/GameResourcesSubScene.unity";

    [MenuItem("Tools/Doll Singer/Set Up Network Player")]
    public static void SetUp()
    {
        if (EditorApplication.isPlaying)
            throw new InvalidOperationException("Stop Play Mode before setting up the network player.");
        var loadedScene = SceneManager.GetSceneByPath(ResourcesScenePath);
        if (loadedScene.isLoaded && loadedScene.isDirty)
            throw new InvalidOperationException("Save the GameResourcesSubScene changes before registration.");

        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null)
            CreatePrefab();
        RemoveTemplateVisuals();
        RegisterHaloWeapon();
        RegisterPrefab();
        Debug.Log("DollSinger network player is registered. Select DollSinger in the MainMenu character choices.");
    }

    private static void CreatePrefab()
    {
        if (!AssetDatabase.CopyAsset("Assets/Prefabs/PlayerGhosts/ArmaturePlayer_Rifle.prefab", PrefabPath))
            throw new InvalidOperationException("Could not copy the template player prefab.");
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            root.name = "DollSingerNetworkPlayer";
            var oldBody = root.transform.Find("Armature_3P");
            if (oldBody != null) UnityEngine.Object.DestroyImmediate(oldBody.gameObject);
            StripTemplateVisuals(root);

            var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/DollSinger/Prefabs/DollSingerPlayer.prefab");
            var model = (GameObject)PrefabUtility.InstantiatePrefab(source, root.scene);
            model.transform.SetParent(root.transform, false);
            model.name = "DollSingerVisual";
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            var movement = model.GetComponent<DollSingerMovement>();
            var input = model.GetComponent<DollSingerInput>();
            var halo = model.GetComponent<DollSingerHaloAim>();
            var view = model.GetComponentInChildren<DollSingerView>(true);
            movement.enabled = false;
            input.enabled = false;
            model.GetComponent<Animator>().applyRootMotion = false;
            var localCapsule = model.GetComponent<UnityEngine.CharacterController>();
            var networkCapsule = root.GetComponent<UnityEngine.CharacterController>();
            networkCapsule.height = localCapsule.height;
            networkCapsule.radius = localCapsule.radius;
            networkCapsule.center = localCapsule.center;
            localCapsule.enabled = false;
            view.camera.enabled = false;
            view.camera.tag = "Untagged";
            var listener = view.camera.GetComponent<AudioListener>();
            if (listener != null) listener.enabled = false;
            view.gameObject.SetActive(false);
            SetBoolean(halo, "networkControlled", true);

            var owner = CreateChild(root, "OwnerAudio", Vector3.up * 1.5f);
            var others = CreateChild(root, "RemotePresentation", Vector3.zero);
            var shot = CreateChild(root, "NetworkShotOrigin", Vector3.up * 1.5f);
            var player = root.GetComponent<PlayerGhost>();
            SetReference(player, "m_OwnerVisuals", owner.gameObject);
            SetReference(player, "m_OtherPlayerVisuals", others.gameObject);
            SetReference(player, "m_Animator3P", null);
            SetReference(player, "<ShotOrigin>k__BackingField", shot);
            SetReference(player, "<VisualShotOrigin1P>k__BackingField", halo.haloVisual);
            SetReference(player, "<VisualShotOrigin3P>k__BackingField", halo.haloVisual);
            var controller = root.GetComponent<FirstPersonController>();
            SetReference(controller, "m_Animator_1P", null);
            SetReference(controller, "m_Animator_3P", null);
            var consts = root.GetComponent<PredictedPlayerControllerConstsAuthoring>();
            SetFloat(consts, "<WalkSpeed>k__BackingField", movement.m_MoveSpeed);
            SetFloat(consts, "<SprintSpeed>k__BackingField", movement.m_SprintSpeed);
            SetFloat(consts, "<Gravity>k__BackingField", movement.m_Gravity);
            SetFloat(consts, "<JumpHeight>k__BackingField", movement.m_JumpHeight);

            var presentation = root.AddComponent<DollSingerNetworkPresentation>();
            SetReference(presentation, "m_Model", movement);
            SetReference(presentation, "m_Input", input);
            SetReference(presentation, "m_View", view);
            SetReference(presentation, "m_Halo", halo);
            model.SetActive(false);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static bool StripTemplateVisuals(GameObject root)
    {
        bool changed = false;
        foreach (var path in new[] { "ViewPoint/Armature_1P", "Armature_3P", "Reticle/Sphere" })
        {
            var child = root.transform.Find(path);
            if (child == null) continue;
            UnityEngine.Object.DestroyImmediate(child.gameObject);
            changed = true;
        }
        return changed;
    }

    private static void RemoveTemplateVisuals()
    {
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            if (StripTemplateVisuals(root)) PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    private static void RegisterHaloWeapon()
    {
        const string path = "Assets/DollSinger/HaloWeapon.asset";
        var registry = AssetDatabase.LoadAssetAtPath<WeaponRegistry>("Assets/Data/Weapons/WeaponRegistry.asset");
        var halo = AssetDatabase.LoadAssetAtPath<WeaponData>(path);
        if (halo == null)
        {
            halo = UnityEngine.Object.Instantiate(registry.Weapons[0]);
            halo.name = "HaloWeapon";
            halo.WeaponName = "Halo";
            halo.WeaponFireSfx = null;
            halo.WeaponReloadSfx = null;
            halo.MuzzleFlashVfxPrefab = default;
            AssetDatabase.CreateAsset(halo, path);
        }
        if (registry.Weapons.Count == 2)
        {
            registry.Weapons.Add(halo);
            EditorUtility.SetDirty(registry);
        }
        else if (registry.Weapons.Count < 3 || registry.Weapons[2] != halo)
            throw new InvalidOperationException("Weapon ID 2 must be reserved for the DollSinger Halo definition.");
    }

    private static void RegisterPrefab()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        var guid = AssetDatabase.AssetPathToGUID(PrefabPath);
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null) throw new InvalidOperationException("Addressables settings are required.");
        var entry = settings.CreateOrMoveEntry(guid, settings.DefaultGroup);
        entry.address = PrefabPath;

        var scene = SceneManager.GetSceneByPath(ResourcesScenePath);
        bool openedHere = !scene.isLoaded;
        if (openedHere) scene = EditorSceneManager.OpenScene(ResourcesScenePath, OpenSceneMode.Additive);
        try
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var authoring in root.GetComponentsInChildren<PlayerEntityPrefabsAuthoring>(true))
                    SetReference(authoring, "<DollSingerEntityPrefab>k__BackingField", prefab.GetComponent<GhostAuthoringComponent>());
                foreach (var authoring in root.GetComponentsInChildren<GhostPrefabsAuthoring>(true))
                {
                    var serialized = new SerializedObject(authoring);
                    var list = serialized.FindProperty("<GhostPrefabs>k__BackingField");
                    bool found = false;
                    for (int i = 0; i < list.arraySize; i++)
                        found |= list.GetArrayElementAtIndex(i).FindPropertyRelative("m_AssetGUID").stringValue == guid;
                    if (!found)
                    {
                        int index = list.arraySize;
                        list.InsertArrayElementAtIndex(index);
                        var reference = list.GetArrayElementAtIndex(index);
                        reference.FindPropertyRelative("m_AssetGUID").stringValue = guid;
                        reference.FindPropertyRelative("m_SubObjectName").stringValue = "";
                        reference.FindPropertyRelative("m_SubObjectType").stringValue = "";
                        reference.FindPropertyRelative("m_SubObjectGUID").stringValue = "";
                        serialized.ApplyModifiedPropertiesWithoutUndo();
                    }
                }
            }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
        finally
        {
            if (openedHere) EditorSceneManager.CloseScene(scene, true);
        }
        AssetDatabase.SaveAssets();
    }

    private static Transform CreateChild(GameObject parent, string name, Vector3 position)
    {
        var child = new GameObject(name).transform;
        child.SetParent(parent.transform, false);
        child.localPosition = position;
        return child;
    }

    private static void SetReference(UnityEngine.Object target, string name, UnityEngine.Object value)
    {
        var serialized = new SerializedObject(target);
        serialized.FindProperty(name).objectReferenceValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetBoolean(UnityEngine.Object target, string name, bool value)
    {
        var serialized = new SerializedObject(target);
        serialized.FindProperty(name).boolValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetFloat(UnityEngine.Object target, string name, float value)
    {
        var serialized = new SerializedObject(target);
        serialized.FindProperty(name).floatValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }
}
