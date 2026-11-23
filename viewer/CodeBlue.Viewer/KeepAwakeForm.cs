using CodeBlue.Viewer.Link;

namespace CodeBlue.Viewer;

/// <summary>
/// How keep awake behaves: what it does, how long the target must be idle
/// first, and how often it acts. These are saved on the board; the toolbar's
/// Keep awake button turns it on and off.
/// </summary>
internal sealed class KeepAwakeForm : Form
{
    private static readonly (KeepAwakeMode Mode, string Label)[] Modes =
    [
        (KeepAwakeMode.Mixed, "Mixed: a different one each time"),
        (KeepAwakeMode.HiddenKey, "Hidden key: taps F15"),
        (KeepAwakeMode.Nudge, "Nudge: one pixel over and back"),
        (KeepAwakeMode.Wander, "Wander: a short glide and back"),
        (KeepAwakeMode.ScrollTick, "Scroll tick: one notch and back"),
    ];

    private readonly IKvmLink _link;
    private readonly ComboBox _modeBox;
    private readonly NumericUpDown _idleBox, _minBox, _maxBox;
    private readonly Label _note;
    private readonly Button _ok;
    private short _boardMode, _boardIdle, _boardMin, _boardMax;
    private bool _loaded, _busy;

    public KeepAwakeForm(Form owner, IKvmLink link)
    {
        _link = link;
        DialogLayout.SetUp(this, owner, "Keep awake settings");

        _modeBox = DialogLayout.Combo(Font, Modes.Select(m => (object)m.Label));
        _idleBox = Seconds(10, 3600);
        _minBox = Seconds(5, 3600);
        _maxBox = Seconds(5, 3600);

        var grid = DialogLayout.Grid();
        DialogLayout.AddRow(grid, "Mode", _modeBox);
        DialogLayout.AddRow(grid, "Start after", WithText(_idleBox, "seconds without input"));
        DialogLayout.AddRow(grid, "Then every", Range(_minBox, _maxBox));
        DialogLayout.AddNote(grid, "Keep awake never wakes a sleeping target, and any input pauses it " +
                                   "until the target has been idle again. Turn it on and off with the " +
                                   "Keep awake button.");
        _note = DialogLayout.AddNote(grid);
        SetEnabled(false);

        _ok = DialogLayout.AddButtons(this, grid);
        _ok.Click += async (_, _) => await ApplyAsync();
    }

    private static NumericUpDown Seconds(int min, int max) =>
        new() { Minimum = min, Maximum = max, Width = 80, Increment = 5 };

    private static FlowLayoutPanel WithText(Control box, string text)
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        row.Controls.Add(box);
        row.Controls.Add(new Label { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(4, 7, 0, 0) });
        return row;
    }

    private static FlowLayoutPanel Range(Control from, Control to)
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        row.Controls.Add(from);
        row.Controls.Add(new Label { Text = "to", AutoSize = true, Margin = new Padding(4, 7, 4, 0) });
        row.Controls.Add(to);
        row.Controls.Add(new Label { Text = "seconds, at random", AutoSize = true, Margin = new Padding(4, 7, 0, 0) });
        return row;
    }

    private void SetEnabled(bool enabled)
    {
        foreach (Control c in new Control[] { _modeBox, _idleBox, _minBox, _maxBox })
            c.Enabled = enabled;
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (!_link.SupportsKeepAwake)
        {
            _note.Text = "Keep awake needs firmware 0.5.0 or later; update it from the web installer.";
            return;
        }
        _note.Text = "Reading the board's settings...";
        try
        {
            _boardMode = (await _link.GetSettingAsync(BoardSetting.KeepAwakeMode)).Value;
            _boardIdle = (await _link.GetSettingAsync(BoardSetting.KeepAwakeIdleDelay)).Value;
            _boardMin = (await _link.GetSettingAsync(BoardSetting.KeepAwakeIntervalMin)).Value;
            _boardMax = (await _link.GetSettingAsync(BoardSetting.KeepAwakeIntervalMax)).Value;
            int i = Array.FindIndex(Modes, m => (short)m.Mode == _boardMode);
            _modeBox.SelectedIndex = i >= 0 ? i : 0;
            _idleBox.Value = Math.Clamp(_boardIdle, (int)_idleBox.Minimum, (int)_idleBox.Maximum);
            _minBox.Value = Math.Clamp(_boardMin, (int)_minBox.Minimum, (int)_minBox.Maximum);
            _maxBox.Value = Math.Clamp(_boardMax, (int)_maxBox.Minimum, (int)_maxBox.Maximum);
            _loaded = true;
            SetEnabled(true);
            _note.Text = "";
        }
        catch (Exception ex)
        {
            _note.ForeColor = Color.Firebrick;
            _note.Text = $"Couldn't read the board's settings: {ex.Message}";
        }
    }

    private async Task ApplyAsync()
    {
        if (_busy) return;
        if (!_loaded)
        {
            DialogResult = DialogResult.Cancel;
            return;
        }
        if (_minBox.Value > _maxBox.Value)
        {
            _note.ForeColor = Color.Firebrick;
            _note.Text = "The first number of the interval can't be larger than the second.";
            return;
        }
        _busy = true;
        UseWaitCursor = true;
        SetEnabled(false);
        _ok.Enabled = false;
        _note.ForeColor = SystemColors.GrayText;
        _note.Text = "Saving...";
        try
        {
            short mode = (short)Modes[_modeBox.SelectedIndex].Mode;
            short idle = (short)_idleBox.Value, min = (short)_minBox.Value, max = (short)_maxBox.Value;
            var changes = new List<(BoardSetting, short)>();
            if (mode != _boardMode) changes.Add((BoardSetting.KeepAwakeMode, mode));
            if (idle != _boardIdle) changes.Add((BoardSetting.KeepAwakeIdleDelay, idle));
            // The board keeps the interval's ends in order at every step, so
            // widen before narrowing: a new minimum above the old maximum
            // needs the maximum raised first.
            var interval = new List<(BoardSetting, short)>();
            if (min != _boardMin) interval.Add((BoardSetting.KeepAwakeIntervalMin, min));
            if (max != _boardMax) interval.Add((BoardSetting.KeepAwakeIntervalMax, max));
            if (min > _boardMax) interval.Reverse();
            changes.AddRange(interval);

            foreach (var (setting, value) in changes)
            {
                var reply = await _link.SetSettingAsync(setting, value);
                if (reply.Result != SettingResult.Ok)
                {
                    _note.ForeColor = Color.Firebrick;
                    _note.Text = reply.Result == SettingResult.OutOfRange
                        ? $"The board refused {value}: it's out of range for that setting."
                        : $"The board refused the change ({reply.Result}).";
                    return;
                }
            }
            _note.Text = "";
            DialogResult = DialogResult.OK;
        }
        catch (Exception ex)
        {
            _note.ForeColor = Color.Firebrick;
            _note.Text = $"Couldn't change the board's settings: {ex.Message}";
        }
        finally
        {
            _busy = false;
            UseWaitCursor = false;
            if (DialogResult != DialogResult.OK)
            {
                SetEnabled(_loaded);
                _ok.Enabled = true;
            }
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_busy) e.Cancel = true; // let the board answer first
        base.OnFormClosing(e);
    }
}
