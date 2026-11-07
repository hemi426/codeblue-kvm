using System.Text.Json;

namespace CodeBlue.Viewer;

/// <summary>Remembers the last-used choices in %APPDATA%\CodeBlue\viewer.json.</summary>
internal sealed class ViewerSettings
{
    public string? SerialPort { get; set; }
    public string? VideoDevice { get; set; }
    public string? Protocol { get; set; }
    public string Format { get; set; } = "MJPG";
    public string Size { get; set; } = "1920x1080";
    public string Fps { get; set; } = "30";
    public int CachingMs { get; set; } = 50;
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
                return JsonSerializer.Deserialize<ViewerSettings>(File.ReadAllText(FilePath)) ?? new();
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
