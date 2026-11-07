using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.MP_FPS;
using UnityEditor;
using UnityEngine;

public static class CoughVoiceReview
{
    public static string Begin()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (EditorApplication.isPlayingOrWillChangePlaymode || scene.isDirty || string.IsNullOrEmpty(scene.path) ||
            UnityEngine.SceneManagement.SceneManager.sceneCount != 1) throw new InvalidOperationException("Need one saved clean Edit scene");
        SessionState.SetString("Moonkov.CoughReview.Scene", scene.path);
        UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
            UnityEditor.SceneManagement.NewSceneMode.Single);
        EditorApplication.isPlaying = true;
        return "Entering isolated empty Play scene";
    }
    public static string Restore()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play first");
        var path = SessionState.GetString("Moonkov.CoughReview.Scene", "");
        if (string.IsNullOrEmpty(path)) throw new InvalidOperationException("No saved scene to restore");
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene(path);
        SessionState.EraseString("Moonkov.CoughReview.Scene");
        return "Restored " + path;
    }
    public static string Run()
    {
        if (!Application.isPlaying || GameManager.Instance)
            throw new InvalidOperationException("Use isolated Play mode without a live manager");
        const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        var property = typeof(GameManager).GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
        var holder = new GameObject("Cough review manager"); holder.SetActive(false);
        var manager = holder.AddComponent<GameManager>();
        var actor = new GameObject("Cough review actor");
        var sourceHolder = new GameObject("Cough review pool");
        var pool = (SoundGameObjectPool)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(SoundGameObjectPool));
        var system = new SoundSystem(); var audio = new MoonkovPlayerAudio();
        int checks = 0;
        void Check(bool condition, string message) { checks++; if (!condition) throw new InvalidOperationException(message); }
        SoundSystem.SoundInfo Voice(string field) => (SoundSystem.SoundInfo)typeof(MoonkovPlayerAudio).GetField(field, hidden).GetValue(audio);
        try
        {
            typeof(SoundGameObjectPool).GetField("m_SourceHolder", hidden).SetValue(pool, sourceHolder);
            pool.SoundGameObjectList = new List<SoundGameObject>();
            for (int i = 0; i < 8; i++) pool.SoundGameObjectList.Add(pool.Create());
            for (int i = 0; i < 8; i++) pool.SoundGameObjectList[i].NextAvailable = pool.SoundGameObjectList[(i + 1) % 8];
            pool.NextSoundGameObject = pool.SoundGameObjectList[0]; pool.AvailableSoundObjectCount = 8;
            var lib = Resources.Load<MoonkovAudioLibrary>("Moonkov/AudioLibrary");
            system.Init(actor.transform, 8, pool, lib.TacticalCough.UnityAudioMixer);
            property.SetValue(null, manager); typeof(GameManager).GetField("m_SoundSystem", hidden).SetValue(manager, system);
            foreach (bool owner in new[] { true, false })
            {
                audio.Stop(); system.KillAll(); audio = new MoonkovPlayerAudio();
                var ghost = new PredictedPlayerGhost(); RaidHealth.Initialize(ref ghost); ghost.BreathableAir = true;
                ghost.ControllerState.CurrentRotation = Unity.Mathematics.quaternion.identity;
                ghost.TacticalCoughSeconds = 8;
                audio.Update(ghost, actor.transform, owner, .02f);
                Check(Voice("m_CoughSound") == null, "Late join must not duplicate a voice in initialization");
                audio.Update(ghost, actor.transform, owner, .02f);
                var cough = Voice("m_CoughSound");
                Check(cough?.Handle?.IsValid() == true && cough.SoundDefinition == lib.TacticalCough, "Actual cough must allocate a real pooled source");
                Check(cough.SoundTransform == actor.transform && Mathf.Abs(cough.SoundDefOverrideInfo.SpatialBlend.GetValueOrDefault(-1) - (owner ? .05f : 1)) < .001f,
                    "Cough must follow actor and use owner/remote spatial routing");
                for (int i = 0; i < 100; i++) audio.Update(ghost, actor.transform, owner, .02f);
                Check(Voice("m_CoughSound") == cough, "Repeated snapshots restarted cough");
                Check(Voice("m_BreathSound") == null && Voice("m_GroanSound") == null, "Other body voices overlapped cough");
                ghost.LastImpactTick = 1; audio.Update(ghost, actor.transform, owner, .02f);
                Check(Voice("m_CoughSound") == null && Voice("m_PainSound")?.Handle?.IsValid() == true, "Impact cry must take priority");
                ghost.CurrentHealth = 0; audio.Update(ghost, actor.transform, owner, .02f);
                Check(Voice("m_CoughSound") == null && Voice("m_PainSound") == null, "Death retained voice handles");
                audio.Stop(); system.KillAll();
                Check(pool.AvailableSoundObjectCount == 8, "Voice sources leaked");
            }
            return "PASS " + checks + " owner/remote cough routing, priority, snapshot and cleanup checks";
        }
        finally
        {
            audio.Stop(); system.KillAll(); property.SetValue(null, null);
            UnityEngine.Object.DestroyImmediate(holder); UnityEngine.Object.DestroyImmediate(actor); UnityEngine.Object.DestroyImmediate(sourceHolder);
        }
    }
}
