using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Unity.MP_FPS;
using UnityEngine;

// Use CoughVoiceReview.Begin/Restore. One isolated Editor, no networking clients.
public static class AirlockButtonAudioReview
{
    public static async Task<string> Run()
    {
        if (!Application.isPlaying || GameManager.Instance) throw new InvalidOperationException("Use isolated empty Play scene");
        const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        var instance = typeof(GameManager).GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
        var holder = new GameObject("Button review manager"); holder.SetActive(false);
        var manager = holder.AddComponent<GameManager>();
        var actor = new GameObject("Button review actor"); actor.AddComponent<AudioListener>();
        var sources = new GameObject("Button review pool");
        var buttonObject = new GameObject("Reviewed airlock button"); buttonObject.AddComponent<BoxCollider>();
        var button = buttonObject.AddComponent<AirlockButton>();
        var pool = (SoundGameObjectPool)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(SoundGameObjectPool));
        var system = new SoundSystem(); int checks = 0;
        bool background = Application.runInBackground; Application.runInBackground = true;
        void Check(bool value, string message) { checks++; if (!value) throw new InvalidOperationException(message); }
        bool Has(SoundDef bank) => system.m_SoundEmitterActiveList.Any(e => e.Active && e.Definition == bank);
        try
        {
            typeof(SoundGameObjectPool).GetField("m_SourceHolder", hidden).SetValue(pool, sources);
            pool.SoundGameObjectList = new List<SoundGameObject>();
            for (int i = 0; i < 8; i++) pool.SoundGameObjectList.Add(pool.Create());
            for (int i = 0; i < 8; i++) pool.SoundGameObjectList[i].NextAvailable = pool.SoundGameObjectList[(i + 1) % 8];
            pool.NextSoundGameObject = pool.SoundGameObjectList[0]; pool.AvailableSoundObjectCount = 8;
            var lib = MoonkovAudio.Library; system.Init(actor.transform, 8, pool, lib.Confirm.UnityAudioMixer);
            instance.SetValue(null, manager); typeof(GameManager).GetField("m_SoundSystem", hidden).SetValue(manager, system);
            Check(lib.AirlockButtonPress != lib.Confirm && lib.AirlockButtonPress.Clips[0] != lib.Confirm.Clips[0], "Button reuses extraction success");
            Check(lib.AirlockButtonPress.Clips[0].name == "HL2AirlockButton" && lib.AirlockButtonPress.Clips[0].length < .15f,
                "Button must be a short original button cue");
            button.ReceiveFeedback(AirlockFeedback.Pending);
            Check(!system.m_SoundEmitterActiveList.Any(e => e.Active), "Pending request played a sound");
            button.ReceiveFeedback(AirlockFeedback.Accepted);
            Check(Has(lib.AirlockButtonPress) && !Has(lib.Confirm), "Accepted airlock press did not use its own bank");
            Check(system.m_SoundEmitterActiveList.Count(e => e.Active) == 1, "Press stacked feedback sounds");
            var emitter = system.m_SoundEmitterActiveList.Single(e => e.Active);
            var source = emitter.ActiveSoundGameObjects[0].m_AudioSource;
            Check(source.isPlaying && source.volume > 0 && !source.loop && source.spatialBlend == 0 &&
                source.outputAudioMixerGroup.name == "SFX", "Actual source playback/routing is wrong");
            // Allow the Editor's initial DSP buffer to start, then tick the real pool
            // as the game does. A wall-clock delay alone is not an audio clock.
            var deadline = System.Diagnostics.Stopwatch.StartNew();
            while (Has(lib.AirlockButtonPress) && deadline.ElapsedMilliseconds < 1500)
            {
                await Task.Delay(25); system.UpdateSoundSystem(false);
            }
            Check(!Has(lib.AirlockButtonPress) && pool.AvailableSoundObjectCount == 8,
                "Short cue did not finish and recycle; playing=" + source.isPlaying + ", time=" + source.time);
            button.ReceiveFeedback(AirlockFeedback.OutOfRange);
            Check(Has(lib.Error) && !Has(lib.AirlockButtonPress) && !Has(lib.Confirm), "Rejected press must retain error feedback");
            system.KillAll(); MoonkovAudio.Confirm();
            Check(Has(lib.Confirm) && !Has(lib.AirlockButtonPress) && lib.Confirm.Clips[0].name == "Confirm", "Extraction success changed");
            system.KillAll();
            Check(pool.AvailableSoundObjectCount == 8, "Feedback leaked sources");
            return "PASS " + checks + " actual airlock pending/accepted/rejected, separate extraction, routing and recycling checks";
        }
        finally
        {
            system.KillAll(); instance.SetValue(null, null); Application.runInBackground = background;
            UnityEngine.Object.DestroyImmediate(buttonObject); UnityEngine.Object.DestroyImmediate(holder);
            UnityEngine.Object.DestroyImmediate(actor); UnityEngine.Object.DestroyImmediate(sources);
        }
    }
}
