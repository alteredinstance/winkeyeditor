using System.Runtime.InteropServices;

namespace WinKeyEditor;

public sealed class MainForm : Form
{
    const int WM_HOTKEY = 0x0312;
    const int HotkeyId = 1;
    const uint MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_NOREPEAT = 0x4000;
    const string GameModeHotkey = "Ctrl+Shift+F12";

    readonly Settings settings = Settings.Load();
    readonly KeyboardHook hook = new();
    readonly NotifyIcon tray = new();
    readonly ToolStripMenuItem trayEnabled = new("Blocking enabled") { CheckOnClick = true };
    readonly ToolStripMenuItem trayGameMode = new($"Game mode (disable Win key)\t{GameModeHotkey}") { CheckOnClick = true };
    readonly ToolStripMenuItem trayStartup = new("Start with Windows") { CheckOnClick = true };
    readonly System.Windows.Forms.Timer fullscreenTimer = new() { Interval = 1500 };

    readonly CheckBox enabledBox = new() { Text = "Blocking enabled", AutoSize = true };
    readonly CheckBox gameModeBox = new() { Text = $"Game mode: disable the Windows key entirely  ({GameModeHotkey})", AutoSize = true };
    readonly CheckBox autoGameBox = new() { Text = "Turn on game mode automatically while a fullscreen app/game is active", AutoSize = true };
    readonly CheckBox startMenuBox = new() { Text = "Don't open the Start menu when Win is tapped alone", AutoSize = true };
    readonly CheckBox startupBox = new() { Text = "Start with Windows (in the tray)", AutoSize = true };
    readonly CheckBox lockBox = new() { Text = "Disable Win+L (turns off workstation locking entirely)", AutoSize = true };
    readonly Label statusLabel = new() { AutoSize = true, ForeColor = SystemColors.GrayText };
    readonly ListView list = new()
    {
        Dock = DockStyle.Fill,
        View = View.Details,
        CheckBoxes = true,
        FullRowSelect = true,
        HeaderStyle = ColumnHeaderStyle.Nonclickable,
    };

    bool loading;
    bool exiting;
    bool autoGameActive;
    bool hotkeyRegistered;

    public MainForm(bool startInTray)
    {
        Text = "WinKey Editor";
        Icon = SystemIcons.Shield;
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(620, 680);
        MinimumSize = new Size(480, 480);
        StartPosition = FormStartPosition.CenterScreen;
        Padding = new Padding(10);

        BuildLayout();
        BuildTray();
        LoadState();

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
        var hint = new Label
        {
            Text = "Check the Win shortcuts you want to disable:",
            AutoSize = true,
            Margin = new Padding(3, 10, 3, 3),
            Font = new Font(Font, FontStyle.Bold),
        };
        options.Controls.AddRange([enabledBox, gameModeBox, autoGameBox, startMenuBox, startupBox, lockBox, hint]);

        list.Columns.Add("Shortcut", 190);
        list.Columns.Add("Action", 360);
        // ListView also raises ItemChecked on its own while (re)creating its handle,
        // so treat each event as "sync this one item" and ignore no-op changes.
        list.ItemChecked += (_, e) =>
        {
            if (loading || e.Item.Tag is not Shortcut s || e.Item.Checked == settings.Disabled.Contains(s.Id))
                return;
            if (e.Item.Checked)
                settings.Disabled.Add(s.Id);
            else
                settings.Disabled.Remove(s.Id);
            ApplyAndSave();
        };

        var add = new Button { Text = "Add custom…", AutoSize = true };
        var remove = new Button { Text = "Remove custom", AutoSize = true };
        var all = new Button { Text = "Check all", AutoSize = true };
        var none = new Button { Text = "Uncheck all", AutoSize = true };
        var hide = new Button { Text = "Hide to tray", AutoSize = true };
        add.Click += (_, _) => AddCustom();
        remove.Click += (_, _) => RemoveCustom();
        all.Click += (_, _) => SetAllChecked(true);
        none.Click += (_, _) => SetAllChecked(false);
        hide.Click += (_, _) => Hide();

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(0, 6, 0, 0) };
        buttons.Controls.AddRange([add, remove, all, none, hide]);

