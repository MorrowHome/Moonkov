using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Text.Json;

public sealed class OnlineInstanceOptions
{
    public string ProjectRoot { get; set; } = "";
    public string Executable { get; set; } = "Build/MoonServer/MoonServer.exe";
    public string Address { get; set; } = "moon.morrowhome.site";
    public string ServerName { get; set; } = "moon.morrowhome.site";
    public int Port { get; set; } = 7979;
}

// No shell commands, no extra Editor, no client processes. The process lease is
// checked against path + start time before signalling a process after a restart.
public sealed class OnlineInstanceWorker(OnlineLobbies lobbies, OnlineInstanceOptions options, ILogger<OnlineInstanceWorker> log) : BackgroundService
{
    private sealed record Lease(int Pid, long Started, string Instance, string Executable, string StopFile);
    private Process? process;
    private Lease? lease;
    private DateTime? stopping;
    private string LeaseFile => Path.Combine(options.ProjectRoot, "LocalData/online-instance-process.json");
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try { Tick(DateTime.UtcNow); }
            catch (Exception ex) { log.LogError("Online instance scheduling: {Kind}: {Message}", ex.GetType().Name, ex.Message); }
            try { await Task.Delay(1000, ct); } catch (OperationCanceledException) { break; }
        }
        // A backend restart does not kill a raid or its receipts; the lease is adopted next launch.
        process?.Dispose();
    }
    private void Tick(DateTime now)
    {
        var active = lobbies.Current();
        if (active == null)
        {
            // Never replace a still-running server left by an incomplete shutdown.
            if (File.Exists(LeaseFile))
            {
                var previous = JsonSerializer.Deserialize<Lease>(File.ReadAllText(LeaseFile));
                using var orphan = previous == null ? null : Owned(previous);
                if (orphan != null) return;
                File.Delete(LeaseFile);
            }
            active = lobbies.Allocate(now);
            if (active == null) return;
        }
        if (lease == null)
        {
            if (File.Exists(LeaseFile))
            {
                var saved = JsonSerializer.Deserialize<Lease>(File.ReadAllText(LeaseFile));
                if (saved?.Instance == active.Id) { process = Owned(saved); lease = saved; }
                if (process == null)
                {
                    lobbies.Finish(active.Id, "对局服务器已退出；请刷新仓库，未完成回执保留待恢复。");
                    File.Delete(LeaseFile); lease = null; return;
                }
            }
            else
            {
                try { Launch(active); }
                catch (Exception ex)
                { lobbies.Finish(active.Id, "无法启动对局：" + ex.Message); log.LogWarning("Instance launch refused: {Message}", ex.Message); return; }
            }
        }
        if (process == null) return;
        process.Refresh();
        if (process.HasExited)
        {
            lobbies.Finish(active.Id, stopping == null ? "对局进程意外退出，请刷新仓库；未完成回执保留待恢复。" : null);
            process.Dispose(); process = null; lease = null; stopping = null;
            File.Delete(LeaseFile); return;
        }
        if (stopping == null && lobbies.ShouldStop(now))
        {
            File.WriteAllText(lease!.StopFile, "stop"); stopping = now;
        }
        // Do not forcibly kill a server flushing inventory receipts. Block reuse and report it.
        if (stopping != null && now - stopping > TimeSpan.FromSeconds(45))
            log.LogWarning("Instance {Instance} is still flushing receipts; its slot remains reserved", active.Id);
    }
    private static Process? Owned(Lease saved)
    {
        try
        {
            var candidate = Process.GetProcessById(saved.Pid);
            if (!candidate.HasExited && candidate.StartTime.ToUniversalTime().Ticks == saved.Started &&
                string.Equals(candidate.MainModule?.FileName, saved.Executable, StringComparison.OrdinalIgnoreCase)) return candidate;
            candidate.Dispose();
        }
        catch (ArgumentException) { }
        return null;
    }
    private void Launch(OnlineLobbies.Allocation active)
    {
        var root = Path.GetFullPath(options.ProjectRoot);
        var exe = Path.GetFullPath(Path.Combine(root, options.Executable));
        if (!File.Exists(exe)) throw new InvalidOperationException("请先构建当前版本独立服务器。");
        string protocolFile = Path.Combine(Path.GetDirectoryName(exe)!, "online-instance-protocol.txt");
        if (!File.Exists(protocolFile) || File.ReadAllText(protocolFile).Trim() != "1")
            throw new InvalidOperationException("独立服尚未更新到每局实例协议，请重新构建服务器。");
        if (!File.Exists(Path.Combine(root, "moon-server.local.json"))) throw new InvalidOperationException("缺少服务器后端配置。");
        if (IPGlobalProperties.GetIPGlobalProperties().GetActiveUdpListeners().Any(e => e.Port == options.Port))
            throw new InvalidOperationException($"UDP {options.Port} 被旧服务器占用，请停止旧常驻服后重试。");
        string folder = Path.Combine(root, "LocalData/instances", active.Id);
        Directory.CreateDirectory(folder);
        string stopFile = Path.Combine(folder, "shutdown.stop");
        var start = new ProcessStartInfo(exe)
        {
            WorkingDirectory = Path.GetDirectoryName(exe)!, UseShellExecute = false, CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        foreach (var arg in new[] { "-batchmode", "-nographics", "-logFile", Path.Combine(folder, "server.log"), "port=" + options.Port }) start.ArgumentList.Add(arg);
        start.Environment["MOON_SERVER_CONFIG"] = Path.Combine(root, "moon-server.local.json");
        start.Environment["MOON_SERVER_SHUTDOWN_FILE"] = stopFile;
        start.Environment["MOON_INSTANCE_ID"] = active.Id;
        start.Environment["MOON_INSTANCE_MODE"] = active.Mode;
        process = Process.Start(start) ?? throw new InvalidOperationException("启动进程失败。");
        lease = new Lease(process.Id, process.StartTime.ToUniversalTime().Ticks, active.Id, exe, stopFile);
        File.WriteAllText(LeaseFile, JsonSerializer.Serialize(lease));
        log.LogInformation("Started fresh {Mode} instance {Instance}, UDP {Port}, {Count} seats", active.Mode, active.Id, options.Port, active.Seats.Count);
    }
}
