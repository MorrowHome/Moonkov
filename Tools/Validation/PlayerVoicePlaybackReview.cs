using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Unity.MP_FPS;
using UnityEditor;
using UnityEngine;

// Uses the SoundDef Inspector's real playback path, without Play, profiles or networking.
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
        var data = new SoundGameObject.AudioComponentData {
            ASource = go.AddComponent<AudioSource>(), LowPassFilter = go.AddComponent<AudioLowPassFilter>(),
            HighPassFilter = go.AddComponent<AudioHighPassFilter>(), DistortionFilter = go.AddComponent<AudioDistortionFilter>()
        };
        var source = new SoundGameObject(go, data);
        var emitter = new SoundEmitter(null) { Reserved = SoundEmitter.ReservedInfo.ReservedEmitterAndAudioSources };
        emitter.ActiveSoundGameObjects.Add(source);
        try
        {
            var lib = Resources.Load<MoonkovAudioLibrary>("Moonkov/AudioLibrary");
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
                    emitter.SetSoundDef(bank); emitter.CopyOverrideDataFromSoundDef(bank);
                    emitter.SoundDefOverrideInfo.SpatialBlend = 0;
                    emitter.Activate(); source.EnableAudioFilterComponents(bank); source.Enable(emitter);
                    emitter.Volume = name == "SuitBreath" ? .7f : name == "SuitBreathEffort" ? 1.25f : 1;
                    emitter.Play(null, mixer.MixerGroups);
                    var audio = data.ASource;
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
                    audio.Stop();
                    if (heard.Count == bank.Clips.Count) break;
                }
                if (heard.Count != bank.Clips.Count) throw new InvalidOperationException("Not all variants played: " + name);
            }
            return "PASS: " + lines.Count + " complete native AudioSource plays; audio clock, SFX, gain, non-repeat and completion.";
        }
        catch (Exception error) { lines.Add("FAIL " + error); throw; }
        finally
        {
            data.ASource.Stop(); UnityEngine.Object.DestroyImmediate(go);
            EditorApplication.update -= EditorApplication.QueuePlayerLoopUpdate;
            if (view) view.audioPlay = previousAudio;
            EditorUtility.audioMasterMute = previousMute;
            Directory.CreateDirectory("LocalData/PlayerVoiceReview");
            File.WriteAllLines("LocalData/PlayerVoiceReview/" + string.Join("-", banks) + "-playback.txt", lines);
        }
    }
}
