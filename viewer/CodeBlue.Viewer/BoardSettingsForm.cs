using CodeBlue.Viewer.Link;

namespace CodeBlue.Viewer;

/// <summary>
/// Settings that live on the board: the status LED's pin and brightness, and the
/// VBUS sense pin. The board applies each change at once and keeps it across resets.
/// </summary>
internal sealed class BoardSettingsForm : Form
{
    private readonly IKvmLink _link;
    private readonly ComboBox _presetBox;
    private readonly NumericUpDown _ledPinBox, _ledPercentBox, _vbusPinBox;
    private readonly RadioButton _vbusNo, _vbusYes;
    private readonly Label _note, _ledPinLabel, _vbusPinLabel;
    private readonly string? _rememberedPreset;
    private readonly Button _ok;
    private readonly int _openingPercent;
    // The pins in effect on the board; a board with no LED pin counts as having the one shown.
    private short _boardLed, _boardVbus;
    private bool _pinsLoaded;
    private bool _syncing; // set while the dialog fills in fields itself
    private bool _busy;

    /// <summary>The LED brightness chosen (and, after OK, saved on the board).</summary>
    public int LedPercent => (int)_ledPercentBox.Value;

    /// <summary>The preset chosen, or null if the board's pins couldn't be read (so nothing was chosen).</summary>
    public string? PresetName => _pinsLoaded ? _presetBox.SelectedItem?.ToString() : null;

    public BoardSettingsForm(Form owner, IKvmLink link, string? rememberedPreset)
    {
        _link = link;
        _rememberedPreset = rememberedPreset;
        DialogLayout.SetUp(this, owner, "Board settings");

        _presetBox = DialogLayout.Combo(Font, [.. BoardPresets.All, BoardPresets.Custom]);
        _ledPinBox = PinBox();
        _ledPercentBox = new NumericUpDown { Minimum = 0, Maximum = 100, Increment = 5, Width = 70 };
        _vbusNo = new RadioButton { Text = "No", AutoSize = true, Checked = true };
        _vbusYes = new RadioButton { Text = "Yes", AutoSize = true, Margin = new Padding(12, 0, 0, 0) };
        var vbusChoice = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        vbusChoice.Controls.AddRange([_vbusNo, _vbusYes]);
        _vbusPinBox = PinBox();

        var grid = DialogLayout.Grid();
        DialogLayout.AddNote(grid, $"Firmware: {link.ProtocolName}", wrap: false).ForeColor = SystemColors.ControlText;
        DialogLayout.AddHeading(grid, "Preset", Font);
        DialogLayout.AddRow(grid, "Board", _presetBox);
        DialogLayout.AddHeading(grid, "Status LED", Font);
        _ledPinLabel = DialogLayout.AddRow(grid, "GPIO", _ledPinBox);
        DialogLayout.AddRow(grid, "Brightness %", _ledPercentBox);
        DialogLayout.AddHeading(grid, "VBUS sense mod", Font);
        DialogLayout.AddRow(grid, "Present", vbusChoice);
        _vbusPinLabel = DialogLayout.AddRow(grid, "GPIO", _vbusPinBox);
        _note = DialogLayout.AddNote(grid);

        _openingPercent = Math.Clamp(link.LedBrightness ?? 5, 0, 100);
        _ledPercentBox.Value = _openingPercent;
        SetPinsEnabled(false);

        _presetBox.SelectedIndexChanged += (_, _) => OnPresetChosen();
        _vbusYes.CheckedChanged += (_, _) => _vbusPinBox.Enabled = _pinsLoaded && _vbusYes.Checked;
        // Preview the brightness as it changes; OK saves it, Cancel puts it back.
        _ledPercentBox.ValueChanged += (_, _) => SetBrightness(LedPercent, persist: false);

        _ok = DialogLayout.AddButtons(this, grid);
        _ok.Click += async (_, _) => await ApplyAsync();
    }

