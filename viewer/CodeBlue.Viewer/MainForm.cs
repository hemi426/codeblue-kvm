using System.ComponentModel;
using System.IO.Ports;
using CodeBlue.Viewer.Input;
using CodeBlue.Viewer.Link;
using CodeBlue.Viewer.Native;
using CodeBlue.Viewer.Video;
using LibVLCSharp.Shared;

namespace CodeBlue.Viewer;

internal sealed class MainForm : Form
{
    private const uint VK_RETURN = 0x0D;
    private const uint VK_1 = 0x31;
    private const uint VK_S = 0x53;
    private const uint VK_APPS = 0x5D; // Menu key: releases capture
    private const uint VK_L = 0x4C;
    private const uint SC_DELETE = 0x53; // Delete and the keypad's Del share this scan code

    private const string ReleaseHint =
        "Keyboard and mouse are going to the target. To release: press and release Ctrl+Alt (or the Menu key).";

    // If the UI thread stops responding this long while captured, the watchdog frees the cursor.
    private const long WatchdogTimeoutMs = 3000;

    // libVLC debug output is capped; warnings and errors are always written.
    private const long MaxVlcLogBytes = 20L * 1024 * 1024;

    // Mouse modes (see "Mouse modes" in docs/development/protocol.md).
    internal const string MouseAuto = "Auto", MouseAbsolute = "Absolute", MouseRelative = "Relative";

    private static readonly string VlcLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodeBlue", "libvlc.log");

    // Full-scale colours; SetLed() scales them by the LED % setting.
    private static readonly (byte R, byte G, byte B) LedConnected = (0, 255, 0);
    private static readonly (byte R, byte G, byte B) LedIdle = (0, 0, 255);

    private readonly ViewerSettings _settings = ViewerSettings.Load();

    private readonly LibVLC _libVlc;
    private readonly Panel _video; // holds each session's drawing window
    private volatile VideoSession? _session; // the capture being shown, if any (read by the fps timer)
    private readonly List<VideoSession> _stopping = []; // stopped sessions whose players haven't let go yet
    private readonly HashSet<VideoSession> _stuck = []; // ...of those, the ones stuck for a while (they hold the dongle)
    private bool _connecting;
    private readonly System.Threading.Timer _fpsTimer;
    private string? _fpsText;

    private readonly FlowLayoutPanel _toolbar;
    private readonly ComboBox _portBox, _deviceBox;
    private readonly CheckBox _videoOnlyBox;
    private readonly Button _connectButton, _captureButton, _cadButton, _boardButton;
    // Keep awake: a toggle (the board's state) with a settings arrow beside it.
    private readonly CheckBox _keepAwakeButton;
    private readonly Button _keepAwakeMenu;
    private bool _syncingKeepAwake; // set while showing the board's state, so it isn't sent back
    private readonly System.Windows.Forms.Timer _keepAwakeTimer; // ticks the countdown
    // Status bar: the main message on the left, fixed fields on the right.
    private readonly Panel _statusBar;
    private readonly Label _status;
    private readonly Label _updateField, _keepAwakeField, _fpsField, _mouseField, _numField, _capsField, _scrollField;
    private readonly System.Windows.Forms.Timer _updateTimer; // asks whether a check is due
    private bool _checkingUpdates;
    private readonly ToolTip _statusTips = new();
    private readonly System.Windows.Forms.Timer _transientTimer;
    private string _baseStatus = ""; // what the main message shows when no transient message is up
    private Font? _boldFont;
    private Font BoldFont => _boldFont ??= new Font(Font, FontStyle.Bold);
    private readonly Label _banner;
    // The other way to show capture: a slim frame at the video area's edges,
    // on top of the picture's window, so nothing moves or resizes.
    internal const string IndicatorBanner = "Banner", IndicatorBorder = "Border";
    private static readonly Color CaptureColor = Color.FromArgb(255, 193, 7);
    private readonly Panel[] _border = new Panel[4]; // top, bottom, left, right

    private IKvmLink? _link;
    private string? _linkPort;

    // After the link is lost mid-session, the viewer keeps trying to get it back.
    private readonly System.Windows.Forms.Timer _reconnectTimer;
    private string? _reconnectPort, _reconnectReason;
    private bool _reconnectInFlight;
    private bool _videoRunning;
    private uint _videoWidth, _videoHeight; // 0 until the stream reports its size

    private KeyboardHook? _hook;
    private volatile bool _captured; // read by the watchdog thread
    private bool _cursorHidden;
    private uint _swallowUpVk; // key-up of a viewer hotkey, kept from the target
    private bool _releaseChord; // Ctrl and Alt are down, and nothing else
    private readonly HashSet<byte> _held = [];

    private readonly System.Windows.Forms.Timer _heartbeatTimer;
    private long _uiHeartbeat = Environment.TickCount64;
    private volatile bool _watchdogTripped;
    private volatile bool _closing;

    private StreamWriter? _vlcLog;
    private readonly object _vlcLogLock = new();
    private long _vlcLogBytes;
    private long _lastErrorPost;
    private volatile bool _captureModeUnsupported; // libVLC said the device lacks the requested mode
    private CaptureOptions? _lastCaptureOptions;

    private bool _fullscreen;
    private FormWindowState _savedState;
    private Rectangle _savedBounds;

    public MainForm()
    {
        Text = $"CodeBlue Viewer {ViewerVersion}";
        using (var iconStream = typeof(MainForm).Assembly.GetManifestResourceStream("CodeBlue.ico"))
            if (iconStream is not null) Icon = new Icon(iconStream);
        Font = new Font("Segoe UI", 10f);
        ClientSize = new Size(1280, 800);
        MinimumSize = new Size(800, 500);
        KeyPreview = true;

        // No renderer (video output) choice: in libVLC 3, embedding the player in our
        // window resets its "vout" setting to automatic, overriding both --vout and a
        // per-media :vout. libVLC 4 adds proper output selection; revisit then.
        // CodeBlue captures no audio; name the do-nothing output, which libVLC
        // wouldn't pick by itself (and the real ones aren't shipped).
        _libVlc = new LibVLC("--no-video-title-show", "--no-snapshot-preview", "--aout=adummy", "--verbose=2");
        OpenVlcLog();
        // Each Connect makes its own player (see VideoSession); this is where they draw.
        _video = new Panel { Dock = DockStyle.Fill, BackColor = Color.Black };
        _video.Resize += (_, _) =>
        {
            if (_captured) ClipCursorToVideo();
            LayOutBorder();
        };
        for (int i = 0; i < _border.Length; i++)
        {
            _border[i] = new Panel { BackColor = CaptureColor, Visible = false };
            _video.Controls.Add(_border[i]);
        }

        _connectButton = MakeButton("Connect", (_, _) => ToggleConnect());
        _portBox = MakeCombo(110);
        var refreshButton = MakeButton("Refresh", (_, _) => { RefreshPorts(); RefreshDevices(); });
        _videoOnlyBox = new CheckBox { Text = "Video only", AutoSize = true, Margin = new Padding(10, 6, 2, 2) };
        _videoOnlyBox.CheckedChanged += (_, _) => UpdateUi();
        _deviceBox = MakeCombo(210, editable: true);
        _captureButton = MakeButton("Capture", (_, _) => StartCapture());
        _cadButton = MakeButton("Ctrl+Alt+Del", (_, _) => SendCtrlAltDel());
        var snapshotButton = MakeButton("Snapshot", (_, _) => TakeSnapshot());
        var fullscreenButton = MakeButton("Full screen", (_, _) => ToggleFullscreen());
        _boardButton = MakeButton("Board settings...", (_, _) => ShowBoardSettings());
        var optionsButton = MakeButton("Viewer options...", (_, _) => ShowViewerOptions());

        // A split button: the toggle, and an arrow for its settings, flush together.
        _keepAwakeButton = new CheckBox
        {
            Text = "Keep awake",
            Appearance = Appearance.Button,
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleCenter,
            Margin = new Padding(2, 2, 0, 2),
        };
        _keepAwakeButton.CheckedChanged += (_, _) =>
        {
            if (!_syncingKeepAwake) _link?.SetKeepAwake(_keepAwakeButton.Checked);
        };
        _keepAwakeMenu = new Button
        {
            Text = "\u25BE", // ▾
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 2, 2, 2),
            Padding = new Padding(2, 0, 2, 0),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom, // as tall as the toggle beside it
        };
        _keepAwakeMenu.Click += (_, _) => ShowKeepAwakeSettings();
        var keepAwake = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        keepAwake.Controls.AddRange([_keepAwakeButton, _keepAwakeMenu]);
        _keepAwakeTimer = new System.Windows.Forms.Timer { Interval = 500 };
        _keepAwakeTimer.Tick += (_, _) => UpdateKeepAwakeField();

