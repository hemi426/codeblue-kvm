using CodeBlue.Viewer.Video;

namespace CodeBlue.Viewer;

/// <summary>The viewer's own options: how it opens the video, and how it handles the mouse and scaling.</summary>
internal sealed class ViewerOptionsForm : Form
{
    // This dialog has the most to say, so its notes run wider, keeping it short enough for small screens.
    private const int NoteWidth = 560;

    private readonly ViewerSettings _settings;
    private readonly ComboBox _formatBox, _sizeBox, _fpsBox, _mouseBox, _indicatorBox;
    private readonly NumericUpDown _cachingBox;
    private readonly CheckBox _nativeBox, _updatesBox;

    public ViewerOptionsForm(Form owner, ViewerSettings settings, bool videoRunning)
    {
        _settings = settings;
        DialogLayout.SetUp(this, owner, "Viewer options");

        // One width for the column, from the widest list (the sizes).
        _sizeBox = DialogLayout.Combo(Font, CaptureOptions.Sizes);
        int width = _sizeBox.Width;
        _formatBox = DialogLayout.Combo(Font, CaptureOptions.Formats, width);
        _fpsBox = DialogLayout.Combo(Font, CaptureOptions.FrameRates, width);
        _cachingBox = new NumericUpDown { Minimum = 0, Maximum = 2000, Increment = 10, Width = width };
        _mouseBox = DialogLayout.Combo(Font, [MainForm.MouseAuto, MainForm.MouseAbsolute, MainForm.MouseRelative], width);
        _nativeBox = new CheckBox { Text = "1:1 pixels (Ctrl+Alt+1)", AutoSize = true };
        _indicatorBox = DialogLayout.Combo(Font, [MainForm.IndicatorBanner, MainForm.IndicatorBorder], width);
        _updatesBox = new CheckBox { Text = "Check for updates", AutoSize = true };

        var grid = DialogLayout.Grid();
        DialogLayout.AddHeading(grid, "Video", Font);
        DialogLayout.AddRow(grid, "Format", _formatBox);
        DialogLayout.AddRow(grid, "Size", _sizeBox);
        DialogLayout.AddRow(grid, "FPS", _fpsBox);
        DialogLayout.AddRow(grid, "Cache ms", _cachingBox);
        if (videoRunning)
            DialogLayout.AddNote(grid, "Video changes take effect the next time you connect.", width: NoteWidth);
        DialogLayout.AddHeading(grid, "Display and mouse", Font);
        DialogLayout.AddRow(grid, "Mouse", _mouseBox);
        DialogLayout.AddRow(grid, "When captured", _indicatorBox);
        grid.Controls.Add(_nativeBox);
        grid.SetColumnSpan(_nativeBox, 2);
        DialogLayout.AddNote(grid, "Mouse: Auto uses the absolute pointer, and switches to relative when the " +
                                   "target needs it. When captured: Banner shows a strip above the picture; " +
                                   "Border, a slim frame around the video area.", width: NoteWidth);
        DialogLayout.AddHeading(grid, "Updates", Font);
        grid.Controls.Add(_updatesBox);
        grid.SetColumnSpan(_updatesBox, 2);
        DialogLayout.AddNote(grid, "About once a day, checks the CodeBlue site (hemi426.github.io) for a newer " +
                                   "viewer, or newer firmware than the board's, and says so in the status bar. " +
                                   "Nothing is downloaded or installed.", width: NoteWidth);

        DialogLayout.Select(_formatBox, settings.Format);
        DialogLayout.Select(_sizeBox, settings.Size);
        DialogLayout.Select(_fpsBox, settings.Fps);
        _cachingBox.Value = Math.Clamp(settings.CachingMs, 0, 2000);
        DialogLayout.Select(_mouseBox, settings.MouseMode);
        DialogLayout.Select(_indicatorBox, settings.CaptureIndicator);
        _nativeBox.Checked = settings.NativeScale;
        _updatesBox.Checked = settings.CheckForUpdates;

        var ok = DialogLayout.AddButtons(this, grid);
        ok.Click += (_, _) =>
        {
            _settings.Format = (string)_formatBox.SelectedItem!;
            _settings.Size = (string)_sizeBox.SelectedItem!;
            _settings.Fps = (string)_fpsBox.SelectedItem!;
            _settings.CachingMs = (int)_cachingBox.Value;
            _settings.MouseMode = (string)_mouseBox.SelectedItem!;
            _settings.CaptureIndicator = (string)_indicatorBox.SelectedItem!;
            _settings.NativeScale = _nativeBox.Checked;
            _settings.CheckForUpdates = _updatesBox.Checked;
            DialogResult = DialogResult.OK;
        };
    }
}
