using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using Unity.MP_FPS.Online;

// Applies to regular Build Profiles as well as the project's build menu.
public sealed class OnlineConnectionBuildMetadata : IPreprocessBuildWithReport, IPostprocessBuildWithReport
{
    public int callbackOrder => 100;
    public void OnPreprocessBuild(BuildReport report)
    {
        if (report.summary.platform != BuildTarget.StandaloneWindows64) return;
        const string plugin = "Assets/Plugins/EmbeddedIce/Windows/x86_64/moon_ice.dll";
        if (!File.Exists(plugin))
            throw new BuildFailedException("Missing embedded P2P plugin. Restore Git LFS files or run Tools/Networking/EmbeddedIce/Build.ps1 -InstallPlugin.");
        using var file = new BinaryReader(File.OpenRead(plugin));
        if (file.BaseStream.Length < 64 || file.ReadUInt16() != 0x5a4d)
            throw new BuildFailedException("Embedded P2P plugin is not a Windows DLL. Restore its Git LFS content before building.");
    }
    public void OnPostprocessBuild(BuildReport report)
    {
        if (report.summary.platform != BuildTarget.StandaloneWindows64) return;
        string directory = Path.GetDirectoryName(Path.GetFullPath(report.summary.outputPath));
        File.WriteAllText(Path.Combine(directory, "moon-network-protocol.txt"), OnlineNetworkProtocol.Version.ToString());
    }
}
