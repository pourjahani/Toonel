using System.Collections.ObjectModel;
using System.Text;

namespace Toonel.Core;

/// <summary>Thread-safe rolling log with UI-friendly snapshots, one buffer per source engine.</summary>
public sealed class LogBuffer
{
    private readonly object _lock = new();
    private readonly Dictionary<string, Queue<string>> _buffers = new();
    private readonly int _capacity;

    public event Action<string, string>? LineAdded; // (source, formattedLine)

    public LogBuffer(int capacity = 2000) => _capacity = capacity;

    public void Add(string source, string line)
    {
        var formatted = $"[{DateTime.Now:HH:mm:ss}] {line}";
        lock (_lock)
        {
            if (!_buffers.TryGetValue(source, out var q))
                _buffers[source] = q = new Queue<string>();
            q.Enqueue(formatted);
            while (q.Count > _capacity) q.Dequeue();
        }
        LineAdded?.Invoke(source, formatted);
    }

    public List<string> Snapshot(string source)
    {
        lock (_lock)
        {
            return _buffers.TryGetValue(source, out var q) ? q.ToList() : new List<string>();
        }
    }

    public void Clear(string? source = null)
    {
        lock (_lock)
        {
            if (source == null) _buffers.Clear();
            else if (_buffers.ContainsKey(source)) _buffers[source].Clear();
        }
    }

    public static string Format(string source, string level, string message)
    {
        var sb = new StringBuilder(source).Append("  [").Append(level).Append("]  ").Append(message);
        return sb.ToString();
    }
}
