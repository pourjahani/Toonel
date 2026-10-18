using System.Diagnostics;

namespace Toonel.Core;

/// <summary>
/// "Flush" action: undo everything Toonel can have changed on the machine —
/// stops/kill all engine processes, reverts the WinINET system proxy, removes the
/// per-user terminal proxy variables (setx), and flushes the DNS cache.
/// Nothing is deleted (profiles/cores/settings stay); this is a network reset.
/// </summary>
public static class NetworkFlusher
{
    public sealed record FlushReport(
        bool EnginesStopped,
        int StaleEnginesKilled,
        bool ProxyReverted,
        bool TerminalProxyCleared,
        bool DnsFlushed,
        IReadOnlyList<string> Notes)
    {
        public bool AllGood =>
            EnginesStopped && ProxyReverted && TerminalProxyCleared && DnsFlushed;
    }

    public static FlushReport Flush(LogBuffer? log = null, int ourHttpPort = 0)
    {
        var notes = new List<string>();

        // 1. Kill any engine processes (leftovers from a crashed run or still running here).
        int killed = KillLeftovers(log);

        // 2. Revert the WinINET system proxy if it points at us.
        bool proxyOk;
        try
        {
            if (ourHttpPort > 0) SystemProxyManager.DisableIfOurs(ourHttpPort);
            else SystemProxyManager.Disable();
            proxyOk = true;
            log?.Add(EngineManager.SourceApp, "System proxy reset.");
        }
        catch (Exception ex)
        {
            proxyOk = false;
            notes.Add($"proxy: {ex.Message}");
            log?.Add(EngineManager.SourceApp, $"proxy reset failed: {ex.Message}");
        }

        // 3. Remove the per-user terminal proxy variables set by "Apply proxy to future terminals".
        bool envOk = ClearTerminalProxyEnv(log);

        // 4. Flush the DNS cache.
        bool dnsOk;
        try
        {
            dnsOk = RunIpconfigFlushDns(out var dnsOut);
            if (!dnsOk) notes.Add($"dns: {dnsOut}");
            log?.Add(EngineManager.SourceApp, dnsOk ? "DNS cache flushed." : $"DNS flush failed: {dnsOut}");
        }
        catch (Exception ex)
        {
            dnsOk = false;
            notes.Add($"dns: {ex.Message}");
        }

        return new FlushReport(true, killed, proxyOk, envOk, dnsOk, notes);
    }

    private static int KillLeftovers(LogBuffer? log)
    {
        int killed = 0;
        var coresDir = Path.Combine(AppContext.BaseDirectory, "cores");
        foreach (var name in new[] { "xray", "sing-box", "psiphon-core" })
        {
            foreach (var proc in Process.GetProcessesByName(name))
            {
                try
                {
                    var path = proc.MainModule?.FileName;
                    if (EngineManager.IsOurBinary(path, coresDir))
                    {
                        proc.Kill(entireProcessTree: true);
                        killed++;
                    }
                }
                catch { /* not ours or access denied */ }
                finally { proc.Dispose(); }
            }
        }
        if (killed > 0)
            log?.Add(EngineManager.SourceApp, $"Killed {killed} leftover engine process(es).");
        return killed;
    }

    private static bool ClearTerminalProxyEnv(LogBuffer? log)
    {
        try
        {
            // setx VAR "" clears the value but leaves the name; reg delete removes both,
            // matching what "Apply proxy to future terminals" created.
            RunCmd("reg delete \"HKCU\\Environment\" /v HTTP_PROXY /f >nul 2>&1");
            RunCmd("reg delete \"HKCU\\Environment\" /v HTTPS_PROXY /f >nul 2>&1");
            RunCmd("reg delete \"HKCU\\Environment\" /v ALL_PROXY /f >nul 2>&1");
            // Broadcast so new processes pick the change up without a re-login.
            RunCmd("setx TOONEL_REFRESH \"\" >nul 2>&1");
            log?.Add(EngineManager.SourceApp, "Terminal proxy variables removed.");
            return true;
        }
        catch (Exception ex)
        {
            log?.Add(EngineManager.SourceApp, $"terminal proxy cleanup failed: {ex.Message}");
            return false;
        }
    }

    private static bool RunIpconfigFlushDns(out string output)
    {
        try
        {
            var psi = new ProcessStartInfo("ipconfig", "/flushdns")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var p = Process.Start(psi)!;
            output = p.StandardOutput.ReadToEnd().Trim();
            p.WaitForExit(8000);
            return p.ExitCode == 0;
        }
        catch (Exception ex)
        {
            output = ex.Message;
            return false;
        }
    }

    private static void RunCmd(string args)
    {
        try
        {
            System.Diagnostics.Process.Start(new ProcessStartInfo("cmd.exe", $"/c {args}")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
            });
        }
        catch { /* ignore */ }
    }
}
