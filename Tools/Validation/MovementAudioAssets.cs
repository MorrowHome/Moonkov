using System;
using System.IO;
using System.Collections.Generic;
using Unity.MP_FPS;
using UnityEditor;
using UnityEngine;

public static class MovementAudioAssets
{
    [Serializable] public class Row { public string name; public float dbMin, dbMax; public List<string> clips = new List<string>(); }
    [Serializable] public class Report { public List<Row> banks = new List<Row>(); }
    public static string Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Use Edit mode");
        MoonkovPresenceAudioSetup.Install(); MoonkovRaidAudioSetup.InstallBreathing();
        var result = MoonkovBreathingAudioChecks.Run(); MoonkovRaidAudioChecks.Run();
        return result + "; " + Export();
    }
    public static string Export()
    {
        var lib = Resources.Load<MoonkovAudioLibrary>("Moonkov/AudioLibrary");
        var report = new Report(); string directory = "LocalData/GameAudioReview/MovementAfter";
        Directory.CreateDirectory(directory);
        foreach (var bank in new[] { lib.RegolithFootsteps, lib.MetalFootsteps, lib.HardFootsteps, lib.Cloth, lib.GearMotion,
            lib.SuitBreath, lib.SuitBreathEffort, lib.JumpEffort, lib.Jump, lib.Land })
        {
            var row = new Row { name = bank.name, dbMin = bank.PitchAndVolumeInfo.VolumeMin, dbMax = bank.PitchAndVolumeInfo.VolumeMax };
            foreach (var clip in bank.Clips)
            {
                row.clips.Add(clip.name); var samples = new float[clip.samples * clip.channels];
                if (!clip.GetData(samples, 0)) throw new InvalidOperationException("Cannot export imported " + clip.name);
                using (var writer = new BinaryWriter(File.Create(directory + "/" + clip.name + ".f32")))
                    foreach (float sample in samples) writer.Write(sample);
            }
            report.banks.Add(row);
        }
        File.WriteAllLines(directory + "/Banks.tsv", report.banks.ConvertAll(row => string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}\t{1}\t{2}\t{3}", row.name, row.dbMin, row.dbMax, string.Join(",", row.clips))));
        return "exported 10 movement banks";
    }
}