    private static NumericUpDown PinBox() => new() { Minimum = 0, Maximum = 48, Width = 70 };

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (!_link.SupportsSettings)
        {
            _note.Text = "Changing pins needs firmware 0.3.0 or later; update it from the web installer.";
            return;
        }
        _note.Text = "Reading the board's settings...";
        try
        {
            var led = await _link.GetSettingAsync(BoardSetting.LedGpio);
            var vbus = await _link.GetSettingAsync(BoardSetting.VbusGpio);
            ShowPins(led.Value, vbus.Value);
            _pinsLoaded = true;
            SetPinsEnabled(true);
            _note.Text = "";
        }
        catch (Exception ex)
        {
            _note.ForeColor = Color.Firebrick;
            _note.Text = $"Couldn't read the board's pins: {ex.Message}";
        }
    }

    /// <summary>
    /// Shows the board's pins, and the preset they came from: the one last chosen
    /// here, else the one they match, else Custom.
    /// </summary>
    private void ShowPins(short led, short vbus)
    {
        _boardVbus = vbus;
        var preset = BoardPresets.All.FirstOrDefault(p => p.Name == _rememberedPreset);
        if (preset is null && _rememberedPreset != BoardPresets.Custom)
            preset = BoardPresets.Match(led, vbus);
        _syncing = true;
        // A board with no LED pin shows its preset's (or the first preset's) pin;
        // nothing is sent unless it's changed.
        _ledPinBox.Value = led >= 0 ? led : (preset ?? BoardPresets.All[0]).LedGpio;
        _vbusYes.Checked = vbus >= 0;
        _vbusNo.Checked = vbus < 0;
        _vbusPinBox.Value = vbus >= 0 ? vbus : (preset ?? BoardPresets.All[0]).VbusGpio;
        _boardLed = (short)_ledPinBox.Value;
        SelectPreset(preset);
        _syncing = false;
    }

    private void SelectPreset(BoardPreset? preset)
    {
        int i = preset is null ? _presetBox.Items.Count - 1 : _presetBox.Items.IndexOf(preset);
        bool wasSyncing = _syncing;
        _syncing = true;
        _presetBox.SelectedIndex = i;
        _syncing = wasSyncing;
    }

    /// <summary>
    /// Shows the preset's pins beside the fields. Chosen by hand, a preset also
    /// fills them in; Custom leaves them as they are.
    /// </summary>
    private void OnPresetChosen()
    {
        var preset = _presetBox.SelectedItem as BoardPreset;
        _ledPinLabel.Text = preset is null ? "GPIO" : $"GPIO (default: {preset.LedGpio})";
        _vbusPinLabel.Text = preset is null ? "GPIO" : $"GPIO (default: {preset.VbusGpio})";
        if (_syncing || preset is null) return;
        _ledPinBox.Value = preset.LedGpio;
        _vbusPinBox.Value = preset.VbusGpio;
    }

    private short WantedVbus => _vbusYes.Checked ? (short)_vbusPinBox.Value : SettingReply.None;

    private void SetPinsEnabled(bool enabled)
    {
        foreach (Control c in new Control[] { _presetBox, _ledPinBox, _vbusNo, _vbusYes })
            c.Enabled = enabled;
        _vbusPinBox.Enabled = enabled && _vbusYes.Checked;
    }

    private async Task ApplyAsync()
    {
        if (_busy) return;
        _busy = true;
        UseWaitCursor = true;
        _note.ForeColor = SystemColors.GrayText;
        _note.Text = "Saving...";
        SetPinsEnabled(false);
        _ledPercentBox.Enabled = _ok.Enabled = false;
        try
        {
            if (_pinsLoaded && !await ApplyPinsAsync()) return;
            if (LedPercent != _openingPercent) _link.SetLedBrightness(LedPercent, persist: true);
            _note.Text = "";
            DialogResult = DialogResult.OK;
        }
        catch (Exception ex) // the link can fail in many ways; none should take the viewer down
        {
            _note.ForeColor = Color.Firebrick;
            _note.Text = $"Couldn't change the board's pins: {ex.Message}";
        }
        finally
        {
            _busy = false;
            UseWaitCursor = false;
            if (DialogResult != DialogResult.OK)
            {
                SetPinsEnabled(_pinsLoaded);
                _ledPercentBox.Enabled = _ok.Enabled = true;
            }
        }
    }

    /// <summary>Sends the pins that changed; false (with the reason shown) if the board refused one.</summary>
    private async Task<bool> ApplyPinsAsync()
    {
        short led = (short)_ledPinBox.Value, vbus = WantedVbus;
        var changes = new List<(BoardSetting Setting, short Value)>();
        if (led != _boardLed) changes.Add((BoardSetting.LedGpio, led));
        if (vbus != _boardVbus) changes.Add((BoardSetting.VbusGpio, vbus));
        // The two can't share a pin, so free the pin the LED is moving to first.
        if (changes.Count == 2 && led == _boardVbus) changes.Reverse();

        foreach (var (setting, value) in changes)
        {
            var reply = await _link.SetSettingAsync(setting, value);
            if (setting == BoardSetting.LedGpio) _boardLed = reply.Value;
            else _boardVbus = reply.Value;
            if (reply.Result != SettingResult.Ok)
            {
                _note.Text = Refusal(setting, value, reply.Result);
                _note.ForeColor = Color.Firebrick;
                return false;
            }
        }
        return true;
    }

    private static string Refusal(BoardSetting setting, short pin, SettingResult result) => result switch
    {
        SettingResult.BadPin when setting == BoardSetting.VbusGpio && pin is 47 or 48 =>
            $"GPIO {pin} can't sense VBUS: on some modules it runs at 1.8 V, too low for the sense divider.",
        SettingResult.BadPin =>
            $"GPIO {pin} can't be used: it's a strapping, USB, UART, or flash/PSRAM pin, or doesn't exist.",
        SettingResult.Conflict => $"The LED and VBUS sense can't share GPIO {pin}.",
        SettingResult.ApplyFailed => "The board couldn't apply or save that setting.",
        _ => "This firmware doesn't know that setting.",
    };

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_busy)
        {
            e.Cancel = true; // let the board answer first
            return;
        }
        // Without OK, the board goes back to its saved brightness.
        if (DialogResult != DialogResult.OK && LedPercent != _openingPercent)
            SetBrightness(_openingPercent, persist: false);
        base.OnFormClosing(e);
    }

    /// <summary>For previews: if the link has dropped meanwhile, there's no LED to preview on.</summary>
    private void SetBrightness(int percent, bool persist)
    {
        try { _link.SetLedBrightness(percent, persist); }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException) { }
    }
}
