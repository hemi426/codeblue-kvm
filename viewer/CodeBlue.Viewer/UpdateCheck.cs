using System.Text.Json;

namespace CodeBlue.Viewer;

/// <summary>
/// The optional update check. It reads latest.json from the docs site, which
/// the site's build writes from the newest viewer and firmware releases.
/// Notify only: it never downloads or installs anything, and any failure just
/// means no news.
/// </summary>
internal static class UpdateCheck
{
    /// <summary>The docs site's home page, which has both installers.</summary>
    internal const string SiteUrl = "https://hemi426.github.io/codeblue-kvm/";
    private const string LatestUrl = SiteUrl + "latest.json";

    /// <summary>How often to ask, at most.</summary>
    internal static readonly TimeSpan Interval = TimeSpan.FromDays(1);

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    internal sealed record Latest(string? Viewer, string? Firmware);

    /// <summary>The newest releases, or null if the site couldn't be reached or read.</summary>
    public static async Task<Latest?> FetchAsync()
    {
        try
        {
            await using var stream = await Http.GetStreamAsync(LatestUrl);
            using var doc = await JsonDocument.ParseAsync(stream);
            return new Latest(Text(doc.RootElement, "viewer"), Text(doc.RootElement, "firmware"));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException
                                       or IOException or InvalidOperationException)
        {
            return null;
        }
    }

    private static string? Text(JsonElement root, string name) =>
        root.ValueKind == JsonValueKind.Object &&
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>The latest version if it's newer than the current one, else null.</summary>
    public static Version? Newer(string? latest, Version? current)
    {
        if (current is null || !Version.TryParse(latest, out var l)) return null;
        return Normalize(l) > Normalize(current) ? l : null;
    }

    // "0.5" and "0.5.0" are the same release; Version alone says the first is older.
    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));
}