        var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.TopDown };
        footer.Controls.Add(statusLabel);
        footer.Controls.Add(new Label
        {
            AutoSize = true,
            MaximumSize = new Size(600, 0),
            ForeColor = SystemColors.GrayText,
            Text = "Note: shortcuts aren't blocked while an app running as administrator has focus, " +
                   "unless WinKey Editor is also run as administrator.",
        });

        Controls.Add(list);
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
        startMenuBox.CheckedChanged += (_, _) => { if (!loading) { settings.BlockStartMenu = startMenuBox.Checked; ApplyAndSave(); } };
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
        startMenuBox.Checked = settings.BlockStartMenu;
        startupBox.Checked = trayStartup.Checked = SystemSettings.StartWithWindows;
        lockBox.Checked = SystemSettings.DisableLock;
        PopulateList();
        // Keep the Run entry pointing at this exe in case it was moved.
        if (startupBox.Checked && !SystemSettings.StartupPointsHere)
            try { SystemSettings.StartWithWindows = true; } catch (Exception) { }
        loading = false;
        ApplyToHook();
    }

    void PopulateList()
    {
        var wasLoading = loading;
        loading = true;
        list.BeginUpdate();
        list.Items.Clear();
        foreach (var s in settings.AllShortcuts())
        {
            var item = new ListViewItem([s.Id, s.Description])
            {
                Tag = s,
                Checked = settings.Disabled.Contains(s.Id),
                ForeColor = s.IsCustom ? SystemColors.HotTrack : SystemColors.WindowText,
            };
            list.Items.Add(item);
        }
        list.EndUpdate();
        loading = wasLoading;
    }

    void SetAllChecked(bool value)
    {
        settings.Disabled = value ? settings.AllShortcuts().Select(s => s.Id).ToList() : [];
        loading = true;
        foreach (ListViewItem item in list.Items)
            item.Checked = value;
        loading = false;
        ApplyAndSave();
    }

    void SetGameMode(bool on)
    {
        settings.GameMode = on;
        ApplyAndSave();
        tray.ShowBalloonTip(1500, "WinKey Editor",
            on ? "Game mode ON: Windows key disabled" : "Game mode OFF", ToolTipIcon.None);
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
        hook.Enabled = settings.Enabled;
        hook.BlockStartMenu = settings.BlockStartMenu;
        hook.WinKeyOff = settings.GameMode || autoGameActive;
        hook.SetBlocked(settings.BlockedCombos());

        loading = true;
        enabledBox.Checked = trayEnabled.Checked = settings.Enabled;
        gameModeBox.Checked = trayGameMode.Checked = settings.GameMode;
        loading = false;

        var state = !settings.Enabled ? "Blocking is paused."
            : hook.WinKeyOff ? (settings.GameMode ? "Game mode: Windows key is disabled." : "Fullscreen app detected: Windows key is disabled.")
            : $"{settings.Disabled.Count} shortcut(s) disabled.";
        if (!hotkeyRegistered)
            state += $"  ({GameModeHotkey} is in use by another app.)";
        statusLabel.Text = state;
        tray.Text = "WinKey Editor – " + (hook.WinKeyOff && settings.Enabled ? "Win key off" : settings.Enabled ? "active" : "paused");
    }

    void AddCustom()
    {
        using var dlg = new CaptureDialog();
        if (dlg.ShowDialog(this) != DialogResult.OK)
            return;

        var id = Shortcut.Format(dlg.Mods, dlg.Key);
        var existing = list.Items.Cast<ListViewItem>()
            .FirstOrDefault(i => ((Shortcut)i.Tag!).Combos.Contains(Shortcut.Combo(dlg.Key, dlg.Mods)));
        if (existing is null)
        {
            settings.Custom.Add(new Settings.CustomEntry { Key = dlg.Key, Mods = dlg.Mods, Description = dlg.Description });
            if (!settings.Disabled.Contains(id))
                settings.Disabled.Add(id);
            PopulateList();
            existing = list.Items.Cast<ListViewItem>().First(i => ((Shortcut)i.Tag!).Id == id);
            ApplyAndSave();
        }
        else
        {
            existing.Checked = true;
        }
        existing.Selected = true;
        existing.EnsureVisible();
        list.Focus();
    }

    void RemoveCustom()
    {
        var selected = list.SelectedItems.Cast<ListViewItem>()
            .Select(i => (Shortcut)i.Tag!)
            .Where(s => s.IsCustom)
            .ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show(this, "Select a custom shortcut (shown in blue) to remove. Built-in entries can only be unchecked.",
                Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        foreach (var s in selected)
        {
            settings.Custom.RemoveAll(c => Shortcut.Format(c.Mods, c.Key) == s.Id);
            settings.Disabled.Remove(s.Id);
        }
        PopulateList();
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
