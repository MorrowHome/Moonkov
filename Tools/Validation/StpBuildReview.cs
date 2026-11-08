using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class StpBuildReview
{
    public static object Build(string label)
    {
        if (BuildPipeline.isBuildingPlayer || EditorApplication.isCompiling || EditorApplication.isPlaying)
            throw new InvalidOperationException("Use the existing idle Editor.");
        string directory = "Library/BuildValidation/WarningCleanup/stp-" + label;
        Directory.CreateDirectory(directory);
        var messages = new List<string>();
        void Log(string message, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Warning)
                messages.Add(type + ": " + message);
        }
        Application.logMessageReceived += Log;
        try
        {
            var manifest = BuildPipeline.BuildAssetBundles(directory, new[] { new AssetBundleBuild {
                assetBundleName = "stp", assetNames = new[] { "StpSetup", "StpPreTaa", "StpTaa" }
                    .Select(n => "Packages/com.unity.render-pipelines.core/Runtime/STP/" + n + ".compute").ToArray()
            } }, BuildAssetBundleOptions.UncompressedAssetBundle, BuildTarget.StandaloneWindows64);
            File.WriteAllLines(directory + "/messages.txt", messages);
            return new { success = manifest != null, messages = messages.ToArray(), directory };
        }
        finally { Application.logMessageReceived -= Log; }
    }
}
