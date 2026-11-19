using CodeBlue.Viewer.Input;

namespace CodeBlue.Viewer.Link;

/// <summary>
/// The viewer's view of the firmware. Everything above this interface works in
/// HID terms (usage IDs, button states, deltas); the implementation turns those
/// into whatever its wire protocol needs, so protocol details stay in this folder.
/// </summary>
internal interface IKvmLink : IDisposable
{
    /// <summary>Human-readable protocol (and firmware, if known) for the status bar.</summary>
    string ProtocolName { get; }

    /// <summary>Raised (on a background thread) if the connection fails.</summary>
    event Action<Exception>? Faulted;

    /// <summary>Raised (on a background thread) when the target's status changes. Never raised by links that can't tell.</summary>
    event Action<TargetStatus>? TargetStatusChanged;

    /// <summary>Raised (on a background thread) with messages worth logging, such as errors the board reports.</summary>
    event Action<string>? Diagnostic;

    /// <summary>The target's last reported status, or null if this link can't tell.</summary>
    TargetStatus? TargetStatus { get; }

    /// <summary>The board's LED brightness when connected, or null if the board doesn't report it.</summary>
    int? LedBrightness { get; }

    /// <summary>
    /// Called from the UI thread several times a second. Protocols with a
    /// heartbeat send it from here, so a frozen viewer stops heartbeating and
    /// the board releases everything.
    /// </summary>
    void Heartbeat();

    void KeyDown(byte usage);
    void KeyUp(byte usage);

    /// <summary>True if the board offers an absolute pointer (MouseMoveAbsolute).</summary>
    bool SupportsAbsolute { get; }

    /// <summary>True if the board senses the target's USB power, so TargetStatus.VbusPresent means something.</summary>
    bool SupportsVbusSense { get; }

    /// <summary>True if the board's pin settings can be read and changed (firmware 0.3.0 and later).</summary>
    bool SupportsSettings { get; }

    /// <summary>Reads a board setting. Fails with TimeoutException if the board doesn't answer.</summary>
    Task<SettingReply> GetSettingAsync(BoardSetting setting);

    /// <summary>
    /// Changes a board setting; the board applies it at once and saves it. The
    /// reply carries the result and the value now in effect, whether or not it changed.
    /// </summary>
    Task<SettingReply> SetSettingAsync(BoardSetting setting, short value);

    /// <summary>
    /// Whether buttons and the wheel go to the absolute pointer (true) or the
    /// relative mouse (false). Ignored by links without an absolute pointer.
    /// </summary>
    bool AbsoluteMouse { get; set; }

    void MouseMoveRelative(int dx, int dy);

    /// <summary>Moves the absolute pointer; 0 to 32767 on each axis spans the target's screen.</summary>
    void MouseMoveAbsolute(int x, int y);

    void MouseButton(TargetMouseButton button, bool down);

    /// <summary>Wheel movement in detents (positive = away from the user).</summary>
    void MouseWheel(int detents);

    /// <summary>Releases every key and button on the target. Called whenever capture ends.</summary>
    void ReleaseAll();

    /// <summary>Sets the status LED to a full-scale colour; the link applies the brightness.</summary>
    void SetStatusLed(byte r, byte g, byte b);

    /// <summary>Sets the LED brightness (0-100). With <paramref name="persist"/>, the board saves it, if it can.</summary>
    void SetLedBrightness(int percent, bool persist);
}

/// <summary>What the board reports about the target (TARGET_STATUS in docs/development/protocol.md).</summary>
internal readonly record struct TargetStatus(byte Flags, byte KeyboardLeds)
{
    public bool Mounted => (Flags & 0x01) != 0;
    public bool Suspended => (Flags & 0x02) != 0;
    public bool KeyboardBoot => (Flags & 0x04) != 0;
    public bool MouseBoot => (Flags & 0x08) != 0;
    public bool RemoteWakeup => (Flags & 0x10) != 0;
    public bool VbusPresent => (Flags & 0x20) != 0;
    /// <summary>The target hasn't collected an absolute pointer report for a while (firmware 0.3.0 and later).</summary>
    public bool AbsoluteUnread => (Flags & 0x40) != 0;

    public bool NumLock => (KeyboardLeds & 0x01) != 0;
    public bool CapsLock => (KeyboardLeds & 0x02) != 0;
    public bool ScrollLock => (KeyboardLeds & 0x04) != 0;
}

/// <summary>Board settings (SETTING_GET and SETTING_SET in docs/development/protocol.md).</summary>
internal enum BoardSetting : byte
{
    LedGpio = 1,
    VbusGpio = 2,
}

internal enum SettingResult : byte
{
    Ok = 0,
    UnknownSetting = 1,
    BadPin = 2,
    Conflict = 3,
    ApplyFailed = 4,
}

/// <summary>The board's answer to a setting request: the value in effect, and how the request went.</summary>
internal readonly record struct SettingReply(BoardSetting Setting, short Value, SettingResult Result)
{
    /// <summary>A pin setting's value for "no pin".</summary>
    public const short None = -1;

    /// <summary>Sent to return a setting to the firmware's build default.</summary>
    public const short Default = -2;
}

/// <summary>A board that answered, but not in a way we can use (no firmware, wrong version).</summary>
internal sealed class KvmLinkException(string message) : IOException(message);

/// <summary>Opens links to the board.</summary>
internal static class KvmLinks
{
    /// <summary>
    /// Opens the CodeBlue protocol on a serial port. Blocks for up to two
    /// seconds while the board answers; throws if no compatible board does.
    /// </summary>
    public static IKvmLink Open(string portName) => BinaryLink.Open(portName);
}
