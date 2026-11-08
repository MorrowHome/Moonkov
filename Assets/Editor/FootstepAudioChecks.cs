using System;
using System.Linq;
using System.Reflection;
using Unity.Mathematics;
using Unity.MP_FPS;
using Unity.MP_FPS.DollSinger;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

public static class FootstepAudioChecks
{
    public static string Run()
    {
        if (!Application.isPlaying || GameManager.Instance)
            throw new InvalidOperationException("Use an isolated Play scene without a GameManager.");
        const BindingFlags instanceFlags = BindingFlags.Instance | BindingFlags.NonPublic;
        var managerProperty = typeof(GameManager).GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
        var soundField = typeof(GameManager).GetField("m_SoundSystem", instanceFlags);
        var holder = new GameObject("Inactive footstep audio fixture"); holder.SetActive(false);
        var manager = holder.AddComponent<GameManager>(); // Inactive: do not run gameplay bootstrap.
        var player = new GameObject("Footstep audio actor");
        var pool = new SoundGameObjectPool("Footstep audio check pool", 16);
        var library = Resources.Load<MoonkovAudioLibrary>("Moonkov/AudioLibrary");
        SoundSystem system = null; MoonkovPlayerAudio audio = null;
        int checks = 0;
        void Check(bool value, string message)
        { checks++; if (!value) throw new InvalidOperationException(message); }
        try
        {
            managerProperty.SetValue(null, manager);
            foreach (string scenario in new[] { "distance_only", "event", "between_ticks", "remote", "idle", "air", "teleport", "dead", "disabled", "landing", "sprint", "turn", "turn_jitter" })
            {
                audio?.Stop(); system?.KillAll();
                system = new SoundSystem();
                system.Init(player.transform, 16, pool, AssetDatabase.LoadAssetAtPath<AudioMixer>("Assets/Audio/Mixers/DefaultMixer.mixer"));
                soundField.SetValue(manager, system);
                audio = new MoonkovPlayerAudio();
                var ghost = new PredictedPlayerGhost(); RaidHealth.Initialize(ref ghost);
                ghost.ControllerState.CurrentRotation = quaternion.identity;
                ghost.ControllerState.MovementType = FirstPersonController.MovementType.Standing;
                ghost.ControllerState.MovementSpeed = 2;
                ghost.ControllerState.Sprinting = scenario == "sprint";
                bool owned = scenario != "remote";
                player.transform.position = Vector3.zero;
                audio.Update(ghost, player.transform, owned, .02f);
                if (scenario == "air") ghost.ControllerState.MovementType = FirstPersonController.MovementType.Falling;
                if (scenario == "dead") ghost.CurrentHealth = 0;
                if (scenario == "landing") ghost.LastLandTick = 1;
                if (scenario.StartsWith("turn")) ghost.ControllerState.CurrentRotation = quaternion.RotateY(math.radians(scenario == "turn" ? 60 : .2f));
                if (scenario != "idle" && !scenario.StartsWith("turn")) player.transform.position = Vector3.forward * (scenario == "teleport" ? 5 : .1f);
                audio.Update(ghost, player.transform, owned, .02f);
                if (scenario == "between_ticks") audio.Update(ghost, player.transform, owned, .007f);
                if (scenario == "disabled") audio.Stop();
                if (scenario == "distance_only")
                {
                    for (int i = 0; i < 8; i++)
                    { player.transform.position += Vector3.forward * .5f; audio.Update(ghost, player.transform, owned, .02f); }
                }
                else if (!scenario.StartsWith("turn")) audio.OnFootstep(ghost, player.transform, owned);
                int count = system.m_SoundEmitterActiveList.Count(e => e.Definition == library.RegolithFootsteps);
                bool audible = scenario == "event" || scenario == "between_ticks" || scenario == "remote" || scenario == "landing" || scenario == "sprint";
                Check(count == (audible ? 1 : 0), "Unexpected actual footstep emitter count for " + scenario + ": " + count);
                if (scenario != "landing")
                {
                    Check(system.m_SoundEmitterActiveList.Count(e => e.Definition == library.Cloth) == (scenario == "turn" ? 1 : 0),
                        "Clothing must only play on substantial stationary turns: " + scenario);
                    Check(!system.m_SoundEmitterActiveList.Any(e => e.Definition == library.GearMotion), "Walking/running retained repetitive equipment friction");
                }
                if (!audible) continue;
                var source = system.m_SoundEmitterActiveList.First(e => e.Definition == library.RegolithFootsteps)
                    .ActiveSoundGameObjects[0].m_AudioSource;
                Check(source.isPlaying && source.volume > 0 && source.outputAudioMixerGroup.name == "Footsteps" &&
                    Mathf.Abs(source.spatialBlend - (owned ? .08f : 1f)) < .001f, "Playback/routing failed: " + scenario);
                audio.OnFootstep(ghost, player.transform, owned);
                Check(system.m_SoundEmitterActiveList.Count(e => e.Definition == library.RegolithFootsteps) == count,
                    "A duplicate/landing event played a second footstep.");
            }

            float Measure(float speed, bool owner, bool sprint, int fps = 60, bool snapshots = false,
                DollSingerStance stance = DollSingerStance.Standing)
            {
                audio.Stop(); system.KillAll(); audio = new MoonkovPlayerAudio();
                system = new SoundSystem(); system.Init(player.transform, 16, pool, library.RegolithFootsteps.UnityAudioMixer);
                soundField.SetValue(manager, system);
                var state = new PredictedPlayerGhost(); RaidHealth.Initialize(ref state); state.BreathableAir = true;
                state.ControllerState.CurrentRotation = quaternion.identity;
                state.ControllerState.MovementType = FirstPersonController.MovementType.Standing;
                state.ControllerState.Stance = stance;
                // Deliberately identical requested speed: measured motion must determine loudness.
                state.ControllerState.MovementSpeed = 5.335f; state.ControllerState.Sprinting = sprint;
                player.transform.position = Vector3.zero; audio.Update(state, player.transform, owner, 1f / fps);
                for (int frame = 1; frame <= fps; frame++)
                {
                    float time = snapshots ? Mathf.Floor(frame * 30f / fps) / 30 : frame / (float)fps;
                    player.transform.position = Vector3.forward * (speed * time);
                    audio.Update(state, player.transform, owner, 1f / fps);
                }
                audio.OnFootstep(state, player.transform, owner);
                var expected = stance == DollSingerStance.Prone ? library.Crawl : library.RegolithFootsteps;
                var emitter = system.m_SoundEmitterActiveList.SingleOrDefault(e => e.Active && e.Definition == expected);
                if (speed == 0) { Check(emitter == null, "Stationary sprint input emitted a footstep"); return 0; }
                Check(emitter != null, "Moving character missed its footstep");
                var source = emitter.ActiveSoundGameObjects[0].m_AudioSource;
                Check(source.isPlaying && source.spatialBlend == (owner ? .08f : 1), "Speed gain lost actual playback or ownership");
                Check(!system.m_SoundEmitterActiveList.Any(e => e.Definition == library.Cloth || e.Definition == library.GearMotion),
                    "Speed-dependent steps restored running friction");
                return source.volume;
            }
            foreach (bool owner in new[] { true, false })
            foreach (bool sprint in new[] { false, true })
            {
                float previous = 0;
                foreach (float speed in new[] { .25f, .8f, 1.5f, 2f, 3.5f, 5.335f })
                {
                    float volume = Measure(speed, owner, sprint);
                    Check(volume > previous, "Actual source gain must rise with speed, independently of sprint input");
                    previous = volume;
                }
                Check(previous >= .49f, "Fast running lost the previous loud mix");
                Measure(0, owner, sprint);
            }
            foreach (int fps in new[] { 30, 60, 144 })
            {
                float moving = Measure(2, false, false, fps);
                float sparse = Measure(2, false, false, fps, true);
                Check(Mathf.Abs(moving - sparse) < moving * .25f,
                    "Render rate or sparse 30 Hz snapshots changed footstep loudness excessively");
            }
            foreach (var stance in new[] { DollSingerStance.Tiptoe, DollSingerStance.Crouching, DollSingerStance.Prone })
            {
                float speed = 2 * DollSingerStances.SpeedScale(stance);
                float slow = Measure(speed * .5f, true, false, stance: stance);
                float fast = Measure(speed, true, false, stance: stance);
                Check(fast > slow * 1.6f, "Quiet postures and crawling must also follow speed");
                Check(MoonkovPlayerAudio.FootstepVolume(speed * 10, stance) == MoonkovPlayerAudio.FootstepVolume(speed, stance),
                    "Quiet stance volume cap was lost");
            }
            float Turn(float degreesPerSecond)
            {
                audio.Stop(); system.KillAll(); audio = new MoonkovPlayerAudio();
                system = new SoundSystem(); system.Init(player.transform, 16, pool, library.Cloth.UnityAudioMixer);
                soundField.SetValue(manager, system);
                var state = new PredictedPlayerGhost(); RaidHealth.Initialize(ref state); state.BreathableAir = true;
                state.ControllerState.MovementType = FirstPersonController.MovementType.Standing;
                state.ControllerState.CurrentRotation = quaternion.identity;
                audio.Update(state, player.transform, true, 1f / 60);
                for (float yaw = degreesPerSecond / 60; yaw <= 60; yaw += degreesPerSecond / 60)
                {
                    state.ControllerState.CurrentRotation = quaternion.RotateY(math.radians(yaw));
                    audio.Update(state, player.transform, true, 1f / 60);
                }
                var emitter = system.m_SoundEmitterActiveList.Single(e => e.Active && e.Definition == library.Cloth);
                Check(!system.m_SoundEmitterActiveList.Any(e => e.Definition == library.RegolithFootsteps), "Stationary turn played walking footsteps");
                return emitter.ActiveSoundGameObjects[0].m_AudioSource.volume;
            }
            float slowTurn = Turn(30), fastTurn = Turn(180);
            Check(fastTurn > slowTurn * 3, "Turn cloth must follow angular speed");
            foreach (float invalid in new[] { 0, -1, float.NaN, float.PositiveInfinity })
            {
                Check(MoonkovPlayerAudio.FootstepVolume(invalid, DollSingerStance.Standing) == 0, "Invalid step speed was audible");
                Check(MoonkovPlayerAudio.TurnVolume(invalid) == 0, "Invalid turn speed was audible");
            }
        }
        finally
        {
            audio?.Stop(); system?.KillAll(); pool.Dispose();
            managerProperty.SetValue(null, null);
            UnityEngine.Object.DestroyImmediate(player);
            UnityEngine.Object.DestroyImmediate(holder);
        }
        return $"PASS {checks} actual pooled footstep playback checks";
    }
}
