using System.Management;

namespace CodeBlue.Viewer.Video;

/// <summary>
/// Lists video capture devices by their friendly names, which is what libVLC's
/// DirectShow module expects for :dshow-vdev. Uses WMI so we don't need a
/// DirectShow COM interop layer just to fill a dropdown.
/// </summary>
internal static class VideoDevices
{
    public static List<string> List()
    {
        var names = new List<string>();
        try
        {
            // Windows keeps records of unplugged devices too; Present = TRUE leaves those out.
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, Present FROM Win32_PnPEntity WHERE PNPClass = 'Camera' OR PNPClass = 'Image'");
            foreach (var obj in searcher.Get())
            {
                if (obj["Present"] is false) continue;
                if (obj["Name"] is string name && !names.Contains(name))
                    names.Add(name);
            }
        }
        catch (ManagementException)
        {
            // WMI unavailable; the device box stays editable, so the user can type a name.
        }
        names.Sort(StringComparer.OrdinalIgnoreCase);
        return names;
    }
}

/// <summary>
/// DirectShow capture options. "Auto" leaves a setting to libVLC and the driver.
/// The defaults favor cheap USB 2.0 dongles: MJPEG keeps 1080p at full frame
/// rate within USB 2.0 bandwidth, and a short cache keeps the picture current.
/// </summary>
internal sealed record CaptureOptions(string Device, string Format, string Size, string Fps, int CachingMs)
{
    public const string Auto = "Auto";

    public static readonly string[] Formats = [Auto, "MJPG", "YUY2", "NV12"];
    public static readonly string[] Sizes = [Auto, "1920x1080", "1600x900", "1280x1024", "1280x720", "1024x768", "800x600"];
    public static readonly string[] FrameRates = [Auto, "60", "30", "25", "15"];

    public string[] ToVlcOptions()
    {
        var opts = new List<string>
        {
            $":dshow-vdev={Device}",
            ":dshow-adev=none",
            $":live-caching={CachingMs}",
        };
        if (Format != Auto) opts.Add($":dshow-chroma={Format}");
        if (Size != Auto)
        {
            opts.Add($":dshow-size={Size}");
            // VLC's DirectShow module assumes 4:3 unless told otherwise, which
            // squeezes 16:9 capture. Capture pixels are square, so the display
            // aspect is simply width:height.
            if (AspectOf(Size) is string aspect) opts.Add($":dshow-aspect-ratio={aspect}");
        }
        if (Fps != Auto) opts.Add($":dshow-fps={Fps}");
        return [.. opts];
    }

    /// <summary>Reduces a width and height to an aspect ratio, e.g. 1920, 1080 to "16:9".</summary>
    public static string AspectOf(uint width, uint height)
    {
        uint a = width, b = height;
        while (b != 0) (a, b) = (b, a % b);
        return a == 0 ? $"{width}:{height}" : $"{width / a}:{height / a}";
    }

    private static string? AspectOf(string size)
    {
        var parts = size.Split('x');
        return parts.Length == 2 && uint.TryParse(parts[0], out uint w) && uint.TryParse(parts[1], out uint h) && w > 0 && h > 0
            ? AspectOf(w, h)
            : null;
    }
}
