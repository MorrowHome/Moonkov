using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

// Schedule from Pipeline so its request timeout cannot become a build error.
public static class WindowsWarningBuild
{
    public static string Start(string outputDirectory)
    {
        if (BuildPipeline.isBuildingPlayer || EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Use the existing idle Editor.");
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64 ||
            EditorUserBuildSettings.standaloneBuildSubtarget != StandaloneBuildSubtarget.Player)
            throw new InvalidOperationException("Select the Windows x64 client before building.");
        if (Directory.Exists(outputDirectory)) throw new InvalidOperationException("Choose a new build directory.");
        Directory.CreateDirectory(outputDirectory);
        string reportPath = Path.Combine(outputDirectory, "build-report.json");
        var scenes = (string[])typeof(MoonkovPlayerBuildChecks).GetMethod("GetBuildScenes", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, null);
        var options = new BuildPlayerOptions { scenes = scenes, target = BuildTarget.StandaloneWindows64,
            subtarget = (int)StandaloneBuildSubtarget.Player, options = BuildOptions.None,
            locationPathName = Path.Combine(outputDirectory, "Moonkov.exe") };
        EditorApplication.delayCall += () =>
        {
            var record = new Record { startedUtc = DateTime.UtcNow.ToString("O"), output = options.locationPathName, scenes = scenes };
            try
            {
                var report = BuildPipeline.BuildPlayer(options);
                record.result = report.summary.result.ToString();
                record.errors = report.summary.totalErrors;
                record.warnings = report.summary.totalWarnings;
                record.seconds = report.summary.totalTime.TotalSeconds;
                record.bytes = report.summary.totalSize;
                record.messages = report.steps.SelectMany(step => step.messages)
                    .Where(message => message.type == LogType.Warning || message.type == LogType.Error || message.type == LogType.Exception)
                    .Select(message => message.type + ": " + message.content).ToArray();
            }
            catch (Exception exception)
            {
                record.result = "Exception"; record.errors = 1;
                record.messages = new[] { exception.ToString() };
            }
            finally
            {
                record.finishedUtc = DateTime.UtcNow.ToString("O");
                File.WriteAllText(reportPath, JsonUtility.ToJson(record, true));
            }
        };
        return "Scheduled Windows player build; inspect " + reportPath;
    }

    [Serializable] private sealed class Record
    {
        public string startedUtc, finishedUtc, result, output;
        public string[] scenes, messages;
        public int errors, warnings;
        public double seconds;
        public ulong bytes;
    }
}
