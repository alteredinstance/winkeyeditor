# WinKey Editor

A small Windows tray app that disables chosen Windows-key shortcuts, all the time or only while you're gaming.

## Features

- **Per-shortcut rules.** Every shortcut gets one of four modes:
  | Mode | Behavior |
  |---|---|
  | Allowed | Never blocked |
  | Always blocked | Blocked all the time |
  | Blocked in game mode | Blocked only while game mode is on |
  | Blocked except in game mode | Blocked normally, but works again during game mode |
- **The Windows key itself** and **Start menu on tap** are rows too. Set the Win key to *Blocked in game mode* to turn it off completely while gaming (this is the default).
- **Game mode**: toggle it from the tray, the window, or **Ctrl+Shift+F12** from anywhere. You can also have any fullscreen or borderless-fullscreen app count as game mode automatically.
- ~40 built-in shortcuts (Win+D, Win+Tab, Win+Ctrl+Left/Right for switching desktops, Win+Shift+S, …), plus custom combos.
- Right-click to set several selected rows at once.
- **Win+L**: Windows doesn't let anything intercept this key. The option sets the `DisableLockWorkstation` policy instead, which turns off locking completely.
- Start with Windows (launches into the tray). Settings are saved to `%AppData%\WinKeyEditor\settings.json`.

## Download

Grab the zip from [Releases](../../releases), unzip it somewhere permanent, and run `WinKeyEditor.exe`. No install is needed.

## Building from source

Requirements: Windows and the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or newer.

```powershell
git clone https://github.com/alteredinstance/winkeyeditor.git
cd winkeyeditor
.\build.cmd
```

This produces:
- `publish\win-x64\WinKeyEditor.exe`: a self-contained single exe that runs without .NET installed
- `dist\WinKeyEditor-<version>-win-x64.zip`: the same exe plus this README, ready to share

`build.cmd` is a wrapper around `build.ps1`, so you don't have to change your PowerShell execution policy. Options:

| Option | Effect |
|---|---|
| `-FrameworkDependent` | Small exe (~0.2 MB) that needs the .NET 8 Desktop Runtime installed |
| `-Runtime win-arm64` | Target ARM64 (also `win-x86`; default `win-x64`) |
| `-Configuration Debug` | Debug build |
| `-NoZip` | Skip creating the zip |
| `-Run` | Launch the exe when the build finishes |

For quick iteration you can also use the standard `dotnet build` / `dotnet run`.

## Limitations

- When a program running as administrator has focus, Windows doesn't send its keystrokes to the hook. To block keys in those windows too (some games and launchers run elevated), run WinKey Editor as administrator.
- Ctrl+Alt+Del and Win+L can't be blocked by any keyboard hook.
- The exe is unsigned, so SmartScreen may warn on first launch ("More info" → "Run anyway").

## License

[MIT](LICENSE)
