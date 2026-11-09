using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Unity.Mathematics;
using Unity.MP_FPS;
using UnityEditor;
using UnityEngine;

public static class DeathVoiceReview
{
    // Apply only the production Death row, without rebuilding other edited banks.
    public static string Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Use Edit mode");
        const string path = "Assets/Audio/Moonkov/Clips/PlayerDeath/PlayerDeath.wav";
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var importer = (AudioImporter)AssetImporter.GetAtPath(path);
        var settings = importer.defaultSampleSettings;
        settings.loadType = AudioClipLoadType.DecompressOnLoad;
        settings.compressionFormat = AudioCompressionFormat.PCM;
        settings.sampleRateSetting = AudioSampleRateSetting.OverrideSampleRate;
        settings.sampleRateOverride = 48000; settings.preloadAudioData = true;
        importer.defaultSampleSettings = settings; importer.forceToMono = true; importer.loadInBackground = false;
        var serialized = new SerializedObject(importer); serialized.FindProperty("m_Normalize").boolValue = false;
        serialized.ApplyModifiedPropertiesWithoutUndo(); importer.SaveAndReimport();
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        var rows = (Array)typeof(MoonkovAudioSetup).GetField("Table", flags).GetValue(null);
        var row = rows.Cast<object>().Single(r => (string)r.GetType().GetField("Name").GetValue(r) == "Death");
        var bank = (SoundDef)typeof(MoonkovAudioSetup).GetMethod("Define", flags).Invoke(null, new[] { row });
        AssetDatabase.SaveAssetIfDirty(bank);
        if (MoonkovAudio.Library.Death != bank || bank.Clips.Single().length < 3f ||
            bank.PitchAndVolumeInfo.VolumeMin != -3 || bank.PitchAndVolumeInfo.VolumeMax != -2)
            throw new InvalidOperationException("Death row did not reach production library");
        return "Death row installed: 3.09 seconds, -3 to -2 dB, original pitch, PCM";
    }

    public static async Task<string> Run()
    {
        if (!Application.isPlaying || GameManager.Instance) throw new InvalidOperationException("Use isolated empty Play scene");
        const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        var instance = typeof(GameManager).GetProperty("Instance", BindingFlags.Static | BindingFlags.Public);
        var holder = new GameObject("Death voice manager"); holder.SetActive(false);
        var manager = holder.AddComponent<GameManager>();
        var actor = new GameObject("Death voice listener"); actor.AddComponent<AudioListener>();
        var sources = new GameObject("Death voice pool"); var bodies = new GameObject("Death voice bodies");
        var pool = (SoundGameObjectPool)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(SoundGameObjectPool));
        var system = new SoundSystem(); bool background = Application.runInBackground; Application.runInBackground = true;
        int checks = 0;
        void Check(bool value, string message) { checks++; if (!value) throw new InvalidOperationException(message); }
        try
        {
            typeof(SoundGameObjectPool).GetField("m_SourceHolder", hidden).SetValue(pool, sources);
            pool.SoundGameObjectList = new List<SoundGameObject>();
            for (int i = 0; i < 4; i++) pool.SoundGameObjectList.Add(pool.Create());
            for (int i = 0; i < 4; i++) pool.SoundGameObjectList[i].NextAvailable = pool.SoundGameObjectList[(i + 1) % 4];
            pool.NextSoundGameObject = pool.SoundGameObjectList[0]; pool.AvailableSoundObjectCount = 4;
            var lib = MoonkovAudio.Library; system.Init(actor.transform, 4, pool, lib.Death.UnityAudioMixer);
            instance.SetValue(null, manager); typeof(GameManager).GetField("m_SoundSystem", hidden).SetValue(manager, system);
            var settings = Resources.Load<CorpsePresentationSettings>("Moonkov/CorpsePresentation");
            var rpc = new RaidCorpseRpc { LootId = 9876, Version = 1, CharacterIndex = 0, Rotation = quaternion.identity };
            var body = CorpseVisual.Spawn(bodies.transform, settings, rpc);
            var source = pool.SoundGameObjectList.Select(s => s.m_AudioSource).Single(s => s.isPlaying);
            Check(source.clip == lib.Death.Clips.Single() && source.clip.length > 3f, "Fresh death did not play the full scream");
            Check(source.volume >= .70f && source.pitch == 1 && source.spatialBlend == 1,
                "Actual death gain, pitch or spatial routing is wrong");
            await Task.Delay(300); system.UpdateSoundSystem(false);
            Check(source.time > .15f, "Playback clock did not advance");
            UnityEngine.Object.DestroyImmediate(body.gameObject);
            new MoonkovPlayerAudio().Stop();
            await Task.Delay(250); system.UpdateSoundSystem(false);
            Check(source.isPlaying && source.time > .4f, "Death cleanup truncated the scream");
            rpc.Age = 10; rpc.LootId++;
            var late = CorpseVisual.Spawn(bodies.transform, settings, rpc);
            Check(pool.AvailableSoundObjectCount == 3, "Old corpse replayed death audio");
            UnityEngine.Object.DestroyImmediate(late.gameObject);
            var timer = System.Diagnostics.Stopwatch.StartNew();
            float end = source.clip.length, furthest = source.time;
            while (source.isPlaying && timer.Elapsed.TotalSeconds < 6)
            {
                furthest = Mathf.Max(furthest, source.time);
                await Task.Delay(25); system.UpdateSoundSystem(false);
            }
            system.UpdateSoundSystem(false);
            Check(furthest >= end - .2f, "Scream stopped before its recorded tail");
            Check(!source.isPlaying && pool.AvailableSoundObjectCount == 4, "Death voice did not complete and recycle");
            return $"PASS {checks} actual corpse trigger, gain, pitch, complete playback, cleanup survival, late-corpse silence and recycling checks";
        }
        finally
        {
            system.KillAll(); instance.SetValue(null, null); Application.runInBackground = background;
            UnityEngine.Object.DestroyImmediate(holder); UnityEngine.Object.DestroyImmediate(actor);
            UnityEngine.Object.DestroyImmediate(sources); UnityEngine.Object.DestroyImmediate(bodies);
        }
    }
}
