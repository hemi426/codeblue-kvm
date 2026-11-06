using CodeBlue.Viewer.Input;

namespace CodeBlue.Viewer.Link;

/// <summary>
/// The viewer's view of the firmware. Everything above this interface works in
/// HID terms (usage IDs, button states, deltas); each implementation turns those
/// into whatever its wire protocol needs. The UI never knows which protocol is
/// in use, so dropping the legacy one later touches only this folder.
/// </summary>
internal interface IKvmLink : IDisposable
{
    /// <summary>Human-readable protocol name for the status bar.</summary>
    string ProtocolName { get; }

    /// <summary>Raised (on a background thread) if the connection fails.</summary>
    event Action<Exception>? Faulted;

    void KeyDown(byte usage);
    void KeyUp(byte usage);

    void MouseMoveRelative(int dx, int dy);
    void MouseButton(TargetMouseButton button, bool down);

    /// <summary>Wheel movement in detents (positive = away from the user).</summary>
    void MouseWheel(int detents);

    /// <summary>Releases every key and button on the target. Called whenever capture ends.</summary>
    void ReleaseAll();

    void SetStatusLed(byte r, byte g, byte b);
}

/// <summary>Available link protocols, as offered in the UI.</summary>
internal static class KvmLinks
{
    public const string VideoOnly = "None (video only)";

#if CODEBLUE_LEGACY_PROTOCOL
    public const string LegacyText = "Legacy text (deprecated)";
#endif

    public static IReadOnlyList<string> Available { get; } =
    [
#if CODEBLUE_LEGACY_PROTOCOL
        LegacyText,
#endif
        VideoOnly,
    ];

    /// <summary>Opens the named link on a serial port, or returns null for video-only.</summary>
    public static IKvmLink? Open(string protocol, string portName) => protocol switch
    {
#if CODEBLUE_LEGACY_PROTOCOL
        LegacyText => new LegacyTextLink(new SerialTransport(portName, LegacyTextLink.BaudRate)),
#endif
        _ => null,
    };
}
