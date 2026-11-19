using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodeBlue.Viewer;

/// <summary>Remembers the last-used choices in %APPDATA%\CodeBlue\viewer.json.</summary>
internal sealed class ViewerSettings
{
    public string? SerialPort { get; set; }
    public string? VideoDevice { get; set; }
    public bool VideoOnly { get; set; }

    /// <summary>Read from older settings files only: the protocol choice that VideoOnly replaced.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Protocol { get; set; }

    public string Format { get; set; } = "MJPG";
    public string Size { get; set; } = "1920x1080";
    public string Fps { get; set; } = "30";
    public int CachingMs { get; set; } = 40;
    public int LedBrightnessPercent { get; set; } = 5;
    public bool NativeScale { get; set; }
    public string MouseMode { get; set; } = "Auto";

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CodeBlue", "viewer.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static ViewerSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var settings = JsonSerializer.Deserialize<ViewerSettings>(File.ReadAllText(FilePath)) ?? new();
                if (settings.Protocol is not null)
                {
                    settings.VideoOnly = settings.Protocol.StartsWith("None", StringComparison.Ordinal);
                    settings.Protocol = null;
                }
                return settings;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Unreadable settings are not worth failing over; start fresh.
        }
        return new();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not fatal.
        }
    }
}
