using System.Text.Json.Nodes;
using Toonel.Core.Models;

namespace Toonel.Core;

/// <summary>Downloads subscription URLs / config lists and turns them into profiles.</summary>
public sealed class SubscriptionClient
{
    private readonly HttpClient _http;

    public SubscriptionClient(HttpClient? http = null)
        => _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(20) };

    /// <summary>Fetches a URL (raw list or base64) and returns parsed profiles.</summary>
    public async Task<List<Profile>> FetchAsync(string url, CancellationToken ct = default)
    {
        var text = await _http.GetStringAsync(url, ct);
        return ProfileUrlParser.ParseMany(text);
    }

    public static string? ValidateUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "Subscription URL is empty.";
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != "http" && uri.Scheme != "https"))
            return "Subscription must be a valid http(s) URL.";
        return null;
    }
}
