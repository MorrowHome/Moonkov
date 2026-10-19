using System;
using Unity.MP_FPS;
using UnityEditor;
using UnityEngine;

public static class MoonkovAlienAudioChecks
{
    private static void Require(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }
    [MenuItem("Tools/Moonkov/Check Species Combat Audio")]
    public static void Run()
    {
        var library = Resources.Load<MoonkovAudioLibrary>("Moonkov/AudioLibrary");
        foreach (string field in new[] { "AlienSwing", "AlienSpit", "AlienImpact", "AlienWetMove", "AlienHeavyStep", "AlienGrip", "AlienBossVoice" })
        {
            var sound = (SoundDef)typeof(MoonkovAudioLibrary).GetField(field).GetValue(library);
            Require(sound && sound.Clips.Count == 3, "Unwired bank " + field);
            Require(sound.Occlusion && sound.DistanceInfo.SpatialBlend == 1 && sound.Priority < 150 &&
                sound.MaxVoices > 0 && sound.MaxVoices <= 6, "Unbounded/unspatialized bank " + field);
            foreach (var clip in sound.Clips)
            {
                Require(clip && clip.channels == 1 && clip.frequency == 48000 && clip.length < 2.4f, "Clip format " + field);
                var importer = (AudioImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(clip));
                Require(importer.defaultSampleSettings.compressionFormat == AudioCompressionFormat.PCM &&
                    importer.defaultSampleSettings.loadType == AudioClipLoadType.DecompressOnLoad, "Codec " + field);
                float peak = 0; var data = new float[clip.samples]; clip.GetData(data, 0);
                foreach (float value in data) peak = Mathf.Max(peak, Mathf.Abs(value));
                Require(peak > .65f && peak < .75f, "Silent/clipped " + clip.name);
            }
        }
        Require(library.AlienHeavyStep.DistanceInfo.VolumeDistMax >= 65 && library.AlienBossVoice.DistanceInfo.VolumeDistMax >= 80,
            "Giant encounter sounds still use small-creature hearing range");
        Debug.Log("Alien combat audio banks, PCM import, levels and giant hearing ranges passed.");
    }
    public static string CheckDelayedStrike()
    {
        Require(EditorApplication.isPlaying && GameManager.Instance, "Initialized Play mode required");
        var system = (SoundSystem)GameManager.Instance.SoundSystem;
        Transform listener = null;
        foreach (var a in UnityEngine.Object.FindObjectsByType<AudioListener>()) if (a.enabled) { listener = a.transform; break; }
        Require(listener, "Missing listener");
        var actor = new GameObject("Delayed alien strike regression"); actor.transform.position = listener.position + Vector3.forward * 4;
        var audio = new AlienCreatureFeedback(.65f, AlienSpecies.Snake, actor.transform);
        try
        {
            system.SetListenerTransform(listener);
            audio.Observe(AlienAction.Chase, 0, 0, 100, actor.transform.position);
            audio.Observe(AlienAction.Windup, .2f, 1, 100, actor.transform.position);
            audio.Observe(AlienAction.Strike, .4f, 1, 100, actor.transform.position);
            bool heard = false;
            foreach (var e in system.m_SoundEmitterActiveList)
                if (e.Active && e.Definition == MoonkovAudio.Library.AlienStrike)
                    foreach (var source in e.ActiveSoundGameObjects)
                        heard |= source.m_AudioSource && source.m_AudioSource.transform.parent == actor.transform && source.m_AudioSource.isPlaying;
            Require(heard, "A live 0.4-second strike snapshot lost its attack voice");
            return "Delayed strike created a playing attack source: passed";
        }
        finally { audio.Dispose(); UnityEngine.Object.DestroyImmediate(actor); }
    }
}
