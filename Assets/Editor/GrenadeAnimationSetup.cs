using System;
using System.IO;
using Unity.MP_FPS;
using UnityEditor;
using UnityEngine;

// Blender owns authored poses. This importer never regenerates or overwrites the source.
public static class GrenadeAnimationSetup
{
    public const string Source = "ArtSource/DollSinger/GrenadeGestures.blend";
    public static readonly string[] Rigs = { "DollSinger", "HatsuneMiku", "Shian" };
    [MenuItem("Tools/Moonkov/Grenades/Import Blender hand motions")]
    public static string BakeAll()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Import outside Play.");
        foreach (string rig in Rigs)
        {
            string source = "SourceData/Animations/Grenades/" + rig + ".json";
            var text = File.ReadAllText(source);
            var data = JsonUtility.FromJson<GrenadePresentation.Library>(text);
            if (data.rig != rig || data.source == null || !data.source.StartsWith(Source))
                throw new InvalidDataException("Wrong grenade source: " + source);
            foreach (var motion in new[] { data.hold, data.prime, data.overhand, data.underhand })
                if (motion == null || motion.frames < 2 || motion.rotations.Length != motion.frames * data.bones.Length * 4)
                    throw new InvalidDataException("Incomplete grenade motion: " + source);
            File.WriteAllText("Assets/Resources/Moonkov/Grenades/" + rig + ".json", text);
        }
        AssetDatabase.Refresh();
        return "Imported compact Blender grips and aim/release poses for three production rigs.";
    }
}
