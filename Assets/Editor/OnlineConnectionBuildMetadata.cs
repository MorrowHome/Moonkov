using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using Unity.MP_FPS.Online;

// Applies to regular Build Profiles as well as the project's build menu.
public sealed class OnlineConnectionBuildMetadata : IPreprocessBuildWithReport, IPostprocessBuildWithReport
{
    public int callbackOrder => 100;
    public void OnPreprocessBuild(BuildReport report) => ValidatePlugin(report.summary.platform);

    internal static void ValidatePlugin(BuildTarget target)
    {
        bool mac = target == BuildTarget.StandaloneOSX;
        if (!mac && target != BuildTarget.StandaloneWindows64) return;
        string plugin = mac ? "Assets/Plugins/EmbeddedIce/macOS/libmoon_ice.dylib"
            : "Assets/Plugins/EmbeddedIce/Windows/x86_64/moon_ice.dll";
        if (!File.Exists(plugin))
            throw new BuildFailedException($"Missing embedded P2P plugin: {plugin}. Restore Git LFS content before building.");
        using var file = new BinaryReader(File.OpenRead(plugin));
        bool valid = file.BaseStream.Length >= 64 &&
            (mac ? file.ReadUInt32() == 0xbebafeca : file.ReadUInt16() == 0x5a4d);
        if (!valid)
            throw new BuildFailedException($"Embedded P2P plugin is not a valid {(mac ? "universal macOS library" : "Windows DLL")}. Restore its Git LFS content before building.");
        if (!(AssetImporter.GetAtPath(plugin) is PluginImporter importer) || !importer.GetCompatibleWithPlatform(target))
            throw new BuildFailedException($"Embedded P2P plugin is excluded from {target}. Restore its plugin import settings before building.");
    }
    public void OnPostprocessBuild(BuildReport report)
    {
        if (report.summary.platform != BuildTarget.StandaloneWindows64 && report.summary.platform != BuildTarget.StandaloneOSX) return;
        string directory = Path.GetDirectoryName(Path.GetFullPath(report.summary.outputPath));
        File.WriteAllText(Path.Combine(directory, "moon-network-protocol.txt"), OnlineNetworkProtocol.Version.ToString());
    }
}
