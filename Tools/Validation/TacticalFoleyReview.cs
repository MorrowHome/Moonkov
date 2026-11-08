using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Unity.MP_FPS;
using UnityEditor;
using UnityEngine;

// Run in the one existing Editor, between CoughVoiceReview.Begin and Restore.
public static class TacticalFoleyReview
{
    public static Task<string> Banks() => Run(true);
    public static Task<string> WorldAndShield() => Run(false);

    private static async Task<string> Run(bool banks)
    {
        if (!Application.isPlaying || GameManager.Instance) throw new InvalidOperationException("Use isolated empty Play scene");
        const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        int checks = 0;
        void Check(bool value, string message) { checks++; if (!value) throw new InvalidOperationException(message); }
        var instance = typeof(GameManager).GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
        var holder = new GameObject("Tactical audio review manager"); holder.SetActive(false);
        var manager = holder.AddComponent<GameManager>();
        var listener = new GameObject("Tactical audio review listener"); listener.AddComponent<AudioListener>();
        var sources = new GameObject("Tactical audio review pool");
        var pool = (SoundGameObjectPool)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(SoundGameObjectPool));
        var system = new SoundSystem(); GameObject worldObject = null, rigHolder = null; EnergyShieldPresentation shield = null;
        TacticalItemsFeedback feedback = null;
        bool background = Application.runInBackground; Application.runInBackground = true;
        try
        {
            typeof(SoundGameObjectPool).GetField("m_SourceHolder", hidden).SetValue(pool, sources);
            pool.SoundGameObjectList = new List<SoundGameObject>();
            for (int i = 0; i < 24; i++) pool.SoundGameObjectList.Add(pool.Create());
            for (int i = 0; i < 24; i++) pool.SoundGameObjectList[i].NextAvailable = pool.SoundGameObjectList[(i + 1) % 24];
            pool.NextSoundGameObject = pool.SoundGameObjectList[0]; pool.AvailableSoundObjectCount = 24;
            var lib = MoonkovAudio.Library; system.Init(listener.transform, 24, pool, lib.TacticalGas.UnityAudioMixer);
            instance.SetValue(null, manager); typeof(GameManager).GetField("m_SoundSystem", hidden).SetValue(manager, system);
            int Count(SoundDef bank) => system.m_SoundEmitterActiveList.Count(e => e.Active && e.Definition == bank);
            int Last() => system.m_SoundEmitterActiveList.Select(e => e.SeqId).DefaultIfEmpty(-1).Max();
            async Task Settle(int milliseconds = 160) { await Task.Delay(milliseconds); system.UpdateSoundSystem(false); }
            if (banks)
            {
                foreach (var bank in new[] { lib.TacticalDeploy, lib.TacticalTrapArm, lib.TacticalSupplyDeploy, lib.TacticalWireRelease,
                    lib.TacticalFlash, lib.TacticalExplosion, lib.TacticalGas, lib.TacticalFireStart, lib.TacticalFireLoop,
                    lib.TacticalSmokeLoop, lib.TacticalChokeLoop, lib.TacticalPoisonLoop, lib.TacticalAlarm,
                    lib.ShieldImpact, lib.ShieldBreak, lib.ShieldRestore, lib.ShieldRaise, lib.ShieldLower })
                {
                    AudioClip previous = null;
                    for (int n = 0; n < bank.Clips.Count; n++)
                    {
                        var info = MoonkovAudio.Play(bank, Vector3.zero);
                        Check(info?.Handle?.IsValid() == true, bank.name + " allocation");
                        var source = info.Handle.Emitter.ActiveSoundGameObjects[0].m_AudioSource;
                        bool loop = bank.RepeatInfo.LoopCount == 0;
                        Check(source.isPlaying && source.volume > 0 && source.spatialBlend == 1 && source.loop == loop &&
                            source.outputAudioMixerGroup.name == "SFX", bank.name + " native playback/routing");
                        if (previous) Check(source.clip != previous, bank.name + " variant repeated");
                        previous = source.clip;
                        var timer = System.Diagnostics.Stopwatch.StartNew();
                        if (loop)
                        {
                            while (timer.Elapsed.TotalSeconds < source.clip.length + .15f) await Settle(50);
                            Check(info.Handle.IsValid() && source.isPlaying, bank.name + " did not continue across the loop boundary");
                            system.Stop(info, .08f); await Settle();
                        }
                        else
                            while (info.Handle.IsValid() && timer.Elapsed.TotalSeconds < source.clip.length + 1.5f) await Settle(25);
                        Check(!info.Handle.IsValid() && pool.AvailableSoundObjectCount == 24, bank.name + " recycle");
                    }
                }
                return "PASS " + checks + " native world/shield bank playback, loop wrapping, variants and recycling checks";
            }

            worldObject = new GameObject("Tactical world audio review");
            feedback = worldObject.AddComponent<TacticalItemsFeedback>(); feedback.enabled = false;
            uint id = 1, sequence = 1, epoch = 1;
            void Tick() => typeof(TacticalItemsFeedback).GetMethod("Update", hidden).Invoke(feedback, null);
            void Present(TacticalWorldStateRpc state) => feedback.Present(state);
            void Clear()
            { feedback.Present(new TacticalWorldStateRpc { Epoch = ++epoch, Phase = TacticalWorldPhase.Removed }); }
            foreach (var pair in new[] {
                (TacticalItemKind.Fire, TacticalWorldPhase.Fire, 30f, lib.TacticalFireStart, lib.TacticalFireLoop),
                (TacticalItemKind.Smoke, TacticalWorldPhase.Smoke, 20f, lib.TacticalGas, lib.TacticalSmokeLoop),
                (TacticalItemKind.Choke, TacticalWorldPhase.Choke, 20f, lib.TacticalGas, lib.TacticalChokeLoop),
                (TacticalItemKind.PoisonBomb, TacticalWorldPhase.Poison, 15f, (SoundDef)null, lib.TacticalPoisonLoop),
                (TacticalItemKind.AlertTrap, TacticalWorldPhase.Alarm, 8f, (SoundDef)null, lib.TacticalAlarm) })
            {
                Clear(); await Settle(); system.KillAll();
                var state = new TacticalWorldStateRpc { Epoch = epoch, Id = id++, Sequence = sequence++, Kind = pair.Item1,
                    Phase = pair.Item2, Remaining = pair.Item3, Radius = 3, Position = Vector3.right * 2 };
                Present(state);
                Check(Count(pair.Item5) == 1, pair.Item2 + " actual world bed missing");
                if (pair.Item4) Check(Count(pair.Item4) == 1, pair.Item2 + " opening missing");
                int before = Last(); for (int n = 0; n < 40; n++) { Present(state); Tick(); }
                Check(Last() == before, pair.Item2 + " repeated snapshot restarted audio");
                state.Sequence = sequence++; state.Remaining -= .2f; Present(state);
                Check(Last() == before, pair.Item2 + " new pose restarted audio");
                listener.transform.position = Vector3.right * 200; Tick(); await Settle();
                Check(Count(pair.Item5) == 0, pair.Item2 + " distant bed retained a source");
                listener.transform.position = Vector3.zero; await Settle(550); Tick();
                Check(Count(pair.Item5) == 1, pair.Item2 + " audible bed did not resume");
                state.Sequence = sequence++; state.Phase = TacticalWorldPhase.Removed; Present(state); await Settle();
                Check(pool.AvailableSoundObjectCount == 24, pair.Item2 + " removal retained sounds");
            }

            Clear(); await Settle(); system.KillAll();
            var fire = new TacticalWorldStateRpc { Epoch = epoch, Id = id++, Sequence = sequence++, Kind = TacticalItemKind.Fire,
                Phase = TacticalWorldPhase.Fire, Remaining = 8, Radius = 3 };
            Present(fire);
            Check(Count(lib.TacticalFireLoop) == 1 && Count(lib.TacticalFireStart) == 0, "Late arrival replayed ignition");
            var old = fire; fire.Sequence = sequence++; fire.Phase = TacticalWorldPhase.Removed;
            Present(fire); await Settle(); Present(old);
            Check(pool.AvailableSoundObjectCount == 24, "Stale world state resurrected sound");
            fire.Id = id++; fire.Sequence = sequence++; fire.Phase = TacticalWorldPhase.Fire; fire.Remaining = .01f;
            Present(fire); await Settle(550); Tick(); await Settle();
            Check(pool.AvailableSoundObjectCount == 24, "Expired world effect retained loop");

            foreach (var item in new[] {
                (TacticalItemKind.AlertTrap, TacticalWorldPhase.Armed, 180f, lib.TacticalTrapArm),
                (TacticalItemKind.WireTrap, TacticalWorldPhase.Wire, 45f, lib.TacticalWireRelease),
                (TacticalItemKind.AmmoBox, TacticalWorldPhase.Supply, 120f, lib.TacticalSupplyDeploy),
                (TacticalItemKind.Flash, TacticalWorldPhase.Explosion, .35f, lib.TacticalFlash),
                (TacticalItemKind.Frag, TacticalWorldPhase.Explosion, .35f, lib.TacticalExplosion) })
            {
                Clear(); await Settle(); system.KillAll();
                var state = new TacticalWorldStateRpc { Epoch = epoch, Id = id++, Sequence = sequence++, Kind = item.Item1,
                    Phase = item.Item2, Remaining = item.Item3, Radius = 2, Energy = 400 };
                Present(state); Check(Count(item.Item4) == 1, item.Item1 + " received wrong cue");
                int before = Last(); for (int n = 0; n < 40; n++) Present(state);
                Check(Last() == before, item.Item1 + " repeated event");
                if (state.Phase == TacticalWorldPhase.Explosion)
                {
                    var sound = system.m_SoundEmitterActiveList.Single(e => e.Active && e.Definition == item.Item4);
                    var source = sound.ActiveSoundGameObjects[0].m_AudioSource;
                    state.Sequence = sequence++; state.Phase = TacticalWorldPhase.Removed; Present(state); await Settle(500);
                    Check(source.isPlaying && source.time > .2f, "Short-lived explosion visual cut off the acoustic tail");
                }
            }
            Clear(); await Settle(); system.KillAll();
            for (int n = 0; n < 8; n++)
                Present(new TacticalWorldStateRpc { Epoch = epoch, Id = id++, Sequence = sequence++, Kind = TacticalItemKind.Fire,
                    Phase = TacticalWorldPhase.Fire, Remaining = 8, Radius = 1 });
            Check(Count(lib.TacticalFireLoop) == 4, "Fire loop voice budget exceeded");
            Clear(); await Settle(); Check(pool.AvailableSoundObjectCount == 24, "Round change retained loops");
            feedback.enabled = true;
            Present(new TacticalWorldStateRpc { Epoch = epoch, Id = id++, Sequence = sequence++, Kind = TacticalItemKind.Smoke,
                Phase = TacticalWorldPhase.Smoke, Remaining = 8, Radius = 1 });
            feedback.enabled = false; await Settle();
            Check(pool.AvailableSoundObjectCount == 24, "Disabled presentation retained loops");
            UnityEngine.Object.DestroyImmediate(worldObject); worldObject = null; feedback = null;

            rigHolder = new GameObject("Shield audio rig review"); rigHolder.SetActive(false);
            var root = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/DollSinger/Prefabs/DollSingerNetworkPlayer.prefab"), rigHolder.transform);
            foreach (var b in root.GetComponentsInChildren<MonoBehaviour>(true)) b.enabled = false;
            foreach (var a in root.GetComponentsInChildren<AudioListener>(true)) a.enabled = false;
            foreach (var c in root.GetComponentsInChildren<Camera>(true)) c.enabled = false;
            var animator = root.GetComponentInChildren<Animator>(true);
            for (var node = animator.transform; node; node = node.parent) node.gameObject.SetActive(true);
            animator.Rebind(); animator.Update(0);
            shield = root.GetComponent<EnergyShieldPresentation>() ?? root.AddComponent<EnergyShieldPresentation>();
            shield.enabled = false; shield.ConfigureHardwareBudget(true);
            foreach (bool owner in new[] { true, false })
            {
                shield.ResetPresentation(); system.KillAll();
                var ghost = new PredictedPlayerGhost(); RaidHealth.Initialize(ref ghost); ghost.HaloEquipped = true;
                void Shield() => shield.Present(ghost, owner, owner, null);
                Shield(); Check(!system.m_SoundEmitterActiveList.Any(e => e.Active), "Initial shield snapshot was audible");
                ghost.HaloDefending = true; Shield(); Check(Count(lib.ShieldRaise) == 1, "Shield raise missing");
                var source = system.m_SoundEmitterActiveList.Single(e => e.Active && e.Definition == lib.ShieldRaise).ActiveSoundGameObjects[0].m_AudioSource;
                Check(source.isPlaying && Mathf.Abs(source.spatialBlend - (owner ? .05f : 1)) < .001f, "Shield ownership routing");
                int before = Last(); for (int n = 0; n < 40; n++) Shield();
                Check(Last() == before, "Shield repeated state retriggered");
                ghost.ShieldHitSequence++; ghost.ShieldHitStrength = .5f; Shield();
                Check(Count(lib.ShieldImpact) == 1, "Raise suppressed immediate shield impact");
                ghost.ShieldBreakSequence++; ghost.HaloDefending = false; Shield();
                Check(Count(lib.ShieldBreak) == 1 && Count(lib.ShieldLower) == 0, "Break must supersede lowering");
                await Settle(); ghost.ShieldRestoreSequence++; Shield(); Check(Count(lib.ShieldRestore) == 1, "Shield recovery missing");
                ghost.HaloDefending = true; Shield(); await Settle(); ghost.HaloDefending = false; Shield();
                Check(Count(lib.ShieldLower) == 1, "Shield lower missing");
                await Settle(); ghost.HaloDefending = true; Shield();
                before = Last(); ghost.CurrentHealth = 0; Shield(); Check(Last() == before, "Death played a lower cue");
                shield.ResetPresentation(); await Settle(); Check(pool.AvailableSoundObjectCount == 24, "Shield reset retained handles");
            }
            return "PASS " + checks + " actual tactical world transitions, late arrival, stale packets, voice caps, range, teardown and production shield checks";
        }
        finally
        {
            if (worldObject) UnityEngine.Object.DestroyImmediate(worldObject);
            if (shield) shield.DisposePresentation(); if (rigHolder) UnityEngine.Object.DestroyImmediate(rigHolder);
            system.KillAll(); instance.SetValue(null, null); Application.runInBackground = background;
            UnityEngine.Object.DestroyImmediate(holder); UnityEngine.Object.DestroyImmediate(listener); UnityEngine.Object.DestroyImmediate(sources);
        }
    }
}
