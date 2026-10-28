using System.Text.Json;
using Microsoft.Win32;

namespace WinKeyEditor;

public sealed class Settings
{
    public bool Enabled { get; set; } = true;
    public bool BlockStartMenu { get; set; }
    public bool GameMode { get; set; }
    public bool AutoGameMode { get; set; } = true;
    public List<string> Disabled { get; set; } = [];
    public List<CustomEntry> Custom { get; set; } = [];

    public sealed class CustomEntry
    {
        public Keys Key { get; set; }
        public Mods Mods { get; set; }
        public string Description { get; set; } = "";
    }

    static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WinKeyEditor");
    static readonly string FilePath = Path.Combine(Dir, "settings.json");
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new();
        }
        catch (Exception) { /* corrupt file: fall back to defaults */ }
        return new();
    }

    public void Save()
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
    }

    public IEnumerable<Shortcut> AllShortcuts() =>
        ShortcutCatalog.BuiltIn.Concat(Custom.Select(c =>
            Shortcut.Of(c.Mods, c.Key, string.IsNullOrWhiteSpace(c.Description) ? "Custom" : c.Description, isCustom: true)));

    public HashSet<int> BlockedCombos() =>
        AllShortcuts().Where(s => Disabled.Contains(s.Id)).SelectMany(s => s.Combos).ToHashSet();
}

/// <summary>Settings that live in the registry rather than the JSON file.</summary>
public static class SystemSettings
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string PolicyKey = @"Software\Microsoft\Windows\CurrentVersion\Policies\System";
    const string AppName = "WinKeyEditor";

    static string StartupCommand => $"\"{Environment.ProcessPath}\" --tray";

    static string? StartupValue
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(AppName) as string;
        }
    }

    public static bool StartWithWindows
    {
        get => StartupValue is not null;
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value)
                key.SetValue(AppName, StartupCommand);
            else
                key.DeleteValue(AppName, throwOnMissingValue: false);
        }
    }

    public static bool StartupPointsHere =>
        string.Equals(StartupValue, StartupCommand, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Win+L is handled by Winlogon and can't be intercepted by a keyboard hook.
    /// The only way to stop it is this policy, which disables workstation locking entirely.
    /// </summary>
    public static bool DisableLock
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(PolicyKey);
            return key?.GetValue("DisableLockWorkstation") is int v && v != 0;
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(PolicyKey);
            if (value)
                key.SetValue("DisableLockWorkstation", 1, RegistryValueKind.DWord);
            else
                key.DeleteValue("DisableLockWorkstation", throwOnMissingValue: false);
        }
    }
}
