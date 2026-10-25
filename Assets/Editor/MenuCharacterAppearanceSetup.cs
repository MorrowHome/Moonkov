using System;
using UnityEditor;
using UnityEngine;
using Unity.MP_FPS;
using Unity.MP_FPS.Client;
using Unity.MP_FPS.DollSinger;

public static class MenuCharacterAppearanceSetup
{
    [MenuItem("Tools/Moonkov/Bind Character Equipment Previews")]
    public static void BindAll()
    {
        foreach (int character in new[] { GameSettings.DollSingerCharacterIndex,
            GameSettings.HatsuneMikuCharacterIndex, GameSettings.ShianCharacterIndex })
        {
            string path = "Assets/Resources/" + GameSettings.MenuCharacterSettingsPath(character) + ".asset";
            Bind(AssetDatabase.LoadAssetAtPath<MenuCharacterSettings>(path), character);
        }
    }

    public static void Bind(MenuCharacterSettings settings, int character)
    {
        if (!settings) throw new InvalidOperationException("Character preview settings are missing.");
        string path = character == GameSettings.HatsuneMikuCharacterIndex
            ? "Assets/HatsuneMiku/Prefabs/HatsuneMikuNetworkPlayer.prefab"
            : character == GameSettings.ShianCharacterIndex
                ? "Assets/Shian/Prefabs/ShianNetworkPlayer.prefab"
                : "Assets/DollSinger/Prefabs/DollSingerNetworkPlayer.prefab";
        var player = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        var halo = player ? player.GetComponentInChildren<DollSingerHaloAim>(true) : null;
        var registry = AssetDatabase.LoadAssetAtPath<WeaponRegistry>("Assets/Data/Weapons/WeaponRegistry.asset");
        if (!halo || !registry) throw new InvalidOperationException("Gameplay equipment source is missing: " + path);
        settings.HaloSource = halo;
        settings.WeaponRegistry = registry;
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssetIfDirty(settings);
    }
}
