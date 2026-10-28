namespace WinKeyEditor;

/// <summary>Extra modifiers held together with the Win key.</summary>
[Flags]
public enum Mods
{
    None = 0,
    Shift = 1,
    Ctrl = 2,
    Alt = 4,
}

/// <summary>A Win-key shortcut. Multi-key entries (e.g. Win+1…9) block every listed key.</summary>
public sealed record Shortcut(string Id, string Description, Mods Mods, Keys[] Keys, bool IsCustom = false)
{
    public static Shortcut Of(Mods mods, Keys key, string description, bool isCustom = false) =>
        new(Format(mods, key), description, mods, [key], isCustom);

    public IEnumerable<int> Combos => Keys.Select(k => Combo(k, Mods));

    public static int Combo(Keys key, Mods mods) => (int)key | ((int)mods << 16);

    public static string Format(Mods mods, Keys key)
    {
        var s = "Win";
        if (mods.HasFlag(Mods.Ctrl)) s += "+Ctrl";
        if (mods.HasFlag(Mods.Alt)) s += "+Alt";
        if (mods.HasFlag(Mods.Shift)) s += "+Shift";
        return s + "+" + KeyName(key);
    }

    public static string KeyName(Keys key) => key switch
    {
        >= System.Windows.Forms.Keys.D0 and <= System.Windows.Forms.Keys.D9 => ((char)key).ToString(),
        System.Windows.Forms.Keys.OemPeriod => ".",
        System.Windows.Forms.Keys.Oemcomma => ",",
        System.Windows.Forms.Keys.OemSemicolon => ";",
        System.Windows.Forms.Keys.Oemplus => "=",
        System.Windows.Forms.Keys.OemMinus => "-",
        System.Windows.Forms.Keys.OemQuestion => "/",
        System.Windows.Forms.Keys.Oemtilde => "`",
        System.Windows.Forms.Keys.OemOpenBrackets => "[",
        System.Windows.Forms.Keys.OemCloseBrackets => "]",
        System.Windows.Forms.Keys.OemQuotes => "'",
        System.Windows.Forms.Keys.OemPipe => "\\",
        System.Windows.Forms.Keys.Add => "Num+",
        System.Windows.Forms.Keys.Subtract => "Num-",
        System.Windows.Forms.Keys.PrintScreen => "PrtSc",
        System.Windows.Forms.Keys.Next => "PageDown",
        System.Windows.Forms.Keys.Prior => "PageUp",
        System.Windows.Forms.Keys.Back => "Backspace",
        System.Windows.Forms.Keys.Return => "Enter",
        _ => key.ToString(),
    };

    public static bool IsModifier(Keys key) => key is
        System.Windows.Forms.Keys.ShiftKey or System.Windows.Forms.Keys.LShiftKey or System.Windows.Forms.Keys.RShiftKey or
        System.Windows.Forms.Keys.ControlKey or System.Windows.Forms.Keys.LControlKey or System.Windows.Forms.Keys.RControlKey or
        System.Windows.Forms.Keys.Menu or System.Windows.Forms.Keys.LMenu or System.Windows.Forms.Keys.RMenu or
        System.Windows.Forms.Keys.LWin or System.Windows.Forms.Keys.RWin;
}

public static class ShortcutCatalog
{
    // Special entries with no key combos; the hook handles these directly.
    public const string WinKeyId = "Win key";
    public const string StartMenuId = "Win (tap alone)";

    public static readonly IReadOnlyList<Shortcut> BuiltIn =
    [
        new(WinKeyId, "The Windows key itself (blocks every Win shortcut)", Mods.None, []),
        new(StartMenuId, "Open the Start menu", Mods.None, []),
        Shortcut.Of(Mods.None, Keys.A, "Open Quick Settings"),
        Shortcut.Of(Mods.None, Keys.B, "Focus the system tray"),
        Shortcut.Of(Mods.None, Keys.C, "Open Copilot"),
        Shortcut.Of(Mods.None, Keys.D, "Show / hide desktop"),
        Shortcut.Of(Mods.None, Keys.E, "Open File Explorer"),
        Shortcut.Of(Mods.None, Keys.G, "Open Xbox Game Bar"),
        Shortcut.Of(Mods.None, Keys.H, "Voice typing"),
        Shortcut.Of(Mods.None, Keys.I, "Open Settings"),
        Shortcut.Of(Mods.None, Keys.K, "Cast to a device"),
        Shortcut.Of(Mods.None, Keys.M, "Minimize all windows"),
        Shortcut.Of(Mods.None, Keys.N, "Open notification center"),
        Shortcut.Of(Mods.None, Keys.P, "Project / display mode"),
        Shortcut.Of(Mods.None, Keys.R, "Open Run dialog"),
        Shortcut.Of(Mods.None, Keys.S, "Open Search"),
        Shortcut.Of(Mods.None, Keys.T, "Cycle taskbar apps"),
        Shortcut.Of(Mods.None, Keys.U, "Accessibility settings"),
        Shortcut.Of(Mods.None, Keys.V, "Clipboard history"),
        Shortcut.Of(Mods.None, Keys.W, "Open Widgets"),
        Shortcut.Of(Mods.None, Keys.X, "Quick Link menu"),
        Shortcut.Of(Mods.None, Keys.Z, "Snap layouts"),
        Shortcut.Of(Mods.None, Keys.Tab, "Task View"),
        Shortcut.Of(Mods.None, Keys.Space, "Switch input language"),
        Shortcut.Of(Mods.None, Keys.OemPeriod, "Emoji panel"),
        Shortcut.Of(Mods.None, Keys.OemSemicolon, "Emoji panel (alternate)"),
        Shortcut.Of(Mods.None, Keys.Oemcomma, "Peek at desktop"),
        Shortcut.Of(Mods.None, Keys.Home, "Minimize all but active window"),
        Shortcut.Of(Mods.None, Keys.PrintScreen, "Save screenshot to Pictures"),
        new("Win+1…9", "Open / switch to pinned taskbar app",
            Mods.None, [Keys.D1, Keys.D2, Keys.D3, Keys.D4, Keys.D5, Keys.D6, Keys.D7, Keys.D8, Keys.D9]),
        new("Win+Arrows", "Snap / maximize / minimize window",
            Mods.None, [Keys.Up, Keys.Down, Keys.Left, Keys.Right]),
        new("Win+Shift+Left/Right", "Move window to other monitor",
            Mods.Shift, [Keys.Left, Keys.Right]),
        new("Win+Plus/Minus", "Magnifier zoom in / out",
            Mods.None, [Keys.Oemplus, Keys.OemMinus, Keys.Add, Keys.Subtract]),
        Shortcut.Of(Mods.Shift, Keys.S, "Snipping Tool screenshot"),
        Shortcut.Of(Mods.Shift, Keys.C, "Color picker / PowerToys (if installed)"),
        Shortcut.Of(Mods.Ctrl, Keys.D, "New virtual desktop"),
        Shortcut.Of(Mods.Ctrl, Keys.F4, "Close virtual desktop"),
        new("Win+Ctrl+Left/Right", "Switch virtual desktop",
            Mods.Ctrl, [Keys.Left, Keys.Right]),
        Shortcut.Of(Mods.Ctrl, Keys.Return, "Toggle Narrator"),
        Shortcut.Of(Mods.Alt, Keys.R, "Game Bar: start/stop recording"),
        Shortcut.Of(Mods.Alt, Keys.G, "Game Bar: record last moments"),
        Shortcut.Of(Mods.Alt, Keys.PrintScreen, "Game Bar: screenshot"),
    ];
}