        _toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            WrapContents = true,
            Padding = new Padding(4),
        };
        _toolbar.Controls.AddRange(
        [
            _connectButton,
            MakeLabel("Port"), _portBox, refreshButton, _videoOnlyBox,
            MakeLabel("Video"), _deviceBox,
            _captureButton, _cadButton, keepAwake, snapshotButton, fullscreenButton,
            _boardButton, optionsButton,
        ]);

        int statusHeight = Font.Height + 12;
        _status = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(6, 0, 6, 0),
            AutoEllipsis = true,
        };
        // Fixed widths (sized for the bold, widest text), so nothing shifts as fields light up.
        // Shown only when a check has found something newer; a click opens the docs site.
        _updateField = MakeStatusField("Firmware 00.00.00 available", statusHeight);
        _updateField.ForeColor = SystemColors.HotTrack;
        _updateField.Cursor = Cursors.Hand;
        _updateField.Visible = false;
        _updateField.Click += (_, _) => OpenSite();
        _keepAwakeField = MakeStatusField("Keep awake in 60:00", statusHeight);
        _fpsField = MakeStatusField("Video off", statusHeight);
        _statusTips.SetToolTip(_fpsField, "Frames per second shown from the capture dongle");
        _mouseField = MakeStatusField("Mouse: Absolute (Auto)", statusHeight);
        _numField = MakeStatusField("NUM", statusHeight);
        _capsField = MakeStatusField("CAPS", statusHeight);
        _scrollField = MakeStatusField("SCRL", statusHeight);
        _statusTips.SetToolTip(_numField, "Num Lock on the target");
        _statusTips.SetToolTip(_capsField, "Caps Lock on the target");
        _statusTips.SetToolTip(_scrollField, "Scroll Lock on the target");
        var fields = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            AutoSize = true,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        fields.Controls.AddRange([_updateField, _keepAwakeField, _fpsField, _mouseField, _numField, _capsField, _scrollField]);
        _statusBar = new Panel { Dock = DockStyle.Bottom, Height = statusHeight };
        _statusBar.Controls.Add(_status); // fills what the fields leave
        _statusBar.Controls.Add(fields);
        // Transient messages (a saved snapshot, say) give way to the normal status after a while.
        _transientTimer = new System.Windows.Forms.Timer { Interval = 5000 };
        _transientTimer.Tick += (_, _) =>
        {
            _transientTimer.Stop();
            _status.Text = _baseStatus;
        };
        _banner = new Label
        {
            Dock = DockStyle.Top,
            AutoSize = false,
            Height = Font.Height + 14,
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = CaptureColor,
            ForeColor = Color.Black,
            Font = new Font(Font, FontStyle.Bold),
            Text = ReleaseHint,
            Visible = false,
        };

        // Dock order: controls added later claim the outer edges first, so the
        // toolbar sits above the banner and the video fills what's left.
        Controls.Add(_video);
        Controls.Add(_banner);
        Controls.Add(_toolbar);
        Controls.Add(_statusBar);

        RefreshPorts();
        RefreshDevices();
        _videoOnlyBox.Checked = _settings.VideoOnly;
        UpdateStatusFields();

        Deactivate += (_, _) => StopCapture();
        FormClosing += (_, _) => { Disconnect(); SaveSettings(); };

        // Dead-man's switch for a hung UI thread: the timer proves the UI is alive,
        // and the watchdog thread frees the cursor if it stops ticking during capture.
        // (Windows itself drops a low-level keyboard hook that stops responding.)
        // The same timer sends the link's heartbeat, so a frozen viewer also stops
        // heartbeating and the board releases every key and button.
        _heartbeatTimer = new System.Windows.Forms.Timer { Interval = 50 };
        _heartbeatTimer.Tick += (_, _) =>
        {
            Volatile.Write(ref _uiHeartbeat, Environment.TickCount64);
            _link?.Heartbeat();
            // Raw input can arrive just before Windows moves the cursor, so the
            // last move of a gesture may be missed; this catches it up.
            if (_captured && _link is { AbsoluteMouse: true } link) SendPointer(link);
        };
        _heartbeatTimer.Start();

        _reconnectTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _reconnectTimer.Tick += (_, _) => TryReconnect();
        new Thread(WatchdogLoop) { IsBackground = true, Name = "CodeBlue capture watchdog" }.Start();

        // Frame rate, read off the UI thread: libVLC calls are what we can't trust to return.
        _fpsTimer = new System.Threading.Timer(_ => SampleFrameRate(), null, 500, 500);

        // The update check (if it's on): once the window is up, then whenever a day has passed.
        _updateTimer = new System.Windows.Forms.Timer { Interval = 60 * 60 * 1000 };
        _updateTimer.Tick += (_, _) => CheckForUpdates();
        _updateTimer.Start();
        Shown += (_, _) => CheckForUpdates();

        UpdateUi();
    }

    // ---------------------------------------------------------------- UI helpers

    private static Button MakeButton(string text, EventHandler onClick)
    {
        var b = new Button { Text = text, AutoSize = true, Margin = new Padding(2, 2, 2, 2) };
        b.Click += onClick;
        return b;
    }

    private static ComboBox MakeCombo(int width, bool editable = false) => new()
    {
        Width = width,
        DropDownStyle = editable ? ComboBoxStyle.DropDown : ComboBoxStyle.DropDownList,
        Margin = new Padding(2, 4, 2, 4),
    };

    private static Label MakeLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Margin = new Padding(10, 8, 2, 0),
    };

    private static void SelectItem(ComboBox box, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            int i = box.FindStringExact(value);
            if (i >= 0) { box.SelectedIndex = i; return; }
            if (box.DropDownStyle == ComboBoxStyle.DropDown) { box.Text = value; return; }
        }
        if (box.SelectedIndex < 0 && box.Items.Count > 0) box.SelectedIndex = 0;
    }

    private void RefreshPorts()
    {
        string? current = _portBox.SelectedItem as string ?? _settings.SerialPort;
        _portBox.Items.Clear();
        _portBox.Items.AddRange(
        [
            .. SerialPort.GetPortNames().Distinct().OrderBy(p => p.Length).ThenBy(p => p, StringComparer.Ordinal),
        ]);
        SelectItem(_portBox, current);
    }

    private void RefreshDevices()
    {
        string current = _deviceBox.Text.Length > 0 ? _deviceBox.Text : _settings.VideoDevice ?? "";
        _deviceBox.Items.Clear();
        _deviceBox.Items.AddRange([.. VideoDevices.List()]);
        SelectItem(_deviceBox, current);
    }

    private void UpdateUi()
    {
        bool running = _videoRunning || _link is not null;
        _connectButton.Text = running ? "Disconnect" : "Connect";
        _videoOnlyBox.Enabled = _deviceBox.Enabled = !running;
        _portBox.Enabled = !running && !_videoOnlyBox.Checked;

        _captureButton.Enabled = _link is not null && !_captured;
        _cadButton.Enabled = _link is not null;
        _boardButton.Enabled = _link is not null;
        bool border = _settings.CaptureIndicator == IndicatorBorder;
        _banner.Visible = _captured && !border;
        foreach (var edge in _border) edge.Visible = _captured && border;
        LayOutBorder();

        // A stuck player holds the dongle until it's unplugged; say so above all else.
        string stuck = _stuck.Count > 0 ? "Video stuck: unplug the dongle and plug it back in. " : "";
        SetStatus(stuck + (_captured
            ? $"Captured.{TargetText()} Release: Ctrl+Alt. Full screen: Ctrl+Alt+Enter. 1:1 pixels: Ctrl+Alt+1. Snapshot: Ctrl+Alt+S."
            : !running
                ? "Not connected. F11: full screen, Ctrl+Alt+S or F12: snapshot, Ctrl+Alt+1: 1:1 pixels, Ctrl+Alt+C: capture."
                : _link is null
                    ? _reconnectPort is not null
                        ? $"Showing {_deviceBox.Text}. Lost the board on {_reconnectPort}: {_reconnectReason}. Reconnecting..."
                        : $"Showing {_deviceBox.Text}. No keyboard/mouse link."
                    : $"Showing {_deviceBox.Text}. Link: {_link.ProtocolName} on {_linkPort}.{TargetText()} Click Capture (or press Ctrl+Alt+C) to take control."));
        UpdateStatusFields();
    }

    private Label MakeStatusField(string widest, int height)
    {
        var field = new Label
        {
            AutoSize = false,
            Size = new Size(TextRenderer.MeasureText(widest, BoldFont).Width + 16, height),
            TextAlign = ContentAlignment.MiddleCenter,
            Margin = Padding.Empty,
            Text = widest,
        };
        // A thin divider on the left, like a classic status bar pane.
        field.Paint += (_, e) =>
            e.Graphics.DrawLine(SystemPens.ControlDark, 0, 4, 0, field.Height - 5);
        return field;
    }

    /// <summary>The mouse mode and lock-key fields at the right of the status bar.</summary>
    private void UpdateStatusFields()
    {
        string setting = _settings.MouseMode;
        if (_link is null)
        {
            _mouseField.Text = $"Mouse: {setting}";
            _statusTips.SetToolTip(_mouseField, "Mouse mode, once connected");
        }
        else
        {
            bool absolute = WantAbsolute();
            string mode = absolute ? MouseAbsolute : MouseRelative;
            _mouseField.Text = setting == MouseAuto ? $"Mouse: {mode} (Auto)" : $"Mouse: {mode}";
            _statusTips.SetToolTip(_mouseField, MouseModeReason(setting, absolute));
        }

        UpdateKeepAwake();
        UpdateUpdateField();

        // Lock keys only mean something while the target has the keyboard awake.
        var t = _link?.TargetStatus is { Mounted: true, Suspended: false } s ? s : default;
        SetLockField(_numField, t.NumLock);
        SetLockField(_capsField, t.CapsLock);
        SetLockField(_scrollField, t.ScrollLock);
    }

    /// <summary>The Keep awake button and status field, from what the board last said.</summary>
    private void UpdateKeepAwake()
    {
        bool supported = _link is { SupportsKeepAwake: true };
        _keepAwakeButton.Enabled = _keepAwakeMenu.Enabled = supported;
        _syncingKeepAwake = true;
        _keepAwakeButton.Checked = supported && _link!.KeepAwake is { On: true };
        _syncingKeepAwake = false;
        _statusTips.SetToolTip(_keepAwakeButton, supported
            ? "Keeps the target from sleeping or locking while nobody's using it. It runs on the board, " +
              "so it carries on after you disconnect."
            : _link is null ? "Keep awake, once connected" : "Keep awake needs firmware 0.5.0 or later");
        _statusTips.SetToolTip(_keepAwakeMenu, "Keep awake settings");
        _keepAwakeField.Visible = supported;
        UpdateKeepAwakeField();
    }

    private void UpdateKeepAwakeField()
    {
        var status = _link is { SupportsKeepAwake: true } link ? link.KeepAwake : null;
        string text, tip;
        bool bold = false;
        switch (status?.State)
        {
            case KeepAwakeState.Waiting:
                int left = status.Value.SecondsLeft(Environment.TickCount64);
                text = $"Keep awake in {left / 60}:{left % 60:00}";
                tip = "Keep awake starts once the target has had no input for a while; any input restarts the countdown";
                break;
            case KeepAwakeState.Active:
                text = "Keeping awake";
                tip = "Keep awake is acting now, every so often, until there's input again";
                bold = true;
                break;
            case KeepAwakeState.Paused:
                text = "Keep awake paused";
                tip = "The target is asleep or not connected; keep awake never wakes it";
                break;
            default:
                text = "Keep awake off";
                tip = "Turn it on with the Keep awake button";
                break;
        }
        _keepAwakeField.Text = text;
        _keepAwakeField.Font = bold ? BoldFont : Font;
        _keepAwakeField.ForeColor = status is { State: KeepAwakeState.Waiting or KeepAwakeState.Active }
            ? SystemColors.ControlText : SystemColors.GrayText;
        _statusTips.SetToolTip(_keepAwakeField, tip);
        _keepAwakeTimer.Enabled = status?.State == KeepAwakeState.Waiting;
    }

    private void ShowKeepAwakeSettings()
    {
        if (_link is not { } link) return;
        using var dialog = new KeepAwakeForm(this, link);
        dialog.ShowDialog(this);
    }

    private string MouseModeReason(string setting, bool absolute)
    {
        if (_link is { SupportsAbsolute: false })
            return "Relative: this board has no absolute pointer";
        if (setting != MouseAuto)
            return $"{setting}: chosen in the Mouse setting";
        if (absolute)
            return "Absolute (Auto): the target uses the absolute pointer";
        return _link?.TargetStatus switch
        {
            { MouseBoot: true } => "Relative (Auto): the target asked for a basic boot mouse",
            { AbsoluteUnread: true } => "Relative (Auto): the target isn't reading the absolute pointer",
            _ => "Relative (Auto)",
        };
    }

    private void SetLockField(Label field, bool lit)
    {
        field.Font = lit ? BoldFont : Font;
        field.ForeColor = lit ? SystemColors.ControlText : SystemColors.GrayText;
    }

    /// <summary>The target's state for the status bar, or empty if the link can't tell.</summary>
    private string TargetText()
    {
        if (_link?.TargetStatus is not TargetStatus t) return "";
        // With VBUS sense, no USB power means the cable is out or the target is off.
        bool unpowered = _link.SupportsVbusSense && !t.VbusPresent;
        if (!t.Mounted)
            return unpowered
                ? " Target: unplugged or powered off (no USB power)."
                : " Target: not connected (rebooting, or the board's native USB port isn't plugged in).";
        if (t.Suspended)
            return unpowered
                ? " Target: unplugged or powered off (no USB power)."
                : _link.SupportsVbusSense
                    ? " Target: asleep; a keypress may wake it."
                    : " Target: asleep or disconnected; a keypress may wake it.";
        return " Target: connected."; // its lock keys have their own fields
    }

    private void OnTargetStatus(TargetStatus status)
    {
        WriteLog($"codeblue: target status flags 0x{status.Flags:X2}, LEDs 0x{status.KeyboardLeds:X2}");
        // Capture deliberately survives the target dropping the device: a
        // rebooting target does that several times, and "capture, hold F2,
        // reboot" has to work. The board delivers held keys again as soon as
        // the target picks it back up.
        ApplyMouseMode(); // Auto follows the target (boot mouse, or an unread absolute pointer)
        UpdateUi();
    }

    // <Version> from the .csproj. The SDK appends "+<commit>" to the
    // informational version; the title shows only the release number.
    private static string ViewerVersion => Application.ProductVersion.Split('+')[0];

    /// <summary>Sets the normal status message (shown again after any transient one).</summary>
    private void SetStatus(string text)
    {
        _baseStatus = text;
        if (!_transientTimer.Enabled) _status.Text = text;
    }

    /// <summary>Shows a message for a few seconds, then the normal status again.</summary>
    private void ShowTransient(string text)
    {
        _status.Text = text;
        _transientTimer.Stop();
        _transientTimer.Start();
    }

    private void ShowError(string message) =>
        MessageBox.Show(this, message, "CodeBlue", MessageBoxButtons.OK, MessageBoxIcon.Warning);

    private void SaveSettings()
    {
        _settings.SerialPort = _portBox.SelectedItem as string ?? _settings.SerialPort;
        _settings.VideoDevice = _deviceBox.Text.Length > 0 ? _deviceBox.Text : _settings.VideoDevice;
        _settings.VideoOnly = _videoOnlyBox.Checked;
        _settings.Save();
    }

    // ---------------------------------------------------------------- Connect

    private void ToggleConnect()
    {
        if (_connecting) return;
        if (_videoRunning || _link is not null) Disconnect();
        else Connect();
    }

    private async void Connect()
    {
        string device = _deviceBox.Text.Trim();
        if (device.Length == 0)
        {
            ShowError("Pick a video capture device first.");
            return;
        }

        var options = new CaptureOptions(
            device,
            _settings.Format,
            _settings.Size,
            _settings.Fps,
            Math.Clamp(_settings.CachingMs, 0, 2000));

        // A player that's still stopping may still hold the dongle; give it a moment.
        if (_stopping.Count > 0)
        {
            _connecting = true;
            _connectButton.Enabled = false;
            SetStatus("Waiting for the last video to stop...");
            await Task.WhenAny(Task.WhenAll(_stopping.Select(s => s.Stopped)), Task.Delay(3000));
            _connecting = false;
            _connectButton.Enabled = true;
            if (IsDisposed || _closing) return;
            if (_stopping.Count > 0)
                WriteLog($"codeblue: {_stopping.Count} earlier player(s) still stopping; starting a new one anyway");
        }

        string[] vlcOptions = options.ToVlcOptions();
        _captureModeUnsupported = false;
        _lastCaptureOptions = options;
        WriteLog($"codeblue: opening dshow:// {string.Join(' ', vlcOptions)}");
        var session = new VideoSession(_libVlc, _video, vlcOptions);
        session.Player.Playing += (_, _) => WriteLog("player: playing");
        session.Player.Vout += (_, e) =>
        {
            WriteLog($"player: video outputs = {e.Count}");
            // Not from inside libVLC's event callback: calling back into libVLC there can deadlock.
            if (e.Count > 0) PostToUi(() => { if (_session == session) ApplySquarePixels(); });
        };
        session.Player.EncounteredError += (_, _) =>
        {
            WriteLog("player: encountered error");
            PostToUi(() =>
            {
                if (_session == session) SetStatus(VideoFailedText());
            });
        };
        _session = session;
        bool started = session.Play();
        WriteLog($"codeblue: Play() returned {started}");
        _videoRunning = true;

        string? port = _portBox.SelectedItem as string;
        if (!_videoOnlyBox.Checked)
        {
            if (port is null)
            {
                ShowError("No serial port selected, so this session is video only.");
            }
            else
            {
                try
                {
                    SetStatus($"Looking for a CodeBlue board on {port}...");
                    Cursor.Current = Cursors.WaitCursor; // the handshake can take up to two seconds
                    InstallLink(KvmLinks.Open(port), port);
                }
                catch (KvmLinkException ex)
                {
                    ShowError($"{port}: {ex.Message}\n\nThe video will keep running without keyboard and mouse.");
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
                {
                    ShowError($"Could not open {port}: {ex.Message}\n\n" +
                              "Is another program (a serial monitor, another viewer) holding it? " +
                              "The video will keep running without keyboard and mouse.");
                }
            }
        }

        SaveSettings();
        UpdateUi();
        if (!started) SetStatus(VideoFailedText("Video could not start."));
    }

    /// <summary>Makes an open link the current one and hooks up its events.</summary>
    private void InstallLink(IKvmLink link, string port)
    {
        _link = link;
        _linkPort = port;
        // Events from a link we've since closed are ignored.
        link.Faulted += ex => PostToUi(() => { if (_link == link) OnLinkFault(ex); });
        link.Diagnostic += message => WriteLog($"codeblue: {message}");
        link.TargetStatusChanged += status => PostToUi(() =>
        {
            if (_link == link) OnTargetStatus(status);
        });
        link.KeepAwakeChanged += _ => PostToUi(() =>
        {
            if (_link == link) UpdateKeepAwake();
        });
        WriteLog($"codeblue: link open: {link.ProtocolName} on {port}");

        // The board is the source of truth for its brightness, if it has one.
        if (link.LedBrightness is int boardPercent)
            _settings.LedBrightnessPercent = Math.Clamp(boardPercent, 0, 100);
        else
            link.SetLedBrightness(_settings.LedBrightnessPercent, persist: false);
        SetLed(LedConnected);
    }

    private void Disconnect()
    {
        StopReconnect();
        StopCapture();
        if (_link is not null)
        {
            _link.ReleaseAll();
            SetLed(LedIdle);
            _link.Dispose(); // drains the queue, so the two commands above go out first
            _link = null;
            _linkPort = null;
        }
        if (_session is { } session)
        {
            StopSession(session);
            _session = null;
            _videoRunning = false;
            _videoWidth = _videoHeight = 0;
        }
        UpdateUi();
    }

    /// <summary>Stops a session without waiting for it; a stop that hangs leaves the viewer free.</summary>
    private void StopSession(VideoSession session)
    {
        _stopping.Add(session);
        session.BeginStop(ms => PostToUi(() =>
        {
            WriteLog($"codeblue: video stopped after {ms} ms");
            _stopping.Remove(session);
            session.Surface.Dispose();
            if (_stuck.Remove(session)) UpdateUi(); // the dongle is free again
        }));
        // A stop that hangs means a stalled dongle, held until it's unplugged: say so.
        _ = session.Stopped.WaitAsync(TimeSpan.FromSeconds(3)).ContinueWith(t =>
        {
            if (!t.IsFaulted) return;
            WriteLog("codeblue: video still stopping after 3 s; leaving it behind");
            PostToUi(() =>
            {
                if (!_stopping.Contains(session)) return; // finished meanwhile
                _stuck.Add(session);
                UpdateUi();
            });
        }, TaskScheduler.Default);
    }

    private string VideoFailedText(string what = "Video playback failed.") =>
        _captureModeUnsupported ? UnsupportedModeText()
        : _stopping.Count > 0 ? $"{what} The last video never let go of the dongle; unplug it and plug it back in."
        : $"{what} Details in {VlcLogPath}";

    /// <summary>On the timer thread: the fps field, posted to the UI only when it changes.</summary>
    private void SampleFrameRate()
    {
        var session = _session;
        string text;
        try
        {
            text = session?.SampleFrameRate() switch
            {
                null when session is null => "Video off",
                null => _fpsText ?? "Video off",
                0 => "No video",
                double fps => $"{fps:0} fps",
            };
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException)
        {
            return; // the session ended under us
        }
        if (text == _fpsText) return;
        _fpsText = text;
        PostToUi(() => _fpsField.Text = text);
    }

    /// <summary>
    /// Displays the capture with square pixels, whatever the dshow module assumed.
    /// Covers Size = Auto, where the frame size isn't known until video starts.
    /// </summary>
    private void ApplySquarePixels()
    {
        if (_session is not { } session) return;
        uint w = 0, h = 0;
        if (session.Player.Size(0, ref w, ref h) && w > 0 && h > 0)
        {
            string aspect = CaptureOptions.AspectOf(w, h);
            session.Player.AspectRatio = aspect;
            (_videoWidth, _videoHeight) = (w, h);
            ApplyScale(); // re-apply to the new video output
            if (_captured) ClipCursorToVideo();
            WriteLog($"codeblue: video is {w}x{h}; display aspect set to {aspect}");
        }
        else
        {
            WriteLog("codeblue: video size not available yet; leaving aspect ratio automatic");
        }
    }

    /// <summary>
    /// 1:1 shows each captured pixel as exactly one screen pixel, centred, for the
    /// sharpest possible text; a capture larger than the window is cropped to its
    /// centre. Otherwise the picture is scaled to fit, which softens thin fonts
    /// when the scale isn't a whole number (1920 to 2560 is 1.33x).
    /// </summary>
    private void ApplyScale()
    {
        if (_session is { } session) session.Player.Scale = _settings.NativeScale ? 1f : 0f;
    }

    private void ToggleNativeScale()
    {
        _settings.NativeScale = !_settings.NativeScale;
        ApplyScale();
        if (_captured) ClipCursorToVideo();
        _settings.Save();
    }

    // ---------------------------------------------------------------- Dialogs

    private void ShowBoardSettings()
    {
        if (_link is not { } link) return;
        using var dialog = new BoardSettingsForm(this, link, _settings.BoardPreset);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _settings.LedBrightnessPercent = dialog.LedPercent;
            _settings.BoardPreset = dialog.PresetName ?? _settings.BoardPreset;
            _settings.Save();
        }
        UpdateUi(); // a new VBUS pin changes what the target's status means
    }

    private void ShowViewerOptions()
    {
        using var dialog = new ViewerOptionsForm(this, _settings, _videoRunning);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        _settings.Save();
        // Video settings wait for the next Connect; these apply now.
        ApplyScale();
        ApplyMouseMode();
        if (_captured) ClipCursorToVideo();
        UpdateUi(); // the capture indicator, and the status fields
        CheckForUpdates(); // in case it was just turned on
    }

    // ---------------------------------------------------------------- Update check

    /// <summary>Asks the docs site for the latest versions, if the option is on and a day has passed.</summary>
    private async void CheckForUpdates()
    {
        if (!_settings.CheckForUpdates || _checkingUpdates) return;
        var now = DateTime.UtcNow;
        if (_settings.UpdateCheckedUtc is DateTime last && last <= now && now - last < UpdateCheck.Interval) return;
        _checkingUpdates = true;
        try
        {
            var latest = await UpdateCheck.FetchAsync();
            if (_closing) return;
            if (latest is null)
            {
                // Quietly try again next hour.
                WriteLog("codeblue: update check: couldn't read the CodeBlue site");
                return;
            }
            _settings.UpdateCheckedUtc = now;
            _settings.LatestViewer = latest.Viewer;
            _settings.LatestFirmware = latest.Firmware;
            _settings.Save();
            WriteLog($"codeblue: update check: latest viewer {latest.Viewer ?? "unknown"}, " +
                     $"firmware {latest.Firmware ?? "unknown"}");
            UpdateUpdateField();
        }
        finally
        {
            _checkingUpdates = false;
        }
    }

    /// <summary>The status bar note: a newer viewer, or newer firmware than the connected board's.</summary>
    private void UpdateUpdateField()
    {
        bool on = _settings.CheckForUpdates;
        var viewer = on ? UpdateCheck.Newer(_settings.LatestViewer, Version.TryParse(ViewerVersion, out var v) ? v : null) : null;
        var board = _link?.FirmwareVersion;
        var firmware = on ? UpdateCheck.Newer(_settings.LatestFirmware, board) : null;
        _updateField.Visible = viewer is not null || firmware is not null;
        if (!_updateField.Visible) return;
        _updateField.Text = viewer is not null && firmware is not null ? "Updates available"
            : viewer is not null ? $"Viewer {viewer} available"
            : $"Firmware {firmware} available";
        var tip = new List<string>();
        if (viewer is not null) tip.Add($"Viewer {viewer} is out (this is {ViewerVersion}).");
        if (firmware is not null) tip.Add($"Firmware {firmware} is out (the board has {board}).");
        tip.Add("Click to open the CodeBlue site.");
        _statusTips.SetToolTip(_updateField, string.Join("\n", tip));
    }

    private void OpenSite()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(UpdateCheck.SiteUrl) { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            ShowTransient($"Couldn't open a browser. The CodeBlue site: {UpdateCheck.SiteUrl}");
        }
    }

    /// <summary>Sends a full-scale status colour; the link applies the LED % setting.</summary>
    private void SetLed((byte R, byte G, byte B) color) => _link?.SetStatusLed(color.R, color.G, color.B);

    private void OnLinkFault(Exception ex)
    {
        if (_link is null) return;
        string? port = _linkPort;
        StopCapture();
        try { _link.Dispose(); } catch { /* already broken */ }
        _link = null;
        _linkPort = null;
        WriteLog($"codeblue: link lost: {ex.GetType().Name}: {ex.Message}");

        // Windows reports a vanished USB serial port as "access denied" on the
        // next write, so say what actually happened.
        bool portGone = port is not null &&
            (ex is UnauthorizedAccessException ||
             !SerialPort.GetPortNames().Contains(port, StringComparer.OrdinalIgnoreCase));
        string reason = portGone ? "its serial port went away (cable unplugged?)"
            : ex is TimeoutException ? "it stopped responding"
            : ex.Message;

        if (_videoRunning && port is not null)
        {
            // No dialog: keep the video up and quietly get the board back.
            StartReconnect(port, reason);
            UpdateUi();
            return;
        }
        UpdateUi();
        ShowError($"Lost the board: {reason}.");
    }

    private void StartReconnect(string port, string reason)
    {
        _reconnectPort = port;
        _reconnectReason = reason;
        _reconnectTimer.Start();
    }

    private void StopReconnect()
    {
        _reconnectTimer.Stop();
        _reconnectPort = _reconnectReason = null;
    }

    /// <summary>One reconnect attempt, off the UI thread (the handshake can take two seconds).</summary>
    private void TryReconnect()
    {
        if (_reconnectInFlight || _link is not null || _reconnectPort is not string port)
            return;
        if (!SerialPort.GetPortNames().Contains(port, StringComparer.OrdinalIgnoreCase))
            return; // not back yet

        _reconnectInFlight = true;
        Task.Run(() =>
        {
            try { return (IKvmLink?)KvmLinks.Open(port); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
            {
                return null; // not ready yet; the timer tries again
            }
        }).ContinueWith(t => PostToUi(() =>
        {
            _reconnectInFlight = false;
            IKvmLink? link = t.IsCompletedSuccessfully ? t.Result : null;
            if (link is null) return;
            if (_link is not null || _reconnectPort != port || !_videoRunning)
            {
                link.Dispose(); // the user disconnected or moved on meanwhile
                return;
            }
            StopReconnect();
            InstallLink(link, port);
            WriteLog("codeblue: reconnected");
            UpdateUi();
        }), TaskScheduler.Default);
    }

    /// <summary>
    /// Called on any device arrival/removal. If our COM port is gone (cable pulled,
    /// board lost power), treat it as a link failure right away rather than waiting
    /// for the next write to fail.
    /// </summary>
    private void CheckLinkPort()
    {
        if (_linkPort is null) return;
        if (!SerialPort.GetPortNames().Contains(_linkPort, StringComparer.OrdinalIgnoreCase))
            OnLinkFault(new IOException($"{_linkPort} was removed."));
    }

    private void PostToUi(Action action)
    {
        if (IsHandleCreated && !IsDisposed)
        {
            try { BeginInvoke(action); }
            catch (InvalidOperationException) { /* form is closing */ }
        }
    }

    // ---------------------------------------------------------------- Capture

    private void StartCapture()
    {
        if (_captured || _link is null) return;
        try
        {
            _hook = new KeyboardHook(OnKey);
            RawMouse.Register(Handle);
        }
        catch (Win32Exception ex)
        {
            _hook?.Dispose();
            _hook = null;
            ShowError($"Could not capture input: {ex.Message}");
            return;
        }

        Volatile.Write(ref _uiHeartbeat, Environment.TickCount64);
        _watchdogTripped = false;
        _releaseChord = false;
        _captured = true;
        _link.AbsoluteMouse = WantAbsolute();
        ClipCursorToVideo(center: true);
        if (!_cursorHidden) { Cursor.Hide(); _cursorHidden = true; }
        UpdateUi();
    }

    private void StopCapture()
    {
        if (!_captured) return;
        _captured = false;

        _hook?.Dispose();
        _hook = null;
        try { RawMouse.Unregister(); } catch (Win32Exception) { /* nothing registered */ }

        Cursor.Clip = Rectangle.Empty;
        if (_cursorHidden) { Cursor.Show(); _cursorHidden = false; }

        _held.Clear();
        _swallowUpVk = 0;
        _releaseChord = false;
        _link?.ReleaseAll(); // never leave a key or button stuck down on the target
        UpdateUi();
    }

    /// <summary>
    /// Keeps the hidden local pointer inside the video, so it can never wander
    /// onto another window.
    ///
    /// Relative: pinned to the middle of the video. Raw input keeps reporting
    /// motion, so the target pointer moves while ours stays put.
    ///
    /// Absolute: free to move over the picture, and its position on the picture
    /// is sent to the target, so the target pointer sits exactly where ours is.
    /// </summary>
    /// <summary>
    /// Places the capture border along the video area's edges, above the
    /// picture's window (where there are black bars, it sits in them).
    /// </summary>
    private void LayOutBorder()
    {
        int w = _video.ClientSize.Width, h = _video.ClientSize.Height;
        int t = Math.Max(2, (int)Math.Round(4 * DeviceDpi / 96.0));
        _border[0].Bounds = new Rectangle(0, 0, w, t);
        _border[1].Bounds = new Rectangle(0, h - t, w, t);
        _border[2].Bounds = new Rectangle(0, 0, t, h);
        _border[3].Bounds = new Rectangle(w - t, 0, t, h);
        foreach (var edge in _border) edge.BringToFront(); // over each session's new window
    }

    private void ClipCursorToVideo(bool center = false)
    {
        if (_link is { AbsoluteMouse: true } link)
        {
            var area = _video.RectangleToScreen(Rectangle.Intersect(PictureRect(), _video.ClientRectangle));
            if (area.Width <= 0 || area.Height <= 0) return;
            if (center || !area.Contains(Cursor.Position))
                Cursor.Position = new Point(area.X + area.Width / 2, area.Y + area.Height / 2);
            Cursor.Clip = area;
            SendPointer(link);
        }
        else
        {
            var middle = _video.PointToScreen(new Point(_video.ClientSize.Width / 2, _video.ClientSize.Height / 2));
            Cursor.Position = middle;
            Cursor.Clip = new Rectangle(middle, new Size(1, 1));
        }
    }

    /// <summary>
    /// Where the picture is drawn inside the video control: fitted and centred,
    /// or centred at 1:1 (and possibly larger than the control, i.e. cropped).
    /// </summary>
    private Rectangle PictureRect()
    {
        var c = _video.ClientRectangle;
        if (_videoWidth == 0 || _videoHeight == 0 || c.Width == 0 || c.Height == 0) return c;
        double scale = _settings.NativeScale ? 1.0 : Math.Min((double)c.Width / _videoWidth, (double)c.Height / _videoHeight);
        int w = (int)Math.Round(_videoWidth * scale), h = (int)Math.Round(_videoHeight * scale);
        return new Rectangle((c.Width - w) / 2, (c.Height - h) / 2, w, h);
    }

    /// <summary>Maps a screen point to the target's 0-32767 absolute coordinates.</summary>
    private (int X, int Y) ToTarget(Point screen)
    {
        var p = _video.PointToClient(screen);
        var r = PictureRect();
        int x = r.Width > 1 ? (int)Math.Round((p.X - r.X) * 32767.0 / (r.Width - 1)) : 16384;
        int y = r.Height > 1 ? (int)Math.Round((p.Y - r.Y) * 32767.0 / (r.Height - 1)) : 16384;
        return (Math.Clamp(x, 0, 32767), Math.Clamp(y, 0, 32767));
    }

    /// <summary>Sends the local pointer's position on the picture to the absolute pointer.</summary>
    private void SendPointer(IKvmLink link)
    {
        var (x, y) = ToTarget(Cursor.Position);
        link.MouseMoveAbsolute(x, y);
    }

    /// <summary>Whether the mouse setting, the board and the target call for the absolute pointer.</summary>
    private bool WantAbsolute()
    {
        if (_link is not { SupportsAbsolute: true } link) return false;
        return _settings.MouseMode switch
        {
            MouseAbsolute => true,
            MouseRelative => false,
            // Auto: absolute, unless the target put the mouse in boot protocol
            // (typically BIOS setup screens), which only understands relative,
            // or never reads the absolute pointer (firmware 0.3.0 and later
            // report that). Some firmware tools read it and ignore it, which
            // no device can detect; the status bar shows the mode for those.
            _ => link.TargetStatus is not ({ MouseBoot: true } or { AbsoluteUnread: true }),
        };
    }

    /// <summary>Switches mouse mode mid-capture when the setting or the target changes.</summary>
    private void ApplyMouseMode()
    {
        if (!_captured || _link is null) return;
        bool want = WantAbsolute();
        if (_link.AbsoluteMouse == want) return;
        _link.AbsoluteMouse = want;
        ClipCursorToVideo(center: true);
        WriteLog($"codeblue: mouse now {(want ? "absolute" : "relative")}");
    }

    /// <summary>Runs inside the keyboard hook: keep it fast, and only enqueue.</summary>
    private bool OnKey(KeyEvent ev)
    {
        if (!_captured || ev.Injected || _link is null) return false;

        if (ev.VirtualKey == VK_APPS)
        {
            if (!ev.Up) PostToUi(StopCapture);
            return true;
        }

        // Ctrl+Alt+Del and Win+L belong to Windows: it acts on them whatever the
        // hook returns, but the hook still sees the last key. Forwarding it would
        // send the chord to the target as well (a BIOS reboots on Ctrl+Alt+Del).
        // Keep it local and end capture, since Windows is taking over the screen
        // anyway. The Ctrl+Alt+Del button sends one to the target on purpose.
        if (!ev.Up && IsWindowsReservedChord(ev))
        {
            _releaseChord = false;
            PostToUi(StopCapture);
            return true;
        }

        // Viewer hotkeys while captured: Ctrl+Alt+Enter (full screen), Ctrl+Alt+1 (1:1 pixels),
        // Ctrl+Alt+S (snapshot). Their key-down, auto-repeats and key-up all stay away from the target.
        if (ev.VirtualKey is VK_RETURN or VK_1 or VK_S)
        {
            if (ev.Up && ev.VirtualKey == _swallowUpVk)
            {
                _swallowUpVk = 0;
                return true;
            }
            if (!ev.Up && ev.VirtualKey == _swallowUpVk) return true; // auto-repeat
            if (!ev.Up && _held.Any(HidUsageMap.IsCtrl) && _held.Any(HidUsageMap.IsAlt))
            {
                _releaseChord = false; // Ctrl+Alt was used for something else
                _swallowUpVk = ev.VirtualKey;
                if (ev.VirtualKey == VK_RETURN) PostToUi(ToggleFullscreen);
                else if (ev.VirtualKey == VK_1) PostToUi(ToggleNativeScale);
                else PostToUi(TakeSnapshot);
                return true;
            }
        }

        byte usage = HidUsageMap.Map(ev);
        if (usage == 0) return true; // unknown key: keep it away from the local desktop too

        if (ev.Up)
        {
            // A key that went down before capture started (Ctrl, Alt and C from
            // Ctrl+Alt+C, say) was seen by the host, not the target. Let its
            // release through too, or the host keeps it held down.
            if (!_held.Remove(usage)) return false;
            _link.KeyUp(usage);

            // Ctrl+Alt pressed together and released with no other key in between
            // releases capture (the VMware convention). Every keyboard has it.
            if (_releaseChord && IsCtrlOrAlt(usage))
            {
                _releaseChord = false;
                PostToUi(StopCapture);
            }
        }
        else if (_held.Add(usage))
        {
            // Windows' auto-repeat is dropped; the target generates its own repeats.
            _link.KeyDown(usage);
            _releaseChord = _held.All(IsCtrlOrAlt)
                && _held.Any(HidUsageMap.IsCtrl)
                && _held.Any(HidUsageMap.IsAlt);
        }
        return true;
    }

    private static bool IsCtrlOrAlt(byte usage) => HidUsageMap.IsCtrl(usage) || HidUsageMap.IsAlt(usage);

    private bool IsWindowsReservedChord(KeyEvent ev) =>
        (ev.ScanCode == SC_DELETE && _held.Any(HidUsageMap.IsCtrl) && _held.Any(HidUsageMap.IsAlt))
        || (ev.VirtualKey == VK_L && _held.Any(HidUsageMap.IsGui));

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == NativeMethods.WM_INPUT && _captured && _link is not null &&
            RawMouse.Decode(m.LParam) is RawMouseEvent e)
        {
            HandleMouse(_link, e);
        }
        else if (m.Msg == NativeMethods.WM_DEVICECHANGE)
        {
            if (_linkPort is not null) PostToUi(CheckLinkPort);
            else if (_reconnectPort is not null) PostToUi(TryReconnect); // the board may be back
        }
        base.WndProc(ref m);
    }

    private void HandleMouse(IKvmLink link, RawMouseEvent e)
    {
        if (e.DeltaX != 0 || e.DeltaY != 0)
        {
            if (link.AbsoluteMouse) SendPointer(link);
            else link.MouseMoveRelative(e.DeltaX, e.DeltaY);
        }

        ushort f = e.ButtonFlags;
        if ((f & NativeMethods.RI_MOUSE_LEFT_BUTTON_DOWN) != 0) link.MouseButton(TargetMouseButton.Left, true);
        if ((f & NativeMethods.RI_MOUSE_LEFT_BUTTON_UP) != 0) link.MouseButton(TargetMouseButton.Left, false);
        if ((f & NativeMethods.RI_MOUSE_RIGHT_BUTTON_DOWN) != 0) link.MouseButton(TargetMouseButton.Right, true);
        if ((f & NativeMethods.RI_MOUSE_RIGHT_BUTTON_UP) != 0) link.MouseButton(TargetMouseButton.Right, false);
        if ((f & NativeMethods.RI_MOUSE_MIDDLE_BUTTON_DOWN) != 0) link.MouseButton(TargetMouseButton.Middle, true);
        if ((f & NativeMethods.RI_MOUSE_MIDDLE_BUTTON_UP) != 0) link.MouseButton(TargetMouseButton.Middle, false);

        if (e.WheelDelta != 0) link.MouseWheel(e.WheelDelta / 120);
    }

    private bool _sendingCad;

    // The board sends the keyboard's current state when the target polls, so
    // changes closer together than a poll merge, and a press released before
    // the next poll is never seen. Space the steps out and hold the chord.
    private async void SendCtrlAltDel()
    {
        var link = _link;
        if (link is null || _sendingCad) return;
        _sendingCad = true;
        try
        {
            link.KeyDown(HidUsageMap.LeftCtrl);
            await Task.Delay(30);
            link.KeyDown(HidUsageMap.LeftAlt);
            await Task.Delay(30);
            link.KeyDown(HidUsageMap.Delete);
            await Task.Delay(100);
            link.KeyUp(HidUsageMap.Delete);
            await Task.Delay(30);
            link.KeyUp(HidUsageMap.LeftAlt);
            await Task.Delay(30);
            link.KeyUp(HidUsageMap.LeftCtrl);
        }
        finally
        {
            _sendingCad = false;
        }
    }

    // ---------------------------------------------------------------- View

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.F11:
                ToggleFullscreen();
                return true;
            case Keys.Escape when _fullscreen:
                ToggleFullscreen();
                return true;
            case Keys.Control | Keys.Alt | Keys.D1:
                ToggleNativeScale();
                return true;
            case Keys.Control | Keys.Alt | Keys.S:
                TakeSnapshot();
                return true;
            case Keys.Control | Keys.Alt | Keys.C:
                StartCapture(); // does nothing without a keyboard/mouse link
                return true;
            case Keys.F12:
                TakeSnapshot();
                return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void ToggleFullscreen()
    {
        if (!_fullscreen)
        {
            _savedState = WindowState;
            _savedBounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            _toolbar.Visible = false;
            _statusBar.Visible = false;
            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Normal;
            Bounds = Screen.FromControl(this).Bounds;
            _fullscreen = true;
        }
        else
        {
            FormBorderStyle = FormBorderStyle.Sizable;
            Bounds = _savedBounds;
            WindowState = _savedState;
            _toolbar.Visible = true;
            _statusBar.Visible = true;
            _fullscreen = false;
        }
        if (_captured) PostToUi(() => ClipCursorToVideo());
    }

    private void TakeSnapshot()
    {
        if (_session is not { } session) return;
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "CodeBlue");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, $"codeblue-{DateTime.Now:yyyyMMdd-HHmmss}.png");
        // libVLC waits for a frame to save; with a stalled dongle, that's not worth freezing for.
        Task.Run(() => session.TakeSnapshot(path)).ContinueWith(t => PostToUi(() =>
            ShowTransient(t is { IsCompletedSuccessfully: true, Result: true }
                ? $"Snapshot saved: {path}"
                : "Snapshot failed.")), TaskScheduler.Default);
    }

    // ---------------------------------------------------------------- Watchdog

    private void WatchdogLoop()
    {
        while (!_closing)
        {
            Thread.Sleep(500);
            if (!_captured || _watchdogTripped) continue;

            long silentMs = Environment.TickCount64 - Volatile.Read(ref _uiHeartbeat);
            if (silentMs > WatchdogTimeoutMs)
            {
                _watchdogTripped = true;
                NativeMethods.ClipCursor(IntPtr.Zero);
                WriteLog($"codeblue: UI unresponsive for {silentMs} ms while captured; cursor released");
            }
        }
    }

    // ---------------------------------------------------------------- libVLC log

    /// <summary>Opens a fresh libVLC log for this run and hooks libVLC's log output to it.</summary>
    private void OpenVlcLog()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(VlcLogPath)!);
            _vlcLog = new StreamWriter(VlcLogPath, append: false) { AutoFlush = true };
            WriteLog($"codeblue: viewer {ViewerVersion} started, libVLC {_libVlc.Version}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _vlcLog = null; // logging is a convenience; run without it
        }
        _libVlc.Log += OnVlcLog;
    }

    private void OnVlcLog(object? sender, LogEventArgs e)
    {
        WriteLog($"{e.Level,-7} {e.Module}: {e.Message}", e.Level);

        // The usual cause of a black screen after changing Format, Size or FPS:
        // the device doesn't offer that combination (YUY2 at 1080p is often
        // only 5 or 10 fps). Remember it, so the failure can say so plainly.
        if (e.Module == "dshow" && e.Message.Contains("does not support required parameters", StringComparison.Ordinal))
            _captureModeUnsupported = true;

        if (e.Level == LogLevel.Error && !IsRoutineVlcError(e.Message))
        {
            // libVLC can emit errors in bursts; update the status bar at most twice a second.
            long now = Environment.TickCount64;
            if (now - Interlocked.Read(ref _lastErrorPost) > 500)
            {
                Interlocked.Exchange(ref _lastErrorPost, now);
                string text = $"Video error: {e.Module}: {e.Message} (details in {VlcLogPath})";
                PostToUi(() => SetStatus(text));
            }
        }
    }

    // libVLC logs these at error level, but they are routine on live capture: they
    // only mean the stream was briefly late and libVLC adjusted its buffering.
    // They still go to the log; they just don't belong in the status bar.
    private static readonly string[] RoutineVlcErrors =
    [
        "is called too late", // ES_OUT_SET_(GROUP_)PCR ... (pts_delay increased to N ms)
        "Timestamp conversion failed", // uncompressed (YUY2) frames arrive without a stream clock
        "Could not convert timestamp",
        "SetThumbNailClip failed", // the taskbar thumbnail, while switching full screen
        "does not exist", // "option ... does not exist": settings of plugins the viewer doesn't ship
    ];

    /// <summary>Status text for a device that doesn't offer the requested format, size and frame rate.</summary>
    private string UnsupportedModeText()
    {
        var o = _lastCaptureOptions;
        var asked = new List<string>();
        if (o is not null)
        {
            if (o.Format != CaptureOptions.Auto) asked.Add(o.Format);
            if (o.Size != CaptureOptions.Auto) asked.Add(o.Size);
            if (o.Fps != CaptureOptions.Auto) asked.Add($"{o.Fps} fps");
        }
        string what = asked.Count > 0 ? $" ({string.Join(", ", asked)})" : "";
        return $"The capture device doesn't offer that format, size and frame rate{what}. " +
               "Set FPS to Auto, or try a smaller size or MJPG.";
    }

    private static bool IsRoutineVlcError(string message) =>
        RoutineVlcErrors.Any(r => message.Contains(r, StringComparison.Ordinal));

    /// <summary>Thread-safe; called from libVLC threads as well as the UI thread.</summary>
    private void WriteLog(string message, LogLevel level = LogLevel.Notice)
    {
        string line = $"{DateTime.Now:HH:mm:ss.fff} {message}";
        lock (_vlcLogLock)
        {
            if (_vlcLog is null) return;
            if (_vlcLogBytes > MaxVlcLogBytes && level < LogLevel.Warning) return;
            _vlcLog.WriteLine(line);
            _vlcLogBytes += line.Length + 2;
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _closing = true;
        _heartbeatTimer.Dispose();
        _reconnectTimer.Dispose();
        _fpsTimer.Dispose();
        _keepAwakeTimer.Dispose();
        // Disconnect (on closing) started the stop. Give it a moment; if libVLC is
        // stuck, leave it: releasing libVLC would wait on the stuck player forever,
        // and the process ending takes care of it anyway.
        if (Task.WaitAll([.. _stopping.Select(s => s.Stopped)], 1500))
        {
            _libVlc.Log -= OnVlcLog;
            _libVlc.Dispose();
        }
        else
        {
            WriteLog("codeblue: exiting with libVLC still stopping");
        }
        lock (_vlcLogLock)
        {
            _vlcLog?.Dispose();
            _vlcLog = null;
        }
        base.OnFormClosed(e);
    }
}
