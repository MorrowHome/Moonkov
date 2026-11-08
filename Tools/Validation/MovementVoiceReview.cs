using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Unity.MP_FPS;
using UnityEditor;
using UnityEngine;

public static class MovementVoiceReview
{
    public static async Task<string> Run()
    {
        if (!Application.isPlaying || GameManager.Instance) throw new InvalidOperationException("Use isolated empty Play scene");
        const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        var instance = typeof(GameManager).GetProperty("Instance", BindingFlags.Static | BindingFlags.Public);
        var holder = new GameObject("Movement voice manager"); holder.SetActive(false);
        var manager = holder.AddComponent<GameManager>();
        var actor = new GameObject("Movement voice actor"); var sources = new GameObject("Movement voice pool");
        var pool = (SoundGameObjectPool)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(SoundGameObjectPool));
        var system = new SoundSystem(); var player = new MoonkovPlayerAudio();
        bool background = Application.runInBackground; Application.runInBackground = true;
        int checks = 0;
        void Check(bool value, string message) { checks++; if (!value) throw new InvalidOperationException(message); }
        SoundSystem.SoundInfo Voice(string name) => (SoundSystem.SoundInfo)typeof(MoonkovPlayerAudio).GetField(name, hidden).GetValue(player);
        AudioSource Source(SoundSystem.SoundInfo voice) => voice.Handle.Emitter.ActiveSoundGameObjects[0].m_AudioSource;
        try
        {
            typeof(SoundGameObjectPool).GetField("m_SourceHolder", hidden).SetValue(pool, sources);
            pool.SoundGameObjectList = new List<SoundGameObject>();
            for (int i = 0; i < 16; i++) pool.SoundGameObjectList.Add(pool.Create());
            for (int i = 0; i < 16; i++) pool.SoundGameObjectList[i].NextAvailable = pool.SoundGameObjectList[(i + 1) % 16];
            pool.NextSoundGameObject = pool.SoundGameObjectList[0]; pool.AvailableSoundObjectCount = 16;
            var lib = MoonkovAudio.Library; system.Init(actor.transform, 16, pool, lib.JumpEffort.UnityAudioMixer);
            instance.SetValue(null, manager); typeof(GameManager).GetField("m_SoundSystem", hidden).SetValue(manager, system);
            foreach (bool owner in new[] { true, false })
            {
                player.Stop(); system.KillAll(); player = new MoonkovPlayerAudio();
                var ghost = new PredictedPlayerGhost(); RaidHealth.Initialize(ref ghost); ghost.BreathableAir = true;
                ghost.ControllerState.CurrentRotation = Unity.Mathematics.quaternion.identity;
                player.Update(ghost, actor.transform, owner, .02f);
                Check(Voice("m_JumpVoice") == null, "First snapshot replayed jump");
                ghost.LastJumpTick = 1; player.Update(ghost, actor.transform, owner, .02f);
                var jump = Voice("m_JumpVoice");
                Check(jump?.Handle?.IsValid() == true && Source(jump).isPlaying, "Jump effort did not play");
                Check(Source(jump).volume >= (owner ? .55f : .38f) && Source(jump).spatialBlend == (owner ? .05f : 1),
                    "Jump actual source remained quiet or lost spatial ownership");
                for (int n = 0; n < 50; n++) player.Update(ghost, actor.transform, owner, .02f);
                Check(Voice("m_JumpVoice") == jump, "Duplicate snapshot restarted jump");
                ghost.LastImpactTick = 1; player.Update(ghost, actor.transform, owner, .02f);
                Check(Voice("m_JumpVoice") == null && Voice("m_PainSound")?.Handle?.IsValid() == true, "Jump masked injury");
            }
            player.Stop(); system.KillAll(); player = new MoonkovPlayerAudio();
            var state = new PredictedPlayerGhost(); RaidHealth.Initialize(ref state); state.BreathableAir = false;
            state.ControllerState.CurrentRotation = Unity.Mathematics.quaternion.identity;
            void Tick() { system.UpdateSoundSystem(false); player.Update(state, actor.transform, true, .02f); }
            Tick(); Tick(); await Task.Delay(550); Tick();
            var calm = Voice("m_BreathSound");
            Check(calm?.Handle?.IsValid() == true && calm.SoundDefinition == lib.SuitBreath, "Calm breathing did not start");
            float calmVolume = Source(calm).volume;
            state.TacticalSprintStamina = 0; state.TacticalSprintExhausted = true; Tick();
            Check(Voice("m_BreathSound") == null, "Calm breath survived exhaustion transition");
            await Task.Delay(150); Tick();
            var heavy = Voice("m_BreathSound");
            Check(heavy?.Handle?.IsValid() == true && heavy.SoundDefinition == lib.SuitBreathEffort, "Heavy breath missed immediate transition");
            Check(Source(heavy).isPlaying && Source(heavy).volume >= .84f && Source(heavy).volume > calmVolume * 2,
                "Heavy breathing actual source is still too quiet");
            Check(Source(heavy).pitch == 1 && Source(heavy).spatialBlend == 0, "Panting changed voice pitch or lost close routing");
            for (int n = 0; n < 100; n++) Tick();
            Check(Voice("m_BreathSound") == heavy, "Snapshots restarted panting");
            state.BreathableAir = true; state.TacticalSprintStamina = 100; state.TacticalSprintExhausted = false; Tick();
            Check(Voice("m_BreathSound") == null, "Recovery retained panting");
            state.CurrentHealth = 0; Tick(); await Task.Delay(400); system.UpdateSoundSystem(false);
            Check(pool.AvailableSoundObjectCount == 16, "Movement voice pool leaked");
            foreach (var bank in new[] { lib.SuitBreathEffort, lib.JumpEffort, lib.RegolithFootsteps, lib.MetalFootsteps, lib.HardFootsteps })
            {
                AudioClip previous = null;
                for (int n = 0; n < bank.Clips.Count + 1; n++)
                {
                    var info = MoonkovAudio.PlayWeaponLayer(bank, Vector3.zero, 1, 0);
                    Check(info?.Handle?.IsValid() == true, bank.name + " allocation");
                    var source = Source(info);
                    Check(source.isPlaying && source.clip != previous, bank.name + " repeated adjacent clip"); previous = source.clip;
                    var timer = System.Diagnostics.Stopwatch.StartNew();
                    while (info.Handle.IsValid() && timer.Elapsed.TotalSeconds < source.clip.length + 1.5)
                    { await Task.Delay(25); system.UpdateSoundSystem(false); }
                    Check(!info.Handle.IsValid() && pool.AvailableSoundObjectCount == 16, bank.name + " incomplete playback/recycle");
                }
            }
            return $"PASS {checks} actual movement voice gains, transitions, ownership, priority, clip rotation and full playback checks";
        }
        finally
        {
            player.Stop(); system.KillAll(); instance.SetValue(null, null); Application.runInBackground = background;
            UnityEngine.Object.DestroyImmediate(holder); UnityEngine.Object.DestroyImmediate(actor); UnityEngine.Object.DestroyImmediate(sources);
        }
    }
}
