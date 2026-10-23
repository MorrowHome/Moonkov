using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Unity.Entities;
using Unity.Mathematics;
using Unity.MP_FPS;
using Unity.MP_FPS.Client;
using Unity.MP_FPS.DollSinger;
using Unity.MP_FPS.Inventory;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

public static class MeleeBodyFacingChecks
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }

    [MenuItem("Tools/Moonkov/Check Melee Body Facing")]
    public static void Run()
    {
        Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Run outside Play mode.");
        var input = new PlayerInput { LookYawPitchDegrees = new float2(100, 0) };
        input.SetFlag(PlayerInput.InputFlag.ThirdPerson, true);
        var rotation = typeof(FirstPersonController).GetMethod("CalculateMovementFromInput", BindingFlags.Static | BindingFlags.NonPublic);
        object[] args = { new FirstPersonController.ControllerState { CurrentRotation = quaternion.identity },
            default(FirstPersonController.ControllerConsts), default(FirstPersonController.ControllerConsts.StateConsts), input, true, .016f, true };
        rotation.Invoke(null, args);
        var facing = (FirstPersonController.ControllerState)args[0];
        Require(math.dot(math.forward(facing.CurrentRotation), math.forward(quaternion.RotateY(math.radians(100)))) > .999f, "Third-person melee guard does not face the view.");
        args[0] = new FirstPersonController.ControllerState { CurrentRotation = quaternion.identity }; args[6] = false;
        rotation.Invoke(null, args);
        Require(math.dot(math.forward(((FirstPersonController.ControllerState)args[0]).CurrentRotation), new float3(0, 0, 1)) > .999f, "Ordinary idle camera orbit now turns the character.");

        Debug.Log("Melee guard follows view while ordinary idle orbit preserves body facing.");
    }
}
