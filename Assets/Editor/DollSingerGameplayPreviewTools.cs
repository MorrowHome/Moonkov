using System;
using Unity.MP_FPS;
using Unity.MP_FPS.DollSinger;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Installs the offline gameplay driver without changing the reusable character prefab.</summary>
public static class DollSingerGameplayPreviewTools
{
    private const string GameplayPrefabPath = "Assets/DollSinger/Prefabs/DollSingerNetworkPlayer.prefab";

    [MenuItem("Tools/Moon Environment/Use Gameplay Controls In Current Preview")]
    public static void InstallInCurrentPreview()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Install preview controls in Edit Mode.");
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        int count = 0;
        foreach (var root in scene.GetRootGameObjects())
        foreach (var marker in root.GetComponentsInChildren<Unity.MP_FPS.Moon.Preview.Apollo12PreviewStart>(true))
        {
            Install(marker.gameObject);
            count++;
        }
        if (count == 0) throw new InvalidOperationException("This scene has no Moon map preview player.");
        EditorSceneManager.MarkSceneDirty(scene);
    }

    // Invoked by the environment's asmdef-based builder through its existing reflection boundary.
    public static void Install(GameObject player)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || player.GetComponentInChildren<PlayerGhost>(true))
            throw new InvalidOperationException("Only install on an offline preview player in Edit Mode.");
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(GameplayPrefabPath);
        var model = player.GetComponentInChildren<DollSingerMovement>(true);
        var view = player.GetComponentInChildren<DollSingerView>(true);
        var input = player.GetComponentInChildren<DollSingerInput>(true);
        if (!source || !model || !view || !input)
            throw new InvalidOperationException("Missing gameplay source or preview character references.");
        var motor = model.GetComponent<FirstPersonController>();
        if (!motor) motor = Undo.AddComponent<FirstPersonController>(model.gameObject);
        Undo.RecordObject(motor, "Use gameplay motor in preview");
        motor.ConfigureStandalonePreview(source.GetComponent<FirstPersonController>());

        var capsule = model.GetComponent<CharacterController>();
        var sourceCapsule = source.GetComponent<CharacterController>();
        Undo.RecordObject(capsule, "Match gameplay collision capsule");
        capsule.height = sourceCapsule.height;
        capsule.radius = sourceCapsule.radius;
        capsule.center = sourceCapsule.center;
        capsule.slopeLimit = sourceCapsule.slopeLimit;
        capsule.stepOffset = sourceCapsule.stepOffset;
        capsule.skinWidth = sourceCapsule.skinWidth;
        capsule.minMoveDistance = sourceCapsule.minMoveDistance;
        capsule.enabled = true;

        // Copy value settings, never prefab references (camera, bones, renderer, input owner).
        CopyControlValues(source.GetComponentInChildren<DollSingerInput>(true), input);
        CopyControlValues(source.GetComponentInChildren<DollSingerView>(true), view);
        CopyControlValues(source.GetComponentInChildren<DollSingerMovement>(true), model);

        var driver = model.GetComponent<DollSingerGameplayPreview>();
        if (!driver) driver = Undo.AddComponent<DollSingerGameplayPreview>(model.gameObject);
        var serialized = new SerializedObject(driver);
        serialized.FindProperty("gameplayPrefab").objectReferenceValue = source;
        serialized.FindProperty("model").objectReferenceValue = model;
        serialized.FindProperty("view").objectReferenceValue = view;
        serialized.FindProperty("input").objectReferenceValue = input;
        serialized.ApplyModifiedProperties();
        foreach (var component in new Component[] { motor, capsule, driver, input, view, model })
        {
            EditorUtility.SetDirty(component);
            PrefabUtility.RecordPrefabInstancePropertyModifications(component);
        }
    }

    private static void CopyControlValues(Component source, Component destination)
    {
        if (!source) throw new InvalidOperationException("Gameplay source is missing " + destination.GetType().Name);
        Undo.RecordObject(destination, "Match gameplay control settings");
        var from = new SerializedObject(source);
        var to = new SerializedObject(destination);
        var property = from.GetIterator();
        if (property.NextVisible(true)) do
        {
            // Stay at the top level: skip Unity identity, references and complex collections.
            if (property.name.StartsWith("m_") &&
                (property.name == "m_Enabled" || property.name == "m_EditorHideFlags")) continue;
            switch (property.propertyType)
            {
                case SerializedPropertyType.Float:
                case SerializedPropertyType.Boolean:
                case SerializedPropertyType.Integer:
                case SerializedPropertyType.Enum:
                case SerializedPropertyType.LayerMask:
                case SerializedPropertyType.Vector2:
                case SerializedPropertyType.Vector3:
                    to.CopyFromSerializedProperty(property);
                    break;
            }
        } while (property.NextVisible(false));
        to.ApplyModifiedProperties();
    }
}
