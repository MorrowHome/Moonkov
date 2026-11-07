using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.MP_FPS;
using UnityEditor;

/// <summary>Runs the real input collector in isolated client worlds, without a transport or Play mode.</summary>
public static class InputTickReview
{
    public static object Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Run in the existing idle Editor.");
        const BindingFlags statics = BindingFlags.Static | BindingFlags.NonPublic;
        var settingsField = typeof(GameSettings).GetField("<Instance>k__BackingField", statics);
        var hudField = typeof(RaidHUD).GetField("s_Active", statics);
        var previousSettings = settingsField.GetValue(null);
        var previousHud = hudField.GetValue(null);
        var failures = new List<string>();
        int checks = 0;
        try
        {
            settingsField.SetValue(null, FormatterServices.GetUninitializedObject(typeof(GameSettings)));
            hudField.SetValue(null, null);
            foreach (uint delay in new uint[] { 0, 1, 32, 62 })
            {
                using var world = new World("Input tick regression", WorldFlags.GameClient);
                var em = world.EntityManager;
                var timeEntity = em.CreateEntity(typeof(NetworkTime));
                var inputEntity = em.CreateEntity(typeof(ClientMovementInput));
                em.AddBuffer<ClientCommandInput>(inputEntity);
                var sender = world.GetOrCreateSystemManaged<ClientInputSenderSystem>();
                var time = new NetworkTime { ServerTick = new NetworkTick(100),
                    InterpolationTick = new NetworkTick(60), EffectiveInputLatencyTicks = delay };
                var movement = new PlayerInput { MoveInput = new float2(1, 0) };
                em.SetComponentData(timeEntity, time);
                em.SetComponentData(inputEntity, new ClientMovementInput { PlayerInput = movement });
                sender.Update();
                CheckCommand("first frame", time, 1, false);

                movement.MoveInput = new float2(0, 1);
                movement.SetFlag(PlayerInput.InputFlag.Shoot, true);
                em.SetComponentData(inputEntity, new ClientMovementInput { PlayerInput = movement });
                sender.Update();
                CheckCommand("same tick", time, 0, true);

                time.ServerTick.Increment();
                em.SetComponentData(timeEntity, time);
                movement.SetFlag(PlayerInput.InputFlag.Shoot, false);
                em.SetComponentData(inputEntity, new ClientMovementInput { PlayerInput = movement });
                sender.Update();
                CheckCommand("next tick preserves buffered press", time, 0, true);

                time.ServerTick.Increment();
                em.SetComponentData(timeEntity, time);
                sender.Update();
                CheckCommand("press consumed once", time, 0, false);

                time.EffectiveInputLatencyTicks += 5;
                em.SetComponentData(timeEntity, time);
                sender.Update();
                CheckCommand("latency grows without prediction advancing", time, 0, false);

                time.EffectiveInputLatencyTicks = 0;
                time.ServerTick = new NetworkTick(300);
                em.SetComponentData(timeEntity, time);
                sender.Update();
                CheckCommand("latency recovered", time, 0, false);

                time.IsOffFrame = true;
                em.SetComponentData(timeEntity, time);
                sender.Update();
                CheckCommand("host off frame", time, 0, false);

                int beforeInvalid = em.GetBuffer<ClientCommandInput>(inputEntity).Length;
                em.SetComponentData(timeEntity, default(NetworkTime));
                sender.Update();
                Require(em.GetBuffer<ClientCommandInput>(inputEntity).Length == beforeInvalid,
                    "invalid clock writes no input, delay=" + delay);

                void CheckCommand(string stage, NetworkTime clock, float moveX, bool shoot)
                {
                    var buffer = em.GetBuffer<ClientCommandInput>(inputEntity);
                    bool found = buffer.GetDataAtTick(clock.InputTargetTick, out var command);
                    string context = stage + ", delay=" + delay;
                    Require(found && command.Tick == clock.InputTargetTick, context + ": wrong input tick");
                    Require(found && command.PlayerInput.MoveInput.x == moveX && command.PlayerInput.Shoot == shoot,
                        context + ": input changed");
                    Require(found && command.ClientInterpolationTick == clock.InterpolationTick,
                        context + ": interpolation tick changed");
                }
            }
        }
        finally
        {
            settingsField.SetValue(null, previousSettings);
            hudField.SetValue(null, previousHud);
        }
        return new { passed = failures.Count == 0, checks, failures = failures.ToArray(),
            scope = "Real ClientInputSenderSystem; no transport, server connection, Play mode or player build." };

        void Require(bool condition, string message) { checks++; if (!condition) failures.Add(message); }
    }
}
