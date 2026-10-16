using System.IO;
using System.IO.Pipes;
using System.Threading;

namespace Toonel.App;

/// <summary>
/// Ensures only one Toonel instance runs at a time.
/// A named mutex claims ownership; a second launch detects it, pings the first
/// instance over a named pipe (so it can bring its window to the front), and exits.
/// </summary>
public sealed class SingleInstanceGuard : IDisposable
{
    public const string MutexName = "Toonel-SingleInstance-Mutex";
    public const string PipeName = "Toonel-SingleInstance-Pipe";

    private Mutex? _mutex;
    private CancellationTokenSource? _pipeCts;
    private Task? _pipeTask;
    private Action? _onActivate;

    /// <summary>
    /// True if this process owns the single-instance mutex (i.e. it is the first launch).
    /// When false, the second instance pings the first and should exit immediately.
    /// </summary>
    public bool TryAcquireFirst(Action onActivate)
    {
        _onActivate = onActivate;
        _mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        if (createdNew)
        {
            StartPipeServer();
            return true;
        }

        // Not the first instance: notify the running one, then release.
        try { PingFirstInstance(); } catch { /* best effort */ }
        _mutex.Dispose();
        _mutex = null;
        return false;
    }

    private static void PingFirstInstance()
    {
        using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
        client.Connect(2000);
        using var writer = new StreamWriter(client) { AutoFlush = true };
        writer.WriteLine("activate");
    }

    private void StartPipeServer()
    {
        _pipeCts = new CancellationTokenSource();
        _pipeTask = Task.Run(async () =>
        {
            while (!_pipeCts.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream(
                        PipeName, PipeDirection.In, maxNumberOfServerInstances: 1,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    await server.WaitForConnectionAsync(_pipeCts.Token);
                    using var reader = new StreamReader(server);
                    var line = await reader.ReadLineAsync(_pipeCts.Token);
                    if (line == "activate")
                        _onActivate?.Invoke();
                }
                catch (OperationCanceledException) { break; }
                catch { /* keep serving */ }
            }
        }, _pipeCts.Token);
    }

    public void Dispose()
    {
        _pipeCts?.Cancel();
        try { _pipeTask?.Wait(TimeSpan.FromSeconds(1)); } catch { /* best effort */ }
        _pipeCts?.Dispose();
        try { _mutex?.ReleaseMutex(); } catch { /* not owned */ }
        _mutex?.Dispose();
        _mutex = null;
    }
}
