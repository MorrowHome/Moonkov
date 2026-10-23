using System;
using Unity.MP_FPS;
using UnityEditor;
using UnityEngine;

public static class MoonkovCombatAudioChecks
{
    [MenuItem("Tools/Moonkov/Check Combat Feedback Audio")]
    public static void Run()
    {
        var lib = Resources.Load<MoonkovAudioLibrary>("Moonkov/AudioLibrary");
        foreach (var cue in new[] {lib.ShieldImpact, lib.ShieldBreak, lib.ShieldRestore, lib.TacticalDeploy,
            lib.TacticalAlarm, lib.TacticalExplosion, lib.TacticalGas, lib.TacticalCough})
        {
            if (!cue || cue.Clips.Count == 0 || cue.Clips.Exists(c => !c) || cue.MaxVoices <= 0 ||
                cue.DistanceInfo.SpatialBlend != 1 || !cue.Occlusion || cue.MixerGroup != SoundMixer.SoundMixerGroup.SFX)
                throw new InvalidOperationException("Invalid pooled combat audio configuration");
            foreach (var clip in cue.Clips)
            {
                clip.LoadAudioData();
                // Compressed-in-memory legacy explosion clips cannot expose PCM via GetData.
                if (clip.loadState != AudioDataLoadState.Loaded || clip.samples <= 0)
                    throw new InvalidOperationException("Unloaded combat clip " + clip.name);
                if (clip.loadType != AudioClipLoadType.DecompressOnLoad) continue;
                var samples = new float[clip.samples * clip.channels];
                if (!clip.GetData(samples, 0) || Array.TrueForAll(samples, x => Mathf.Abs(x) < .0001f))
                    throw new InvalidOperationException("Silent combat clip " + clip.name);
            }
        }
        if (lib.ShieldBreak.Priority >= lib.TacticalCough.Priority)
            throw new InvalidOperationException("Breathing must yield to shield warnings");
        Debug.Log("Combat audio: eight populated spatial SFX banks, loaded clips, readable PCM and priority budgets passed.");
    }
}
