using System.Diagnostics;

namespace Toonel.Core;

/// <summary>TCP connect latency probe (a real "delay through the core" needs a running engine).</summary>
public static class LatencyTester
{
    public static async Task<int> TcpPingAsync(string host, int port, int timeoutMs = 4000)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            using var client = new System.Net.Sockets.TcpClient();
            var connectTask = client.ConnectAsync(host, port);
            var done = await Task.WhenAny(connectTask, Task.Delay(timeoutMs));
            if (done != connectTask || !client.Connected) return -1;
            return (int)sw.ElapsedMilliseconds;
        }
        catch
        {
            return -1;
        }
    }
}
