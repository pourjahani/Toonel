using System.Text.Json;
using Toonel.Core.Models;

namespace Toonel.Core;

/// <summary>
/// Helpers for the Configs page: validates user-edited JSON and resolves what an engine
/// should actually run. The rule everywhere: the user's edited config wins when it is
/// explicitly enabled AND valid JSON — anything else falls back to the generated config,
/// so a broken manual edit can never silently kill connectivity.
/// </summary>
public static class ConfigManual
{
    /// <summary>Parses JSON; returns null when valid, otherwise the first error message.</summary>
    public static string? ValidateJson(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "empty";
        try
        {
            using var doc = JsonDocument.Parse(text);
            return doc.RootElement.ValueKind is JsonValueKind.Object or JsonValueKind.Array
                ? null
                : "expected a JSON object or array";
        }
        catch (JsonException ex)
        {
            return ex.Message;
        }
    }

    /// <summary>Xray config the engine runs: manual override when enabled/valid, else generated.</summary>
    public static (string Text, bool UsedManual) ResolveXray(
        AppSettings s, ConnectionMode mode, Action<string>? note = null) =>
        Resolve(s.ManualConfig.UseXray, s.ManualConfig.Xray,
            () => XrayConfigBuilder.Build(s, mode), "Xray", note);

    /// <summary>sing-box config the engine runs: manual override when enabled/valid, else generated.</summary>
    public static (string Text, bool UsedManual) ResolveSingBox(
        AppSettings s, ConnectionMode mode, string singBoxLogPath, bool withTun, Action<string>? note = null) =>
        Resolve(s.ManualConfig.UseSingBox, s.ManualConfig.SingBox,
            () => SingBoxConfigBuilder.Build(s, mode, singBoxLogPath, withTun), "sing-box", note);

    private static (string Text, bool UsedManual) Resolve(
        bool use, string manualText, Func<string> generate, string engine, Action<string>? note)
    {
        if (use && !string.IsNullOrWhiteSpace(manualText))
        {
            if (ValidateJson(manualText) is null)
            {
                note?.Invoke($"Using manual {engine} config (user-edited).");
                return (manualText, true);
            }
            note?.Invoke($"Manual {engine} config is not valid JSON — using the generated config instead.");
        }
        return (generate(), false);
    }
}
