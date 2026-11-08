using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Unity.MP_FPS;
using Unity.MP_FPS.DollSinger;
using UnityEditor;
using UnityEngine;

// CoughVoiceReview.Begin/Restore isolate this check in the existing Editor.
public static class InjectorPresentationReview
{
    public static async Task<string> Run()
    {
        if (!Application.isPlaying || GameManager.Instance) throw new InvalidOperationException("Use isolated empty Play scene");
        const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        int checks = 0;
        void Check(bool value, string message) { checks++; if (!value) throw new InvalidOperationException(message); }
        var lib = MoonkovAudio.Library;
        var clip = lib.InjectorPress.Clips.Single();
        var samples = new float[clip.samples * clip.channels]; clip.GetData(samples, 0);
        double rms = Math.Sqrt(samples.Average(s => (double)s * s));
        double effectiveRms = rms * Math.Pow(10, lib.InjectorPress.PitchAndVolumeInfo.VolumeMin / 20);
        Check(clip.name == "InjectorHuntShot" && clip.length >= 1.37f && clip.length <= 1.39f,
            "Injection must include entry, pressure and withdrawal");
        Check(effectiveRms >= .035, "Injection body is too quiet at its configured minimum gain: " + effectiveRms);
        var instance = typeof(GameManager).GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
        var holder = new GameObject("Injector review manager"); holder.SetActive(false);
        var manager = holder.AddComponent<GameManager>();
        var listener = new GameObject("Injector review listener"); listener.AddComponent<AudioListener>();
        var sources = new GameObject("Injector review sources");
        var pool = (SoundGameObjectPool)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(SoundGameObjectPool));
        var system = new SoundSystem(); GameObject rigHolder = null; RaidActionPresentation action = null;
        bool background = Application.runInBackground; Application.runInBackground = true;
        try
        {
            typeof(SoundGameObjectPool).GetField("m_SourceHolder", hidden).SetValue(pool, sources);
            pool.SoundGameObjectList = new List<SoundGameObject>();
            for (int i = 0; i < 16; i++) pool.SoundGameObjectList.Add(pool.Create());
            for (int i = 0; i < 16; i++) pool.SoundGameObjectList[i].NextAvailable = pool.SoundGameObjectList[(i + 1) % 16];
            pool.NextSoundGameObject = pool.SoundGameObjectList[0]; pool.AvailableSoundObjectCount = 16;
            system.Init(listener.transform, 16, pool, lib.InjectorPress.UnityAudioMixer);
            instance.SetValue(null, manager); typeof(GameManager).GetField("m_SoundSystem", hidden).SetValue(manager, system);
            uint sequence = 1;
            foreach (var rig in new[] { "DollSinger", "HatsuneMiku", "Shian" })
            {
                rigHolder = new GameObject("Injector rig review"); rigHolder.SetActive(false);
                var root = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/{rig}/Prefabs/{rig}NetworkPlayer.prefab"), rigHolder.transform);
                foreach (var b in root.GetComponentsInChildren<MonoBehaviour>(true)) b.enabled = false;
                foreach (var a in root.GetComponentsInChildren<AudioListener>(true)) a.enabled = false;
                foreach (var c in root.GetComponentsInChildren<Camera>(true)) c.enabled = false;
                var animator = root.GetComponentInChildren<Animator>(true);
                for (var node = animator.transform; node; node = node.parent) node.gameObject.SetActive(true);
                animator.Rebind(); animator.Update(0);
                action = animator.gameObject.AddComponent<RaidActionPresentation>(); action.enabled = false;
                action.Configure(animator, null, null, rig);
                var motion = JsonUtility.FromJson<DollSingerActionAnimation.Library>(Resources.Load<TextAsset>("Moonkov/Actions/" + rig).text).motions.First(m => m.action == "Inject");
                foreach (var code in new[] { "medkit", "vitality_shot", "regeneration_shot" })
                {
                    action.ResetPresentation(); system.KillAll();
                    var ghost = new PredictedPlayerGhost { CurrentHealth = 100, HandAction = (byte)HandAction.Inject,
                        HandActionSequence = sequence++, HandActionItem = code, HandActionDuration = 3, HandActionRemaining = 3 };
                    void Tick()
                    {
                        typeof(RaidActionPresentation).GetMethod("Update", hidden).Invoke(action, null);
                        typeof(RaidActionPresentation).GetMethod("LateUpdate", hidden).Invoke(action, null);
                    }
                    void Present(float phase)
                    {
                        ghost.HandActionRemaining = 3 * (1 - phase / motion.contactOut);
                        action.Present(ghost, true, true, false);
                        typeof(RaidActionPresentation).GetField("blend", hidden).SetValue(action, 1f);
                        Tick();
                    }
                    bool Playing(SoundDef bank) => system.m_SoundEmitterActiveList.Any(e => e.Active && e.Definition == bank);
                    Present(0); Present(motion.openMarker + .01f);
                    Check(Playing(lib.InjectorCap), rig + " " + code + " cap did not reach sound pool");
                    Present(motion.contactIn - .06f);
                    Check(!Playing(lib.InjectorPress), rig + " injection fired before its authored marker");
                    Present(motion.contactIn + .01f);
                    Check(Playing(lib.InjectorPress), rig + " " + code + " push missing through actual presentation");
                    var emitter = system.m_SoundEmitterActiveList.Single(e => e.Active && e.Definition == lib.InjectorPress);
                    var source = emitter.ActiveSoundGameObjects[0].m_AudioSource;
                    Check(source.isPlaying && source.volume * rms >= .035 && source.spatialBlend == 0, rig + " injection source routing or level mismatch");
                    int id = emitter.SeqId; for (int i = 0; i < 20; i++) Present(motion.contactIn + .01f);
                    Check(system.m_SoundEmitterActiveList.Count(e => e.Active && e.Definition == lib.InjectorPress) == 1 && emitter.SeqId == id, "Duplicate push");
                    await Task.Delay(600);
                    Check(source.isPlaying && source.time > .3f, "Injection did not advance into its pressure stroke");
                    if (code == "medkit")
                    {
                        float untilComplete = 3 * (1 - (motion.contactIn + .01f) / motion.contactOut);
                        await Task.Delay(Math.Max(1, Mathf.CeilToInt(untilComplete * 1000) - 600));
                        Present(motion.contactOut);
                        ghost.HandAction = (byte)HandAction.None; ghost.HandActionRemaining = 0;
                        action.Present(ghost, true, true, false);
                        await Task.Delay(150); Tick();
                        Check(source.isPlaying && source.time > 1f, "Successful use cut off the original withdrawal sound");
                        await Task.Delay(Math.Max(1, Mathf.CeilToInt((clip.length - untilComplete) * 1000)));
                        system.UpdateSoundSystem(false);
                        Check(!Playing(lib.InjectorPress) && pool.AvailableSoundObjectCount == 16, "Completed injection did not finish and recycle");
                    }
                    else
                    {
                        ghost.CurrentHealth = 0; action.Present(ghost, true, true, false);
                        await Task.Delay(100); system.UpdateSoundSystem(false);
                        Check(!Playing(lib.InjectorPress) && pool.AvailableSoundObjectCount == 16, "Death retained injection audio");
                    }
                }
                action.ResetPresentation(); UnityEngine.Object.DestroyImmediate(rigHolder); rigHolder = null; action = null;
            }
            return $"PASS {checks} real injection presentation checks; minimum configured RMS {20 * Math.Log10(effectiveRms):F1} dBFS; {clip.length:F2} s stroke";
        }
        finally
        {
            if (action) action.ResetPresentation(); system.KillAll(); instance.SetValue(null, null); Application.runInBackground = background;
            if (rigHolder) UnityEngine.Object.DestroyImmediate(rigHolder);
            UnityEngine.Object.DestroyImmediate(holder); UnityEngine.Object.DestroyImmediate(listener); UnityEngine.Object.DestroyImmediate(sources);
        }
    }
}
