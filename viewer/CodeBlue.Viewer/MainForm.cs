using System.ComponentModel;
using System.IO.Ports;
using CodeBlue.Viewer.Input;
using CodeBlue.Viewer.Link;
using CodeBlue.Viewer.Native;
using CodeBlue.Viewer.Video;
using LibVLCSharp.Shared;
using LibVLCSharp.WinForms;

namespace CodeBlue.Viewer;

internal sealed class MainForm : Form
{
    private const uint VK_RETURN = 0x0D;
    private const uint VK_APPS = 0x5D; // Menu key: releases capture

    private const string ReleaseHint =
        "Keyboard and mouse are going to the target. To release: press and release Ctrl+Alt (or the Menu key).";

    // If the UI thread stops responding this long while captured, the watchdog frees the cursor.
    private const long WatchdogTimeoutMs = 3000;

    // libVLC debug output is capped; warnings and errors are always written.
    private const long MaxVlcLogBytes = 20L * 1024 * 1024;

    private static readonly string VlcLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodeBlue", "libvlc.log");

    // Full-scale colours; SetLed() scales them by the LED % setting.
    private static readonly (byte R, byte G, byte B) LedConnected = (0, 255, 0);
    private static readonly (byte R, byte G, byte B) LedIdle = (0, 0, 255);

    private readonly ViewerSettings _settings = ViewerSettings.Load();

    private readonly LibVLC _libVlc;
    private readonly MediaPlayer _player;
    private readonly VideoView _video;

    private readonly FlowLayoutPanel _toolbar;
    private readonly ComboBox _portBox, _protocolBox, _deviceBox, _formatBox, _sizeBox, _fpsBox;
    private readonly NumericUpDown _cachingBox, _ledBox;
    private readonly Button _connectButton, _captureButton, _cadButton;
    private readonly Label _status;
    private readonly Label _banner;

    private IKvmLink? _link;
    private string? _linkPort;
    private bool _videoRunning;

    private KeyboardHook? _hook;
    private volatile bool _captured; // read by the watchdog thread
    private bool _cursorHidden;
    private bool _swallowEnterUp;
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

    private bool _fullscreen;
    private FormWindowState _savedState;
    private Rectangle _savedBounds;

