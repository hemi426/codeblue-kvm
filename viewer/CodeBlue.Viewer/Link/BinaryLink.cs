using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using CodeBlue.Viewer.Input;

namespace CodeBlue.Viewer.Link;

/// <summary>
/// The CodeBlue binary protocol, version 1 (docs/PROTOCOL.md), spoken by the
/// ESP-IDF firmware in firmware/.
///
/// Keyboard and mouse go out as complete state, never as events: the board is
/// always told every held key and button, so a lost frame is corrected by the
/// next one and nothing can stay stuck.
/// </summary>
internal sealed class BinaryLink : IKvmLink
{
    public const int BaudRate = 921600;
    private const byte ProtocolMajor = 1;

    private const int DefaultTimeoutMs = 500;
    private const int DebuggerTimeoutMs = 30000; // so breakpoints don't end the session

    // A gaming mouse can report 1000 times a second; send motion at most every 8 ms.
    private static readonly TimeSpan MotionInterval = TimeSpan.FromMilliseconds(8);

    private static class Msg
    {
        public const byte Hello = 0x01, Heartbeat = 0x02, SessionConfig = 0x03;
        public const byte Keyboard = 0x10, MouseAbs = 0x20, MouseRel = 0x21;
        public const byte Led = 0x30, LedBrightness = 0x31, ReleaseAll = 0x40;
        public const byte Info = 0x81, DeviceHeartbeat = 0x82, TargetStatus = 0x90, Error = 0xE0;
    }

    private readonly SerialTransport _transport;
    private readonly FrameReader _reader = new();
    private readonly ManualResetEventSlim _gotInfo = new();
    private readonly int _timeoutMs;
    private long _lastRxMs;
    private int _faulted;

    // Input state, guarded by _lock.
    private readonly object _lock = new();
    private readonly List<byte> _keys = []; // non-modifier usages, in press order
    private byte _modifiers;
    private byte _buttons;
    private int _pendingX, _pendingY, _pendingWheel;
    private int _absX = 16384, _absY = 16384;
    private bool _absDirty;
    private bool _absolute;
    private readonly System.Threading.Timer _motionTimer;

    private TargetStatus? _status;

    public string ProtocolName { get; private set; } = "CodeBlue";
    public TargetStatus? TargetStatus => _status;
    public int? LedBrightness { get; private set; }
    public bool SupportsAbsolute { get; private set; }
    public bool SupportsVbusSense { get; private set; }

    public bool AbsoluteMouse
    {
        get { lock (_lock) return _absolute; }
        set
        {
            lock (_lock)
            {
                if (_absolute == value) return;
                _absolute = value && SupportsAbsolute;
                // Held buttons move with the mode; the board releases them on the other device.
                if (_absolute) _absDirty = true;
                else SendMouseLocked(force: true);
            }
        }
    }

    public event Action<Exception>? Faulted;
    public event Action<TargetStatus>? TargetStatusChanged;
    public event Action<string>? Diagnostic;

