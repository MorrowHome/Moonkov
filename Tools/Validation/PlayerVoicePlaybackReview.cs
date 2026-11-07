using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Unity.MP_FPS;
using UnityEditor;
using UnityEngine;

// Uses the real sound pool and clip selection, without Play, profiles or networking.
public static class PlayerVoicePlaybackReview
{
    public static async Task<string> Check(string[] banks)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer)
            throw new InvalidOperationException("Use an idle Editor");
        var lines = new List<string>();
        var view = SceneView.lastActiveSceneView;
        bool previousAudio = view && view.audioPlay, previousMute = EditorUtility.audioMasterMute;
        if (view) view.audioPlay = true;
        EditorUtility.audioMasterMute = false;
        EditorApplication.update += EditorApplication.QueuePlayerLoopUpdate;
        var go = new GameObject("Player voice playback review") { hideFlags = HideFlags.HideAndDontSave };
        var holder = new GameObject("Player voice review pool") { hideFlags = HideFlags.HideAndDontSave };
        // The runtime constructor calls DontDestroyOnLoad, which is forbidden in Edit mode.
        // Supply only that lifetime shell here; source creation, selection, playback and recycling remain real.
        var pool = (SoundGameObjectPool)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(SoundGameObjectPool));
        var system = new SoundSystem();
        try
        {
            typeof(SoundGameObjectPool).GetField("m_SourceHolder", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(pool, holder);
            pool.SoundGameObjectList = new List<SoundGameObject> { pool.Create(), pool.Create() };
            pool.SoundGameObjectList[0].NextAvailable = pool.SoundGameObjectList[1];
            pool.SoundGameObjectList[1].NextAvailable = pool.SoundGameObjectList[0];
            pool.NextSoundGameObject = pool.SoundGameObjectList[0]; pool.AvailableSoundObjectCount = 2;
            var lib = Resources.Load<MoonkovAudioLibrary>("Moonkov/AudioLibrary");
            system.Init(go.transform, 2, pool, lib.SuitBreath.UnityAudioMixer);
            foreach (string name in banks)
            {
                var bank = (SoundDef)typeof(MoonkovAudioLibrary).GetField(name).GetValue(lib);
                if (!bank) throw new InvalidOperationException("Missing bank " + name);
                var mixer = new SoundMixer(bank.UnityAudioMixer);
                AudioClip previous = null;
                var heard = new HashSet<AudioClip>();
                // Check every variant through native clip selection, with bounded retries.
                for (int i = 0; i < Math.Max(1, bank.Clips.Count * 8); i++)
                {
                    var info = new SoundSystem.SoundInfo { SoundDefinition = bank, Position = Vector3.zero,
                        SoundDefOverrideInfo = new SoundEmitter.SoundDefOverrideData(bank) { SpatialBlend = 0 } };
                    float gain = name == "SuitBreath" ? .7f : name == "SuitBreathEffort" ? 1.25f : 1;
                    if (!system.PlayEmitter(info, gain) || !info.Handle.IsValid())
                        throw new InvalidOperationException("Pool allocation failed: " + name);
                    var audio = info.Handle.Emitter.ActiveSoundGameObjects[0].m_AudioSource;
                    if (!audio.isPlaying || !bank.Clips.Contains(audio.clip) || audio.volume <= 0 ||
                        audio.outputAudioMixerGroup != mixer.MixerGroups[(int)SoundMixer.SoundMixerGroup.SFX] ||
                        audio.spatialBlend != 0 || bank.Clips.Count > 1 && previous == audio.clip)
                        throw new InvalidOperationException("Invalid playback/selection: " + name);
                    previous = audio.clip;
                    if (heard.Add(audio.clip))
                    {
                        int before = audio.timeSamples;
                        await Task.Delay(200);
                        if (!audio.isPlaying || audio.timeSamples <= before)
                            throw new InvalidOperationException("Audio clock did not advance: " + name);
                        lines.Add($"PASS {name}/{audio.clip.name}: {audio.clip.length:F3}s, gain={audio.volume:F4}, pitch={audio.pitch:F3}, samples={audio.timeSamples}, SFX");
                        // Let each complete human performance finish during the native preview.
                        await Task.Delay(Mathf.CeilToInt(audio.clip.length / audio.pitch * 1000));
                        if (audio.isPlaying) throw new InvalidOperationException("One-shot did not finish: " + name);
                    }
                    system.Kill(info);
                    if (heard.Count == bank.Clips.Count) break;
                }
                if (heard.Count != bank.Clips.Count) throw new InvalidOperationException("Not all variants played: " + name);
                if (pool.AvailableSoundObjectCount != 2) throw new InvalidOperationException("Leaked pooled sources: " + name);
            }
            return "PASS: " + lines.Count + " complete native AudioSource plays; audio clock, SFX, gain, non-repeat and completion.";
        }
        catch (Exception error) { lines.Add("FAIL " + error); throw; }
        finally
        {
            system.KillAll(); UnityEngine.Object.DestroyImmediate(holder); UnityEngine.Object.DestroyImmediate(go);
            EditorApplication.update -= EditorApplication.QueuePlayerLoopUpdate;
            if (view) view.audioPlay = previousAudio;
            EditorUtility.audioMasterMute = previousMute;
            Directory.CreateDirectory("LocalData/PlayerVoiceReview");
            File.WriteAllLines("LocalData/PlayerVoiceReview/" + string.Join("-", banks) + "-playback.txt", lines);
        }
    }
}
