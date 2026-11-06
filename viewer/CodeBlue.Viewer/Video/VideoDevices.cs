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
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name FROM Win32_PnPEntity WHERE PNPClass = 'Camera' OR PNPClass = 'Image'");
            foreach (var obj in searcher.Get())
            {
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
        if (Size != Auto) opts.Add($":dshow-size={Size}");
        if (Fps != Auto) opts.Add($":dshow-fps={Fps}");
        return [.. opts];
    }
}
