namespace WinKeyEditor;

/// <summary>Asks the user to press a key (plus optional Shift/Ctrl/Alt). Win is implied.</summary>
public sealed class CaptureDialog : Form
{
    readonly TextBox capture = new() { ReadOnly = true, Dock = DockStyle.Top, TextAlign = HorizontalAlignment.Center };
    readonly TextBox description = new() { Dock = DockStyle.Top, PlaceholderText = "Description (optional)" };
    readonly Button ok = new() { Text = "Add", DialogResult = DialogResult.OK, Enabled = false, AutoSize = true };

    public Keys Key { get; private set; }
    public Mods Mods { get; private set; }
    public string Description => description.Text.Trim();

    public CaptureDialog()
    {
        Text = "Add custom shortcut";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(380, 150);
        Padding = new Padding(12);

        var hint = new Label
        {
            Text = "Click the box below and press the key (with Shift / Ctrl / Alt if needed). " +
                   "Don't hold Win — it's added automatically.",
            Dock = DockStyle.Top,
            Height = 40,
        };
        capture.Font = new Font(Font.FontFamily, 12f, FontStyle.Bold);
        capture.PlaceholderText = "Press a key…";
        capture.PreviewKeyDown += (_, e) => e.IsInputKey = true;
        capture.KeyDown += OnCaptureKeyDown;

        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true };
        buttons.Controls.AddRange([cancel, ok]);

        Controls.Add(description);
        Controls.Add(new Panel { Dock = DockStyle.Top, Height = 8 });
        Controls.Add(capture);
        Controls.Add(hint);
        Controls.Add(buttons);
        AcceptButton = ok;
        CancelButton = cancel;
    }

    void OnCaptureKeyDown(object? sender, KeyEventArgs e)
    {
        e.SuppressKeyPress = true;
        e.Handled = true;
        if (Shortcut.IsModifier(e.KeyCode))
            return;

        Key = e.KeyCode;
        Mods = (e.Shift ? Mods.Shift : 0) | (e.Control ? Mods.Ctrl : 0) | (e.Alt ? Mods.Alt : 0);
        capture.Text = Shortcut.Format(Mods, Key);
        ok.Enabled = true;
    }
}
