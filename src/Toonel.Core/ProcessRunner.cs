using System.Diagnostics;

namespace Toonel.Core;

/// <summary>Runs an external engine binary, streaming merged stdout/stderr lines.</summary>
public sealed class ProcessRunner
{
    private Process? _proc;
    private readonly object _gate = new();

    public string Name { get; }
    public string ExePath { get; }
    public event Action<string>? OutputLine;

    public ProcessRunner(string name, string exePath)
    {
        Name = name;
        ExePath = exePath;
    }

    public bool IsRunning
    {
        get { lock (_gate) return _proc is { HasExited: false }; }
    }

    public int? ExitCode
    {
        get { lock (_gate) return _proc?.HasExited == true ? _proc.ExitCode : null; }
    }

    public void Start(string args)
    {
        if (IsRunning) return;
        if (!File.Exists(ExePath))
            throw new FileNotFoundException($"{Name} binary not found: {ExePath}");

        var psi = new ProcessStartInfo
        {
            FileName = ExePath,
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(ExePath) ?? ".",
        };

        var proc = new Process { StartInfo = psi };
        proc.OutputDataReceived += (_, e) => { if (e.Data is not null) OutputLine?.Invoke(e.Data); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data is not null) OutputLine?.Invoke(e.Data); };
        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        lock (_gate) _proc = proc;
    }

    /// <summary>Stops the process gracefully, then force-kills after the grace period.</summary>
    public async Task StopAsync(int graceMs = 3000)
    {
        Process? proc;
        lock (_gate) { proc = _proc; _proc = null; }
        if (proc is null) return;

        try
        {
            if (!proc.HasExited)
            {
                proc.Kill(entireProcessTree: false);
                await proc.WaitForExitAsync().WaitAsync(TimeSpan.FromMilliseconds(graceMs));
            }
        }
        catch (TimeoutException)
        {
            try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { /* already gone */ }
        }
        catch
        {
            // process already exited
        }
        finally
        {
            proc.Dispose();
        }
    }
}
