namespace CodeBlue.Viewer;

/// <summary>Shared layout for the viewer's small dialogs: labelled rows, then OK and Cancel.</summary>
internal static class DialogLayout
{
    public static void SetUp(Form form, Form owner, string title)
    {
        form.Text = title;
        form.Font = owner.Font;
        form.Icon = owner.Icon;
        form.FormBorderStyle = FormBorderStyle.FixedDialog;
        form.MaximizeBox = false;
        form.MinimizeBox = false;
        form.ShowInTaskbar = false;
        form.StartPosition = FormStartPosition.CenterParent;
        form.AutoSize = true;
        form.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        form.Padding = new Padding(10);
    }

    public static TableLayoutPanel Grid()
    {
        // Not docked: a form that sizes itself to its contents ignores docked controls.
        var grid = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            Margin = Padding.Empty,
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        return grid;
    }

    /// <summary>Adds a labelled row; returns the label, for callers that change it later.</summary>
    public static Label AddRow(TableLayoutPanel grid, string label, Control control)
    {
        control.Margin = new Padding(4, 4, 4, 4);
        var caption = new Label
        {
            Text = label,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 4, 12, 4),
        };
        grid.Controls.Add(caption);
        grid.Controls.Add(control);
        return caption;
    }

    /// <summary>A line of text across both columns; wrapped, unless it's short enough to be worth keeping whole.</summary>
    public static Label AddNote(TableLayoutPanel grid, string text = "", bool wrap = true)
    {
        var note = new Label
        {
            Text = text,
            AutoSize = true,
            MaximumSize = wrap ? new Size(420, 0) : Size.Empty,
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(0, 6, 0, 6),
        };
        grid.Controls.Add(note);
        grid.SetColumnSpan(note, 2);
        return note;
    }

    /// <summary>A heading across both columns, to start a group of rows.</summary>
    public static void AddHeading(TableLayoutPanel grid, string text, Font font)
    {
        var heading = new Label
        {
            Text = text,
            AutoSize = true,
            Font = new Font(font, FontStyle.Bold),
            Margin = new Padding(0, grid.Controls.Count == 0 ? 0 : 12, 0, 2),
        };
        grid.Controls.Add(heading);
        grid.SetColumnSpan(heading, 2);
    }

    /// <summary>A drop-down list wide enough for its longest item in <paramref name="font"/>.</summary>
    public static ComboBox Combo(Font font, IEnumerable<object> items, int minWidth = 110)
    {
        var box = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        box.Items.AddRange([.. items]);
        int widest = box.Items.Cast<object>()
            .Max(i => TextRenderer.MeasureText(i.ToString(), font).Width);
        // Room for the arrow button and the box's own borders and padding.
        box.Width = Math.Max(minWidth, widest + SystemInformation.VerticalScrollBarWidth + 16);
        return box;
    }

    public static void Select(ComboBox box, string? value)
    {
        int i = value is null ? -1 : box.FindStringExact(value);
        box.SelectedIndex = i >= 0 ? i : 0;
    }

    /// <summary>OK and Cancel at the bottom right; returns OK, whose click the caller handles.</summary>
    public static Button AddButtons(Form form, Control body)
    {
        var ok = new Button { Text = "OK", AutoSize = true, MinimumSize = new Size(88, 0) };
        var cancel = new Button
        {
            Text = "Cancel", AutoSize = true, MinimumSize = new Size(88, 0), DialogResult = DialogResult.Cancel,
        };
        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Anchor = AnchorStyles.Right,
            Margin = new Padding(0, 10, 0, 0),
        };
        buttons.Controls.AddRange([cancel, ok]);
        // The body above, the buttons below at the right; the form sizes itself to fit both.
        var outer = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Location = new Point(form.Padding.Left, form.Padding.Top),
            Margin = Padding.Empty,
        };
        outer.Controls.Add(body);
        outer.Controls.Add(buttons);
        form.Controls.Add(outer);
        form.AcceptButton = ok;
        form.CancelButton = cancel;
        return ok;
    }
}