    private BinaryLink(SerialTransport transport)
    {
        _transport = transport;
        _timeoutMs = Debugger.IsAttached ? DebuggerTimeoutMs : DefaultTimeoutMs;
        _transport.Faulted += Fault;
        _transport.StartReading(OnData);
        _motionTimer = new System.Threading.Timer(_ => FlushMotion(), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>Opens the port and does the handshake; throws if no compatible board answers.</summary>
    public static BinaryLink Open(string portName)
    {
        var link = new BinaryLink(new SerialTransport(portName, BaudRate));
        try
        {
            link.Handshake();
            return link;
        }
        catch
        {
            link.Dispose();
            throw;
        }
    }

    private void Handshake()
    {
        // HELLO is the only thing we may send until the board answers: its bytes
        // are chosen to be harmless to the old Arduino firmware.
        byte[] hello = Frame.Encode(Msg.Hello, [(byte)'C', (byte)'B', (byte)'L', (byte)'U', ProtocolMajor]);
        var deadline = Stopwatch.StartNew();
        while (deadline.ElapsedMilliseconds < 2000)
        {
            _transport.Send(hello);
            if (_gotInfo.Wait(250)) break;
        }
        if (!_gotInfo.IsSet)
            throw new KvmLinkException("No CodeBlue firmware found on this port. Check that it's the board's " +
                                       "UART port, and that the board has the CodeBlue firmware.");
        if (_infoMajor != ProtocolMajor)
            throw new KvmLinkException($"The board speaks CodeBlue protocol {_infoMajor}.{_infoMinor}; this viewer " +
                                       $"speaks {ProtocolMajor}. Update the viewer or the firmware so they match.");

        if (_timeoutMs != DefaultTimeoutMs)
        {
            Span<byte> p = stackalloc byte[2];
            BinaryPrimitives.WriteUInt16LittleEndian(p, (ushort)_timeoutMs);
            Send(Msg.SessionConfig, p);
        }
        Volatile.Write(ref _lastRxMs, Environment.TickCount64);
        _motionTimer.Change(MotionInterval, MotionInterval);
    }

    private byte _infoMajor, _infoMinor;

    // ---------------------------------------------------------------- Receive

    private void OnData(ReadOnlySpan<byte> data)
    {
        foreach (byte b in data)
        {
            if (_reader.Feed(b, out byte type, out byte[] p))
            {
                Volatile.Write(ref _lastRxMs, Environment.TickCount64);
                OnMessage(type, p);
            }
        }
    }

    private void OnMessage(byte type, byte[] p)
    {
        switch (type)
        {
            case Msg.Info when p.Length >= 8:
                _infoMajor = p[0];
                _infoMinor = p[1];
                string board = Encoding.UTF8.GetString(p, 8, p.Length - 8);
                ProtocolName = $"CodeBlue {p[2]}.{p[3]}.{p[4]} ({board})";
                LedBrightness = p[7];
                ushort caps = BinaryPrimitives.ReadUInt16LittleEndian(p.AsSpan(5));
                SupportsAbsolute = (caps & 0x0001) != 0;
                SupportsVbusSense = (caps & 0x0008) != 0;
                Diagnostic?.Invoke($"board: {ProtocolName}, protocol {p[0]}.{p[1]}, " +
                                   $"capabilities 0x{caps:X4}, LED {p[7]}%");
                _gotInfo.Set();
                break;

            case Msg.TargetStatus when p.Length >= 2:
                var status = new TargetStatus(p[0], p[1]);
                if (status != _status)
                {
                    _status = status;
                    TargetStatusChanged?.Invoke(status);
                }
                break;

            case Msg.DeviceHeartbeat:
                break;

            case Msg.Error when p.Length >= 2:
                string what = p[0] switch
                {
                    1 => "unknown message type",
                    2 => "bad payload length",
                    3 => "USB report could not be sent",
                    _ => $"code {p[0]}",
                };
                Diagnostic?.Invoke($"board error: {what} (detail 0x{p[1]:X2})");
                break;

            default:
                Diagnostic?.Invoke($"board sent unexpected message 0x{type:X2}");
                break;
        }
    }

    private void Fault(Exception ex)
    {
        if (Interlocked.Exchange(ref _faulted, 1) == 0)
            Faulted?.Invoke(ex);
    }

    // ---------------------------------------------------------------- Send

    private void Send(byte type, ReadOnlySpan<byte> payload = default) => _transport.Send(Frame.Encode(type, payload));

    public void Heartbeat()
    {
        if (!_gotInfo.IsSet) return;
        long silent = Environment.TickCount64 - Volatile.Read(ref _lastRxMs);
        if (silent > _timeoutMs)
        {
            Fault(new TimeoutException("CodeBlue board not responding."));
            return;
        }
        Send(Msg.Heartbeat);
    }

    public void KeyDown(byte usage)
    {
        lock (_lock)
        {
            if (HidUsageMap.IsModifier(usage)) _modifiers |= ModifierBit(usage);
            else if (!_keys.Contains(usage)) _keys.Add(usage);
            SendKeyboardLocked();
        }
    }

    public void KeyUp(byte usage)
    {
        lock (_lock)
        {
            if (HidUsageMap.IsModifier(usage)) _modifiers &= (byte)~ModifierBit(usage);
            else _keys.Remove(usage);
            SendKeyboardLocked();
        }
    }

    private static byte ModifierBit(byte usage) => (byte)(1 << (usage - HidUsageMap.LeftCtrl));

    private void SendKeyboardLocked()
    {
        // HID reports six keys at most; with more held, the six pressed first win.
        int count = Math.Min(_keys.Count, 6);
        Span<byte> p = stackalloc byte[2 + count];
        p[0] = _modifiers;
        p[1] = (byte)count;
        for (int i = 0; i < count; i++) p[2 + i] = _keys[i];
        Send(Msg.Keyboard, p);
    }

    public void MouseMoveRelative(int dx, int dy)
    {
        lock (_lock)
        {
            _pendingX += dx;
            _pendingY += dy;
        }
    }

    public void MouseMoveAbsolute(int x, int y)
    {
        x = Math.Clamp(x, 0, 32767);
        y = Math.Clamp(y, 0, 32767);
        lock (_lock)
        {
            if (x == _absX && y == _absY) return;
            _absX = x;
            _absY = y;
            _absDirty = true;
        }
    }

    public void MouseButton(TargetMouseButton button, bool down)
    {
        byte bit = button switch
        {
            TargetMouseButton.Left => 0x01,
            TargetMouseButton.Right => 0x02,
            _ => 0x04,
        };
        lock (_lock)
        {
            _buttons = down ? (byte)(_buttons | bit) : (byte)(_buttons & ~bit);
            SendMouseLocked(force: true); // a click must land where the pointer is now
        }
    }

    public void MouseWheel(int detents)
    {
        lock (_lock) _pendingWheel += detents;
    }

    private void FlushMotion()
    {
        lock (_lock) SendMouseLocked(force: false);
    }

    private void SendMouseLocked(bool force)
    {
        if (_absolute)
        {
            SendAbsoluteLocked(force);
            return;
        }
        if (!force && _pendingX == 0 && _pendingY == 0 && _pendingWheel == 0) return;

        // The board splits large deltas itself; i16 is plenty for 8 ms of motion.
        short dx = (short)Math.Clamp(_pendingX, short.MinValue, short.MaxValue);
        short dy = (short)Math.Clamp(_pendingY, short.MinValue, short.MaxValue);
        sbyte wheel = (sbyte)Math.Clamp(_pendingWheel, -127, 127);
        _pendingX -= dx;
        _pendingY -= dy;
        _pendingWheel -= wheel;

        Span<byte> p = stackalloc byte[7];
        BinaryPrimitives.WriteInt16LittleEndian(p, dx);
        BinaryPrimitives.WriteInt16LittleEndian(p[2..], dy);
        p[4] = _buttons;
        p[5] = (byte)wheel;
        p[6] = 0; // pan: not captured yet
        Send(Msg.MouseRel, p);
    }

    private void SendAbsoluteLocked(bool force)
    {
        if (!force && !_absDirty && _pendingWheel == 0) return;
        sbyte wheel = (sbyte)Math.Clamp(_pendingWheel, -127, 127);
        _pendingWheel -= wheel;
        _pendingX = _pendingY = 0; // relative motion has no meaning here
        _absDirty = false;

        Span<byte> p = stackalloc byte[7];
        BinaryPrimitives.WriteUInt16LittleEndian(p, (ushort)_absX);
        BinaryPrimitives.WriteUInt16LittleEndian(p[2..], (ushort)_absY);
        p[4] = _buttons;
        p[5] = (byte)wheel;
        p[6] = 0;
        Send(Msg.MouseAbs, p);
    }

    public void ReleaseAll()
    {
        lock (_lock)
        {
            _keys.Clear();
            _modifiers = 0;
            _buttons = 0;
            _pendingX = _pendingY = _pendingWheel = 0;
        }
        Send(Msg.ReleaseAll);
    }

    public void SetStatusLed(byte r, byte g, byte b) => Send(Msg.Led, [r, g, b]);

    public void SetLedBrightness(int percent, bool persist)
    {
        LedBrightness = Math.Clamp(percent, 0, 100);
        Send(Msg.LedBrightness, [(byte)LedBrightness.Value, persist ? (byte)1 : (byte)0]);
    }

    public void Dispose()
    {
        _motionTimer.Dispose();
        _transport.Dispose();
        _gotInfo.Dispose();
    }
}
