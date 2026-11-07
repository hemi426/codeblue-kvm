// DEPRECATED: compiled only when CodeBlueLegacyProtocol=true (the default for now).
// Remove this file and its #if blocks in IKvmLink.cs once the binary protocol ships.

using System.Globalization;
using CodeBlue.Viewer.Input;

namespace CodeBlue.Viewer.Link;

/// <summary>
/// Speaks the line-based protocol of the original Arduino firmware
/// (alpharesearch/KeyboardAndMouseControl): one command letter, three integers,
/// newline. See docs/PROTOCOL.md.
///
/// Keys are sent with press/release ("p"/"e") using the Arduino Keyboard
/// library's raw-key escape: values 0x88 and up are passed through as
/// HID usage (value - 0x88), and 0x80-0x87 are the eight modifiers. That gives
/// us physical, layout-independent keys over the old protocol, limited to
/// usages 0x00-0x77, which covers every standard key.
/// </summary>
internal sealed class LegacyTextLink : IKvmLink
{
    public const int BaudRate = 921600;

    private const int RawKeyBase = 0x88;
    private const int ModifierBase = 0x80;
    private const int MaxRawUsage = 0xFF - RawKeyBase; // 0x77

    // Mouse motion is accumulated and sent at a fixed rate instead of once per
    // raw input packet; a gaming mouse can report 1000 times a second.
    private static readonly TimeSpan MotionInterval = TimeSpan.FromMilliseconds(8);

    private readonly SerialTransport _transport;
    private readonly object _motionLock = new();
    private readonly System.Threading.Timer _motionTimer;
    private int _pendingX, _pendingY;
    private int _ledPercent = 100;
    private (byte R, byte G, byte B) _ledColor;

    public string ProtocolName => "legacy text (deprecated)";

    public event Action<Exception>? Faulted
    {
        add => _transport.Faulted += value;
        remove => _transport.Faulted -= value;
    }

    // The Arduino firmware never sends anything back.
    public event Action<TargetStatus>? TargetStatusChanged { add { } remove { } }
    public event Action<string>? Diagnostic { add { } remove { } }
    public TargetStatus? TargetStatus => null;
    public int? LedBrightness => null;

    public void Heartbeat() { /* no heartbeat in this protocol */ }

    public LegacyTextLink(SerialTransport transport)
    {
        _transport = transport;
        _motionTimer = new System.Threading.Timer(_ => FlushMotion(), null, MotionInterval, MotionInterval);
    }

    public void KeyDown(byte usage)
    {
        if (Encode(usage) is int code) Send('p', code);
    }

    public void KeyUp(byte usage)
    {
        if (Encode(usage) is int code) Send('e', code);
    }

    private static int? Encode(byte usage) => usage switch
    {
        >= HidUsageMap.LeftCtrl and <= HidUsageMap.RightGui => ModifierBase + (usage - HidUsageMap.LeftCtrl),
        <= MaxRawUsage => RawKeyBase + usage,
        _ => null, // international keys above 0x77 can't be expressed in this protocol
    };

    public void MouseMoveRelative(int dx, int dy)
    {
        lock (_motionLock)
        {
            _pendingX += dx;
            _pendingY += dy;
        }
    }

    private void FlushMotion()
    {
        int dx, dy;
        lock (_motionLock)
        {
            dx = _pendingX; dy = _pendingY;
            _pendingX = _pendingY = 0;
        }

        // The firmware's Mouse.move() takes signed 8-bit deltas.
        while (dx != 0 || dy != 0)
        {
            int sx = Math.Clamp(dx, -127, 127);
            int sy = Math.Clamp(dy, -127, 127);
            Send('a', sx, sy);
            dx -= sx; dy -= sy;
        }
    }

    public void MouseButton(TargetMouseButton button, bool down)
    {
        FlushMotion(); // a click must land where the pointer is now
        int code = (down ? 3 : 6) + button switch
        {
            TargetMouseButton.Left => 0,
            TargetMouseButton.Right => 1,
            _ => 2,
        };
        Send('m', code);
    }

    public void MouseWheel(int detents)
    {
        // Not supported: the legacy firmware's "a" command ignores its third value.
    }

    public void ReleaseAll()
    {
        FlushMotion();
        Send('m', 9);
    }

    // The Arduino firmware has no brightness setting, so scale the colour here.
    public void SetStatusLed(byte r, byte g, byte b)
    {
        _ledColor = (r, g, b);
        byte Scale(byte v) => (byte)((v * _ledPercent + 50) / 100);
        Send('s', Scale(r), Scale(g), Scale(b));
    }

    public void SetLedBrightness(int percent, bool persist)
    {
        _ledPercent = Math.Clamp(percent, 0, 100);
        SetStatusLed(_ledColor.R, _ledColor.G, _ledColor.B);
    }

    private void Send(char command, int v0, int v1 = 0, int v2 = 0) =>
        _transport.Send(string.Create(CultureInfo.InvariantCulture, $"{command} {v0} {v1} {v2}\n"));

    public void Dispose()
    {
        _motionTimer.Dispose();
        _transport.Dispose();
    }
}
