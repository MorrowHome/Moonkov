using System;
using System.Linq;
using System.Reflection;
using Unity.Mathematics;
using Unity.MP_FPS;
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
                Check(source.isPlaying && source.volume >= .3f && source.outputAudioMixerGroup.name == "Footsteps" &&
                    Mathf.Abs(source.spatialBlend - (owned ? .08f : 1f)) < .001f, "Playback/routing failed: " + scenario);
                audio.OnFootstep(ghost, player.transform, owned);
                Check(system.m_SoundEmitterActiveList.Count(e => e.Definition == library.RegolithFootsteps) == count,
                    "A duplicate/landing event played a second footstep.");
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
