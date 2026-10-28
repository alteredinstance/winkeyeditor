using System.Runtime.InteropServices;

namespace WinKeyEditor;

public sealed class MainForm : Form
{
    const int WM_HOTKEY = 0x0312;
    const int HotkeyId = 1;
    const uint MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_NOREPEAT = 0x4000;
    const string GameModeHotkey = "Ctrl+Shift+F12";

    sealed record ModeChoice(BlockMode Mode, string Label, Color Back, Color Fore);

    static readonly ModeChoice[] ModeChoices =
    [
        new(BlockMode.Allowed, "Allowed", Color.Empty, SystemColors.GrayText),
        new(BlockMode.Always, "Always blocked", Color.FromArgb(253, 226, 226), Color.FromArgb(153, 27, 27)),
        new(BlockMode.GameOnly, "Blocked in game mode", Color.FromArgb(219, 234, 254), Color.FromArgb(30, 64, 175)),
        new(BlockMode.OutsideGame, "Blocked except in game mode", Color.FromArgb(254, 243, 199), Color.FromArgb(146, 64, 14)),
    ];

    readonly Settings settings = Settings.Load();
    readonly KeyboardHook hook = new();
    readonly NotifyIcon tray = new();
    readonly ToolStripMenuItem trayEnabled = new("Blocking enabled") { CheckOnClick = true };
    readonly ToolStripMenuItem trayGameMode = new($"Game mode\t{GameModeHotkey}") { CheckOnClick = true };
    readonly ToolStripMenuItem trayStartup = new("Start with Windows") { CheckOnClick = true };
    readonly System.Windows.Forms.Timer fullscreenTimer = new() { Interval = 1500 };

    readonly CheckBox enabledBox = new() { Text = "Blocking enabled", AutoSize = true };
    readonly CheckBox gameModeBox = new() { Text = $"Game mode  ({GameModeHotkey} toggles it from anywhere)", AutoSize = true };
    readonly CheckBox autoGameBox = new() { Text = "Also treat any fullscreen app/game as game mode automatically", AutoSize = true };
    readonly CheckBox startupBox = new() { Text = "Start with Windows (in the tray)", AutoSize = true };
    readonly CheckBox lockBox = new() { Text = "Disable Win+L (turns off workstation locking entirely)", AutoSize = true };
    readonly Label statusLabel = new() { AutoSize = true, Margin = new Padding(3, 6, 3, 3) };
    readonly DataGridView grid = new()
    {
        Dock = DockStyle.Fill,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        AllowUserToResizeRows = false,
        RowHeadersVisible = false,
        EditMode = DataGridViewEditMode.EditOnEnter,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        BackgroundColor = SystemColors.Window,
        BorderStyle = BorderStyle.FixedSingle,
        CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
    };
    readonly DataGridViewComboBoxColumn modeColumn = new()
    {
        HeaderText = "Block",
        FillWeight = 38,
        DataSource = ModeChoices,
        DisplayMember = nameof(ModeChoice.Label),
        ValueMember = nameof(ModeChoice.Mode),
        ValueType = typeof(BlockMode),
        FlatStyle = FlatStyle.Flat,
    };

    bool loading;
    bool exiting;
    bool autoGameActive;
    bool hotkeyRegistered;

    bool GameActive => settings.GameMode || autoGameActive;

    public MainForm(bool startInTray)
    {
        Text = "WinKey Editor";
        Icon = SystemIcons.Shield;
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(700, 720);
        MinimumSize = new Size(560, 480);
        StartPosition = FormStartPosition.CenterScreen;
        Padding = new Padding(10);

        BuildLayout();
        BuildTray();
        LoadState();

        Shown += (_, _) => grid.ClearSelection();
        fullscreenTimer.Tick += (_, _) => UpdateAutoGameMode();
        fullscreenTimer.Start();

        if (startInTray)
        {
            WindowState = FormWindowState.Minimized;
            ShowInTaskbar = false;
            Load += (_, _) => Hide();
        }
    }

