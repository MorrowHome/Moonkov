using System;
using System.Collections.Generic;
using Unity.MP_FPS;
using Unity.MP_FPS.SpiderLab;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Keep the authored ceiling spider intact. The boss owns a separate
/// profile and Ghost prefab while reusing the finished spider rig.</summary>
public static class SpiderBossSetup
{
    public const string ProfilePath = "Assets/AlienCreatures/Profiles/SpiderBoss.asset";
    public const string PrefabPath = "Assets/AlienCreatures/Prefabs/XenoSpiderBoss.prefab";

    [MenuItem("Tools/Moonkov/Aliens/Install giant spider boss")]
    public static void Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play first.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Save scene edits first.");
        ApplyProfiles();
        if (!AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) &&
            !AssetDatabase.CopyAsset("Assets/AlienCreatures/Prefabs/XenoSpider.prefab", PrefabPath))
            throw new InvalidOperationException("Cannot create the spider boss prefab.");
        var prefab = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            prefab.name = "XenoSpiderBoss";
            var data = new SerializedObject(prefab.GetComponent<ProceduralAlienEnemy>());
            data.FindProperty("profile").objectReferenceValue = AssetDatabase.LoadAssetAtPath<AlienCreatureProfile>(ProfilePath);
            data.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(prefab, PrefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(prefab); }
        var addressables = AddressableAssetSettingsDefaultObject.Settings;
        if (!addressables) throw new InvalidOperationException("Missing Addressables settings.");
        string guid = AssetDatabase.AssetPathToGUID(PrefabPath);
        var entry = addressables.CreateOrMoveEntry(guid, addressables.DefaultGroup);
        entry.address = PrefabPath;
        AlienEnemySetup.RegisterResources(new[] { AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) });

        var scene = SceneManager.GetSceneByPath(AlienAmbushSetup.ScenePath);
        bool opened = !scene.isLoaded;
        if (opened) scene = EditorSceneManager.OpenScene(AlienAmbushSetup.ScenePath, OpenSceneMode.Additive);
        try
        {
            AlienEncounterSettings settings = null;
            foreach (var root in scene.GetRootGameObjects())
                if ((settings = root.GetComponentInChildren<AlienEncounterSettings>(true))) break;
            if (!settings) throw new InvalidOperationException("Missing formal alien encounters.");
            var data = new SerializedObject(settings);
            var slots = data.FindProperty("encounters");
            var retained = new HashSet<AlienSpecies>();
            int bosses = 0;
            for (int i = 0; i < slots.arraySize; i++)
            {
                var slot = slots.GetArrayElementAtIndex(i);
                var profile = slot.FindPropertyRelative("Profile").objectReferenceValue as AlienCreatureProfile;
                if (!profile) continue;
                if (profile.IsSpiderBoss) { bosses++; continue; }
                if (profile.Species != AlienSpecies.Tentacle && profile.Species != AlienSpecies.Roots) continue;
                // Retain one ordinary encounter of each former boss. Their other
                // wide-area slots become candidates for the same one-life spider.
                if (retained.Add(profile.Species)) continue;
                slot.FindPropertyRelative("Profile").objectReferenceValue = AssetDatabase.LoadAssetAtPath<AlienCreatureProfile>(ProfilePath);
                slot.FindPropertyRelative("Prefab").FindPropertyRelative("<GhostPrefab>k__BackingField")
                    .FindPropertyRelative("m_AssetGUID").stringValue = guid;
                slot.FindPropertyRelative("Ambush").boolValue = false;
                slot.FindPropertyRelative("SurfaceNormal").vector3Value = Vector3.up;
                bosses++;
            }
            if (bosses == 0) throw new InvalidOperationException("No former boss candidate slots to replace.");
            data.FindProperty("maximumBosses").intValue = 1;
            if (data.ApplyModifiedPropertiesWithoutUndo())
            { EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); }
        }
        finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
        AssetDatabase.SaveAssets();
        Debug.Log("Giant spider boss installed; ceiling spiders retained; tentacle and roots demoted to ordinary enemies.");
    }

    public static void ApplyProfiles()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play first.");
        var boss = AssetDatabase.LoadAssetAtPath<AlienCreatureProfile>(ProfilePath);
        if (!boss)
        {
            var spider = AssetDatabase.LoadAssetAtPath<AlienCreatureProfile>("Assets/AlienCreatures/Profiles/Spider.asset");
            if (!spider) throw new InvalidOperationException("Missing original spider profile.");
            boss = Object.Instantiate(spider); boss.name = "SpiderBoss";
            AssetDatabase.CreateAsset(boss, ProfilePath);
        }
        Undo.RecordObject(boss, "Configure giant spider boss");
        boss.Species = AlienSpecies.Spider; boss.Boss = true; boss.ThreatTier = AlienThreatTier.Boss;
        boss.DisplayName = "巨型猎蛛";
        boss.EncounterHint = "抬足重击时退开，低身扑冲时横移；半血后加快追击，收招时反击。";
        boss.Scale = 3.6f; boss.Health = 3200; boss.Damage = 42;
        boss.Approach = AlienApproach.Direct; boss.TurnSpeed = 95;
        boss.Speed = 4.2f / boss.Scale; boss.SightRange = 60; boss.HearingRange = 28;
        boss.MemorySeconds = 14; boss.PatrolRadius = 4;
        boss.AttackReach = 1.18f; boss.Windup = 1.4f; boss.Recovery = 1.65f; boss.AttackCooldown = 2.8f;
        boss.SpiderAmbushRange = 12; boss.SpiderWindup = 1.65f;
        boss.SpiderStrikeSeconds = 1; boss.SpiderRecovery = 1.8f;
        boss.BossPursuitSpeed = 4.2f; boss.BossRepositionSpeed = 5.5f;
        boss.BossRepositionSeconds = .65f; boss.BossRepositionCooldown = 5.5f; boss.BossPreferredDistance = 5;
        boss.BossDisplaySeconds = 1.8f; boss.EnrageHealthFraction = .5f;
        boss.MucusEveryAttacks = 3; boss.MucusRange = 30; boss.MucusSpeed = 22; boss.MucusBlindSeconds = 1.8f;
        boss.LiquidDistance = 16; boss.MaximumLiquidEnemies = 1;
        boss.Gait.forwardLean = 12; boss.Gait.bodyBob = .025f; boss.Gait.roll = 2;
        EditorUtility.SetDirty(boss); AssetDatabase.SaveAssetIfDirty(boss);
        Demote("Tentacle", "触肢兽", "前方横扫；绕到侧后方反击。", 280, 36, 1, 5.6f);
        Demote("Roots", "伏根兽", "扎根攻击附近落点；离开预兆位置。", 320, 36, 1.1f, 1.4f);
    }

    private static void Demote(string asset, string title, string hint, float health, float damage, float scale, float speed)
    {
        var p = AssetDatabase.LoadAssetAtPath<AlienCreatureProfile>("Assets/AlienCreatures/Profiles/" + asset + ".asset");
        if (!p) throw new InvalidOperationException("Missing former boss profile " + asset);
        Undo.RecordObject(p, "Remove former boss role");
        p.Boss = false; p.ThreatTier = AlienThreatTier.Common; p.DisplayName = title; p.EncounterHint = hint;
        p.Health = health; p.Damage = damage; p.Scale = scale; p.Speed = speed / scale;
        p.Branch = p.Species == AlienSpecies.Roots ? BranchCreatureSettings.RootsDefaults() : new BranchCreatureSettings();
        p.Approach = AlienApproach.Direct; p.SightRange = 32; p.HearingRange = 14; p.MemorySeconds = 8;
        p.TurnSpeed = 180; p.MucusEveryAttacks = 0; p.PatrolRadius = 5;
        EditorUtility.SetDirty(p); AssetDatabase.SaveAssetIfDirty(p);
    }
}
