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
    private readonly StatusStrip _statusStrip;
    private readonly ToolStripStatusLabel _status;

    private IKvmLink? _link;
    private string? _linkPort;
    private bool _videoRunning;

    private KeyboardHook? _hook;
    private bool _captured;
    private bool _cursorHidden;
    private bool _swallowEnterUp;
    private readonly HashSet<byte> _held = [];

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

        _libVlc = new LibVLC("--no-video-title-show", "--no-snapshot-preview");
        _player = new MediaPlayer(_libVlc)
        {
            // Input belongs to us, not to VLC's own hotkeys and mouse handling.
            EnableKeyInput = false,
            EnableMouseInput = false,
        };
        _video = new VideoView { Dock = DockStyle.Fill, BackColor = Color.Black, MediaPlayer = _player };
        _video.MouseDown += (_, _) => StartCapture();
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

        _status = new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        _statusStrip = new StatusStrip();
        _statusStrip.Items.Add(_status);

        // Dock order: the fill control is added first so the bars claim their edges.
        Controls.Add(_video);
        Controls.Add(_toolbar);
        Controls.Add(_statusStrip);

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
        foreach (Control c in new Control[] { _portBox, _protocolBox, _deviceBox, _formatBox, _sizeBox, _fpsBox, _cachingBox })
            c.Enabled = !running;

        _captureButton.Enabled = _link is not null && !_captured;
        _cadButton.Enabled = _link is not null;

        _status.Text = _captured
            ? "Captured: keyboard and mouse go to the target. Menu key releases; Ctrl+Alt+Enter toggles full screen."
            : !running
                ? "Not connected. F11: full screen, F12: snapshot."
                : _link is null
                    ? $"Showing {_deviceBox.Text}. No keyboard/mouse link."
                    : $"Showing {_deviceBox.Text}. Link: {_link.ProtocolName} on {_linkPort}. Click the video or Capture to take control.";
    }

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

        using (var media = new Media(_libVlc, "dshow://", FromType.FromLocation, options.ToVlcOptions()))
            _player.Play(media);
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
        }
        else if (_held.Add(usage))
        {
            // Windows' auto-repeat is dropped; the target generates its own repeats.
            _link.KeyDown(usage);
        }
        return true;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == NativeMethods.WM_INPUT && _captured && _link is not null &&
            RawMouse.Decode(m.LParam) is RawMouseEvent e)
        {
            HandleMouse(_link, e);
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
            _statusStrip.Visible = false;
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
            _statusStrip.Visible = true;
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

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _video.MediaPlayer = null;
        _player.Dispose();
        _libVlc.Dispose();
        base.OnFormClosed(e);
    }
}