    void BuildLayout()
    {
        var options = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.TopDown,
            AutoSize = true,
            WrapContents = false,
        };
        statusLabel.Font = new Font(Font, FontStyle.Bold);
        var hint = new Label
        {
            Text = "Choose when each shortcut is blocked. Right-click to change several selected rows at once.",
            AutoSize = true,
            Margin = new Padding(3, 10, 3, 3),
        };
        options.Controls.AddRange([enabledBox, gameModeBox, autoGameBox, startupBox, lockBox, statusLabel, hint]);

        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Shortcut", ReadOnly = true, FillWeight = 26 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Action", ReadOnly = true, FillWeight = 46 });
        grid.Columns.Add(modeColumn);
        foreach (DataGridViewColumn c in grid.Columns)
            c.SortMode = DataGridViewColumnSortMode.NotSortable;

        // Commit dropdown picks immediately instead of when the cell loses focus.
        grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (grid.IsCurrentCellDirty)
                grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        grid.CellValueChanged += (_, e) =>
        {
            if (loading || e.RowIndex < 0 || e.ColumnIndex != modeColumn.Index)
                return;
            var row = grid.Rows[e.RowIndex];
            if (row.Tag is Shortcut s && row.Cells[e.ColumnIndex].Value is BlockMode mode && mode != settings.ModeOf(s.Id))
            {
                settings.SetMode(s.Id, mode);
                StyleRow(row, mode);
                ApplyAndSave();
            }
        };
        grid.DataError += (_, e) => e.ThrowException = false;
        grid.CellMouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Right && e.RowIndex >= 0 && !grid.Rows[e.RowIndex].Selected)
            {
                grid.ClearSelection();
                grid.Rows[e.RowIndex].Selected = true;
            }
        };

        var rowMenu = new ContextMenuStrip();
        foreach (var choice in ModeChoices)
            rowMenu.Items.Add("Set to: " + choice.Label, null, (_, _) => SetSelectedModes(choice.Mode));
        grid.ContextMenuStrip = rowMenu;

        var add = new Button { Text = "Add custom…", AutoSize = true };
        var remove = new Button { Text = "Remove custom", AutoSize = true };
        var reset = new Button { Text = "Allow all", AutoSize = true };
        var hide = new Button { Text = "Hide to tray", AutoSize = true };
        add.Click += (_, _) => AddCustom();
        remove.Click += (_, _) => RemoveCustom();
        reset.Click += (_, _) => { grid.SelectAll(); SetSelectedModes(BlockMode.Allowed); grid.ClearSelection(); };
        hide.Click += (_, _) => Hide();

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(0, 6, 0, 0) };
        buttons.Controls.AddRange([add, remove, reset, hide]);

        var footer = new Label
        {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            MaximumSize = new Size(680, 0),
            ForeColor = SystemColors.GrayText,
            Padding = new Padding(0, 4, 0, 0),
            Text = "Note: shortcuts aren't blocked while an app running as administrator has focus, " +
                   "unless WinKey Editor is also run as administrator.",
        };

        Controls.Add(grid);
        Controls.Add(options);
        Controls.Add(buttons);
        Controls.Add(footer);

        enabledBox.CheckedChanged += (_, _) => { if (!loading) { settings.Enabled = enabledBox.Checked; ApplyAndSave(); } };
        gameModeBox.CheckedChanged += (_, _) => { if (!loading) SetGameMode(gameModeBox.Checked); };
        autoGameBox.CheckedChanged += (_, _) =>
        {
            if (loading) return;
            settings.AutoGameMode = autoGameBox.Checked;
            ApplyAndSave();
            UpdateAutoGameMode();
        };
        startupBox.CheckedChanged += (_, _) => { if (!loading) SetStartWithWindows(startupBox.Checked); };
        lockBox.CheckedChanged += (_, _) => { if (!loading) TryRegistry(() => SystemSettings.DisableLock = lockBox.Checked); };
    }

    void BuildTray()
    {
        trayEnabled.CheckedChanged += (_, _) => { if (!loading) enabledBox.Checked = trayEnabled.Checked; };
        trayGameMode.CheckedChanged += (_, _) => { if (!loading) SetGameMode(trayGameMode.Checked); };
        trayStartup.CheckedChanged += (_, _) => { if (!loading) SetStartWithWindows(trayStartup.Checked); };

        var menu = new ContextMenuStrip();
        menu.Items.Add("Open WinKey Editor", null, (_, _) => ShowWindow());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(trayEnabled);
        menu.Items.Add(trayGameMode);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(trayStartup);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitApp());

        tray.Icon = SystemIcons.Shield;
        tray.Text = "WinKey Editor";
        tray.ContextMenuStrip = menu;
        tray.Visible = true;
        tray.DoubleClick += (_, _) => ShowWindow();
    }

    void LoadState()
    {
        loading = true;
        enabledBox.Checked = trayEnabled.Checked = settings.Enabled;
        gameModeBox.Checked = trayGameMode.Checked = settings.GameMode;
        autoGameBox.Checked = settings.AutoGameMode;
        startupBox.Checked = trayStartup.Checked = SystemSettings.StartWithWindows;
        lockBox.Checked = SystemSettings.DisableLock;
        PopulateGrid();
        // Keep the Run entry pointing at this exe in case it was moved.
        if (startupBox.Checked && !SystemSettings.StartupPointsHere)
            try { SystemSettings.StartWithWindows = true; } catch (Exception) { }
        loading = false;
        // Persist any migration of older settings files.
        ApplyAndSave();
    }

    void PopulateGrid()
    {
        var wasLoading = loading;
        loading = true;
        grid.Rows.Clear();
        foreach (var s in settings.AllShortcuts())
        {
            var row = grid.Rows[grid.Rows.Add(s.Id, s.Description, settings.ModeOf(s.Id))];
            row.Tag = s;
            if (s.IsCustom)
                row.Cells[0].Style.ForeColor = row.Cells[1].Style.ForeColor = SystemColors.HotTrack;
            if (s.Keys.Length == 0)
                row.Cells[0].Style.Font = new Font(grid.Font, FontStyle.Bold);
            StyleRow(row, settings.ModeOf(s.Id));
        }
        loading = wasLoading;
    }

    static void StyleRow(DataGridViewRow row, BlockMode mode)
    {
        var choice = ModeChoices.First(c => c.Mode == mode);
        var style = row.Cells[2].Style;
        style.BackColor = style.SelectionBackColor = choice.Back.IsEmpty ? SystemColors.Window : choice.Back;
        style.ForeColor = style.SelectionForeColor = choice.Fore;
    }

    void SetSelectedModes(BlockMode mode)
    {
        grid.EndEdit();
        var rows = grid.SelectedRows.Cast<DataGridViewRow>()
            .Concat(grid.SelectedCells.Cast<DataGridViewCell>().Select(c => c.OwningRow!))
            .Distinct();
        loading = true;
        foreach (var row in rows)
        {
            if (row.Tag is not Shortcut s)
                continue;
            settings.SetMode(s.Id, mode);
            row.Cells[modeColumn.Index].Value = mode;
            StyleRow(row, mode);
        }
        loading = false;
        ApplyAndSave();
    }

    void SetGameMode(bool on)
    {
        settings.GameMode = on;
        ApplyAndSave();
        tray.ShowBalloonTip(1500, "WinKey Editor", on ? "Game mode ON" : "Game mode OFF", ToolTipIcon.None);
    }

    void UpdateAutoGameMode()
    {
        var active = settings.AutoGameMode && FullscreenDetector.IsFullscreenAppActive();
        if (active == autoGameActive)
            return;
        autoGameActive = active;
        ApplyToHook();
    }

    void ApplyAndSave()
    {
        ApplyToHook();
        try { settings.Save(); }
        catch (Exception ex) { statusLabel.Text = "Couldn't save settings: " + ex.Message; }
    }

    void ApplyToHook()
    {
        var game = GameActive;
        hook.Enabled = settings.Enabled;
        hook.WinKeyOff = settings.IsBlocked(ShortcutCatalog.WinKeyId, game);
        hook.BlockStartMenu = settings.IsBlocked(ShortcutCatalog.StartMenuId, game);
        hook.SetBlocked(settings.BlockedCombos(game));

        loading = true;
        enabledBox.Checked = trayEnabled.Checked = settings.Enabled;
        gameModeBox.Checked = trayGameMode.Checked = settings.GameMode;
        loading = false;

        var blockedCount = settings.AllShortcuts().Count(s => settings.IsBlocked(s.Id, game));
        var gameText = settings.GameMode ? "Game mode ON" : autoGameActive ? "Game mode ON (fullscreen app detected)" : "Game mode off";
        string state;
        if (!settings.Enabled)
            state = "Blocking is paused.";
        else if (hook.WinKeyOff)
            state = $"{gameText} · the Windows key is fully disabled.";
        else
            state = $"{gameText} · {blockedCount} shortcut(s) blocked right now.";
        if (!hotkeyRegistered)
            state += $"  ({GameModeHotkey} is in use by another app.)";
        statusLabel.Text = state;
        statusLabel.ForeColor = !settings.Enabled ? SystemColors.GrayText : game ? Color.FromArgb(30, 64, 175) : SystemColors.ControlText;

        var trayText = "WinKey Editor – " + (!settings.Enabled ? "paused" : game ? "game mode" : "active");
        tray.Text = trayText.Length > 63 ? trayText[..63] : trayText;
    }

    void AddCustom()
    {
        using var dlg = new CaptureDialog();
        if (dlg.ShowDialog(this) != DialogResult.OK)
            return;

        var id = Shortcut.Format(dlg.Mods, dlg.Key);
        var combo = Shortcut.Combo(dlg.Key, dlg.Mods);
        var existing = grid.Rows.Cast<DataGridViewRow>().FirstOrDefault(r => ((Shortcut)r.Tag!).Combos.Contains(combo));
        if (existing is null)
        {
            settings.Custom.Add(new Settings.CustomEntry { Key = dlg.Key, Mods = dlg.Mods, Description = dlg.Description });
            settings.SetMode(id, BlockMode.Always);
            PopulateGrid();
            ApplyAndSave();
            existing = grid.Rows.Cast<DataGridViewRow>().First(r => ((Shortcut)r.Tag!).Id == id);
        }
        grid.ClearSelection();
        grid.CurrentCell = existing.Cells[modeColumn.Index];
        grid.FirstDisplayedScrollingRowIndex = existing.Index;
        grid.Focus();
    }

    void RemoveCustom()
    {
        var selected = grid.SelectedCells.Cast<DataGridViewCell>()
            .Select(c => (Shortcut)c.OwningRow!.Tag!)
            .Where(s => s.IsCustom)
            .Distinct()
            .ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show(this, "Select a custom shortcut (shown in blue) to remove. Built-in entries can only be set to Allowed.",
                Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        foreach (var s in selected)
        {
            settings.Custom.RemoveAll(c => Shortcut.Format(c.Mods, c.Key) == s.Id);
            settings.SetMode(s.Id, BlockMode.Allowed);
        }
        PopulateGrid();
        ApplyAndSave();
    }

    void SetStartWithWindows(bool on) => TryRegistry(() => SystemSettings.StartWithWindows = on);

    void TryRegistry(Action action)
    {
        try { action(); }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        loading = true;
        startupBox.Checked = trayStartup.Checked = SystemSettings.StartWithWindows;
        lockBox.Checked = SystemSettings.DisableLock;
        loading = false;
    }

    public void ShowWindow()
    {
        ShowInTaskbar = true;
        Show();
        if (WindowState == FormWindowState.Minimized)
            WindowState = FormWindowState.Normal;
        Activate();
    }

    void ExitApp()
    {
        exiting = true;
        Close();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        hotkeyRegistered = RegisterHotKey(Handle, HotkeyId, MOD_CONTROL | MOD_SHIFT | MOD_NOREPEAT, (uint)Keys.F12);
        ApplyToHook();
    }

    // Toggling ShowInTaskbar recreates the window, so the hotkey must follow the handle.
    protected override void OnHandleDestroyed(EventArgs e)
    {
        UnregisterHotKey(Handle, HotkeyId);
        base.OnHandleDestroyed(e);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY && m.WParam == HotkeyId)
            SetGameMode(!settings.GameMode);
        base.WndProc(ref m);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!exiting && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        fullscreenTimer.Dispose();
        hook.Dispose();
        tray.Visible = false;
        tray.Dispose();
        base.OnFormClosed(e);
    }

    [DllImport("user32.dll")]
    static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