    public MainForm()
    {
        Text = "CodeBlue";
        Font = new Font("Segoe UI", 10f);
        ClientSize = new Size(1280, 800);
        MinimumSize = new Size(800, 500);
        KeyPreview = true;

        _libVlc = new LibVLC("--no-video-title-show", "--no-snapshot-preview", "--verbose=2");
        OpenVlcLog();
        _player = new MediaPlayer(_libVlc)
        {
            // Input belongs to us, not to VLC's own hotkeys and mouse handling.
            EnableKeyInput = false,
            EnableMouseInput = false,
        };
        _player.Playing += (_, _) => WriteLog("player: playing");
        _player.Vout += (_, e) => WriteLog($"player: video outputs = {e.Count}");
        _player.EncounteredError += (_, _) =>
        {
            WriteLog("player: encountered error");
            PostToUi(() => SetStatus($"Video playback failed. Details in {VlcLogPath}"));
        };

        // The player is attached in OnShown, once this control has its final window handle.
        _video = new VideoView { Dock = DockStyle.Fill, BackColor = Color.Black };
        _video.Resize += (_, _) => { if (_captured) ClipCursorToVideo(); };

        _connectButton = MakeButton("Connect", (_, _) => ToggleConnect());
        _portBox = MakeCombo(110);
        var refreshButton = MakeButton("Refresh", (_, _) => { RefreshPorts(); RefreshDevices(); });
        _protocolBox = MakeCombo(190);
        _protocolBox.Items.AddRange([.. KvmLinks.Available]);
        _deviceBox = MakeCombo(210, editable: true);
        _formatBox = MakeCombo(80);
        _formatBox.Items.AddRange(CaptureOptions.Formats);
        _sizeBox = MakeCombo(110);
        _sizeBox.Items.AddRange(CaptureOptions.Sizes);
        _fpsBox = MakeCombo(70);
        _fpsBox.Items.AddRange(CaptureOptions.FrameRates);
        _cachingBox = new NumericUpDown
        {
            Minimum = 0, Maximum = 2000, Increment = 10, Width = 70,
            Margin = new Padding(2, 4, 2, 4),
        };
        _ledBox = new NumericUpDown
        {
            Minimum = 0, Maximum = 100, Increment = 5, Width = 60,
            Margin = new Padding(2, 4, 2, 4),
        };
        _captureButton = MakeButton("Capture", (_, _) => StartCapture());
        _cadButton = MakeButton("Ctrl+Alt+Del", (_, _) => SendCtrlAltDel());
        var snapshotButton = MakeButton("Snapshot", (_, _) => TakeSnapshot());
        var fullscreenButton = MakeButton("Full screen", (_, _) => ToggleFullscreen());

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
            MakeLabel("Port"), _portBox, refreshButton,
            MakeLabel("Protocol"), _protocolBox,
            MakeLabel("Video"), _deviceBox,
            MakeLabel("Format"), _formatBox,
            MakeLabel("Size"), _sizeBox,
            MakeLabel("FPS"), _fpsBox,
            MakeLabel("Cache ms"), _cachingBox,
            MakeLabel("LED %"), _ledBox,
            _captureButton, _cadButton, snapshotButton, fullscreenButton,
        ]);

        _status = new Label
        {
            Dock = DockStyle.Bottom,
            AutoSize = false,
            Height = Font.Height + 12,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(6, 0, 6, 0),
            AutoEllipsis = true,
        };
        _banner = new Label
        {
            Dock = DockStyle.Top,
            AutoSize = false,
            Height = Font.Height + 14,
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.FromArgb(255, 193, 7),
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
        Controls.Add(_status);

        RefreshPorts();
        RefreshDevices();
        SelectItem(_protocolBox, _settings.Protocol);
        SelectItem(_formatBox, _settings.Format);
        SelectItem(_sizeBox, _settings.Size);
        SelectItem(_fpsBox, _settings.Fps);
        _cachingBox.Value = Math.Clamp(_settings.CachingMs, 0, 2000);
        _ledBox.Value = Math.Clamp(_settings.LedBrightnessPercent, 0, 100);
        _ledBox.ValueChanged += (_, _) => SetLed(LedConnected); // live preview while connected

        Deactivate += (_, _) => StopCapture();
        FormClosing += (_, _) => { Disconnect(); SaveSettings(); };

        // Dead-man's switch for a hung UI thread: the timer proves the UI is alive,
        // and the watchdog thread frees the cursor if it stops ticking during capture.
        // (Windows itself drops a low-level keyboard hook that stops responding.)
        _heartbeatTimer = new System.Windows.Forms.Timer { Interval = 250 };
        _heartbeatTimer.Tick += (_, _) => Volatile.Write(ref _uiHeartbeat, Environment.TickCount64);
        _heartbeatTimer.Start();
        new Thread(WatchdogLoop) { IsBackground = true, Name = "CodeBlue capture watchdog" }.Start();

        UpdateUi();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _video.MediaPlayer = _player;
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
        foreach (Control c in new Control[] { _portBox, _protocolBox, _deviceBox, _formatBox, _sizeBox, _fpsBox, _cachingBox })
            c.Enabled = !running;

        _captureButton.Enabled = _link is not null && !_captured;
        _cadButton.Enabled = _link is not null;
        _banner.Visible = _captured;

        SetStatus(_captured
            ? "Captured. Release: Ctrl+Alt or Menu key. Full screen: Ctrl+Alt+Enter."
            : !running
                ? "Not connected. F11: full screen, F12: snapshot."
                : _link is null
                    ? $"Showing {_deviceBox.Text}. No keyboard/mouse link."
                    : $"Showing {_deviceBox.Text}. Link: {_link.ProtocolName} on {_linkPort}. Click Capture to take control.");
    }

    private void SetStatus(string text) => _status.Text = text;

    private void ShowError(string message) =>
        MessageBox.Show(this, message, "CodeBlue", MessageBoxButtons.OK, MessageBoxIcon.Warning);

    private void SaveSettings()
    {
        _settings.SerialPort = _portBox.SelectedItem as string ?? _settings.SerialPort;
        _settings.VideoDevice = _deviceBox.Text.Length > 0 ? _deviceBox.Text : _settings.VideoDevice;
        _settings.Protocol = _protocolBox.SelectedItem as string;
        _settings.Format = _formatBox.SelectedItem as string ?? CaptureOptions.Auto;
        _settings.Size = _sizeBox.SelectedItem as string ?? CaptureOptions.Auto;
        _settings.Fps = _fpsBox.SelectedItem as string ?? CaptureOptions.Auto;
        _settings.CachingMs = (int)_cachingBox.Value;
        _settings.LedBrightnessPercent = (int)_ledBox.Value;
        _settings.Save();
    }

    // ---------------------------------------------------------------- Connect

    private void ToggleConnect()
    {
        if (_videoRunning || _link is not null) Disconnect();
        else Connect();
    }

    private void Connect()
    {
        string device = _deviceBox.Text.Trim();
        if (device.Length == 0)
        {
            ShowError("Pick a video capture device first.");
            return;
        }

        var options = new CaptureOptions(
            device,
            _formatBox.SelectedItem as string ?? CaptureOptions.Auto,
            _sizeBox.SelectedItem as string ?? CaptureOptions.Auto,
            _fpsBox.SelectedItem as string ?? CaptureOptions.Auto,
            (int)_cachingBox.Value);

        // Always point libVLC at the video control's current window handle.
        _player.Hwnd = _video.Handle;

        string[] vlcOptions = options.ToVlcOptions();
        WriteLog($"codeblue: opening dshow:// {string.Join(' ', vlcOptions)}");
        bool started;
        using (var media = new Media(_libVlc, "dshow://", FromType.FromLocation, vlcOptions))
            started = _player.Play(media);
        WriteLog($"codeblue: Play() returned {started}");
        _videoRunning = true;

        string protocol = _protocolBox.SelectedItem as string ?? KvmLinks.VideoOnly;
        string? port = _portBox.SelectedItem as string;
        if (protocol != KvmLinks.VideoOnly)
        {
            if (port is null)
            {
                ShowError("No serial port selected, so this session is video only.");
            }
            else
            {
                try
                {
                    _link = KvmLinks.Open(protocol, port);
                    if (_link is not null)
                    {
                        _linkPort = port;
                        _link.Faulted += ex => PostToUi(() => OnLinkFault(ex));
                        SetLed(LedConnected);
                    }
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
        if (!started) SetStatus($"Video could not start. Details in {VlcLogPath}");
    }

    private void Disconnect()
    {
        StopCapture();
        if (_link is not null)
        {
            _link.ReleaseAll();
            SetLed(LedIdle);
            _link.Dispose(); // drains the queue, so the two commands above go out first
            _link = null;
            _linkPort = null;
        }
        if (_videoRunning)
        {
            _player.Stop();
            _videoRunning = false;
        }
        UpdateUi();
    }

    /// <summary>Sends a status colour scaled by the LED % setting (0 turns the LED off).</summary>
    private void SetLed((byte R, byte G, byte B) color)
    {
        if (_link is null) return;
        int percent = (int)_ledBox.Value;
        byte Scale(byte v) => (byte)((v * percent + 50) / 100);
        _link.SetStatusLed(Scale(color.R), Scale(color.G), Scale(color.B));
    }

    private void OnLinkFault(Exception ex)
    {
        if (_link is null) return;
        StopCapture();
        try { _link.Dispose(); } catch { /* already broken */ }
        _link = null;
        _linkPort = null;
        UpdateUi();
        ShowError($"The serial link was lost: {ex.Message}\n\nThe video is still running. Disconnect and reconnect to try again.");
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
        ClipCursorToVideo();
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
        _swallowEnterUp = false;
        _releaseChord = false;
        _link?.ReleaseAll(); // never leave a key or button stuck down on the target
        UpdateUi();
    }

    /// <summary>
    /// Pins the hidden local pointer to the middle of the video. Raw input keeps
    /// reporting motion, so the target pointer moves while ours stays put and
    /// can never wander onto another window.
    /// </summary>
    private void ClipCursorToVideo()
    {
        var center = _video.PointToScreen(new Point(_video.ClientSize.Width / 2, _video.ClientSize.Height / 2));
        Cursor.Position = center;
        Cursor.Clip = new Rectangle(center, new Size(1, 1));
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

        if (ev.VirtualKey == VK_RETURN)
        {
            if (!ev.Up && _held.Any(HidUsageMap.IsCtrl) && _held.Any(HidUsageMap.IsAlt))
            {
                _releaseChord = false; // Ctrl+Alt was used for something else
                _swallowEnterUp = true;
                PostToUi(ToggleFullscreen);
                return true;
            }
            if (ev.Up && _swallowEnterUp)
            {
                _swallowEnterUp = false;
                return true;
            }
        }

        byte usage = HidUsageMap.Map(ev);
        if (usage == 0) return true; // unknown key: keep it away from the local desktop too

        if (ev.Up)
        {
            if (_held.Remove(usage)) _link.KeyUp(usage);

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

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == NativeMethods.WM_INPUT && _captured && _link is not null &&
            RawMouse.Decode(m.LParam) is RawMouseEvent e)
        {
            HandleMouse(_link, e);
        }
        else if (m.Msg == NativeMethods.WM_DEVICECHANGE && _linkPort is not null)
        {
            PostToUi(CheckLinkPort);
        }
        base.WndProc(ref m);
    }

    private static void HandleMouse(IKvmLink link, RawMouseEvent e)
    {
        if (e.DeltaX != 0 || e.DeltaY != 0) link.MouseMoveRelative(e.DeltaX, e.DeltaY);

        ushort f = e.ButtonFlags;
        if ((f & NativeMethods.RI_MOUSE_LEFT_BUTTON_DOWN) != 0) link.MouseButton(TargetMouseButton.Left, true);
        if ((f & NativeMethods.RI_MOUSE_LEFT_BUTTON_UP) != 0) link.MouseButton(TargetMouseButton.Left, false);
        if ((f & NativeMethods.RI_MOUSE_RIGHT_BUTTON_DOWN) != 0) link.MouseButton(TargetMouseButton.Right, true);
        if ((f & NativeMethods.RI_MOUSE_RIGHT_BUTTON_UP) != 0) link.MouseButton(TargetMouseButton.Right, false);
        if ((f & NativeMethods.RI_MOUSE_MIDDLE_BUTTON_DOWN) != 0) link.MouseButton(TargetMouseButton.Middle, true);
        if ((f & NativeMethods.RI_MOUSE_MIDDLE_BUTTON_UP) != 0) link.MouseButton(TargetMouseButton.Middle, false);

        if (e.WheelDelta != 0) link.MouseWheel(e.WheelDelta / 120);
    }

    private void SendCtrlAltDel()
    {
        if (_link is null) return;
        _link.KeyDown(HidUsageMap.LeftCtrl);
        _link.KeyDown(HidUsageMap.LeftAlt);
        _link.KeyDown(HidUsageMap.Delete);
        _link.KeyUp(HidUsageMap.Delete);
        _link.KeyUp(HidUsageMap.LeftAlt);
        _link.KeyUp(HidUsageMap.LeftCtrl);
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
            _status.Visible = false;
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
            _status.Visible = true;
            _fullscreen = false;
        }
        if (_captured) PostToUi(ClipCursorToVideo);
    }

    private void TakeSnapshot()
    {
        if (!_videoRunning) return;
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "CodeBlue");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, $"codeblue-{DateTime.Now:yyyyMMdd-HHmmss}.png");
        _status.Text = _player.TakeSnapshot(0, path, 0, 0)
            ? $"Snapshot saved: {path}"
            : "Snapshot failed.";
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
            WriteLog($"codeblue: viewer started, libVLC {_libVlc.Version}");
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

        if (e.Level == LogLevel.Error)
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
        _video.MediaPlayer = null;
        _player.Dispose();
        _libVlc.Log -= OnVlcLog;
        _libVlc.Dispose();
        lock (_vlcLogLock)
        {
            _vlcLog?.Dispose();
            _vlcLog = null;
        }
        base.OnFormClosed(e);
    }
}
