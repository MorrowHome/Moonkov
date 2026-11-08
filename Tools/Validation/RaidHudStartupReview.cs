using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.MP_FPS;
using UnityEditor;
using UnityEngine;

// Exercise Unity's MonoBehaviour construction boundary, not just new RaidInteractionOutline().
public static class RaidHudStartupReview
{
    public static string Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            throw new InvalidOperationException("Use the existing idle Editor.");
        var errors = new List<string>();
        void Logged(string message, string stack, LogType type)
        {
            if (type == LogType.Exception || type == LogType.Error || type == LogType.Assert)
                errors.Add(message);
        }
        var host = new GameObject("Raid HUD constructor review") { hideFlags = HideFlags.HideAndDontSave };
        host.SetActive(false);
        Application.logMessageReceived += Logged;
        try
        {
            var hud = host.AddComponent<RaidHUD>();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            foreach (string field in new[] { "m_InteractionOutline", "m_ReturnGuidance" })
                if (typeof(RaidHUD).GetField(field, flags)?.GetValue(hud) == null)
                    errors.Add("Raid HUD construction did not initialize " + field);
            if (errors.Count != 0) throw new InvalidOperationException(string.Join("\n", errors));
            return "PASS: Unity constructs RaidHUD without native-resource exceptions or partially initialized fields.";
        }
        finally
        {
            Application.logMessageReceived -= Logged;
            UnityEngine.Object.DestroyImmediate(host);
        }
    }
}
