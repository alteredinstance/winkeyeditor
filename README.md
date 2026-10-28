# WinKey Editor

A small Windows tray app that disables chosen Windows-key shortcuts, or the Windows key entirely while gaming.

## Features

- **Per-shortcut blocking**: check any of ~40 built-in Win shortcuts (Win+D, Win+Tab, Win+Ctrl+Left/Right for switching desktops, Win+Shift+S, …) or add your own.
- **Game mode**: disables the Windows key completely. Toggle it from the tray, the window, or **Ctrl+Shift+F12**.
- **Auto game mode**: turns the Windows key off automatically while a fullscreen or borderless-fullscreen app has focus.
- **Start menu suppression**: optionally stop a lone Win tap from opening Start.
- **Win+L**: Windows doesn't let anything intercept this key. The option sets the `DisableLockWorkstation` policy instead, which turns off locking completely.
- Start with Windows (launches into the tray). Settings are saved to `%AppData%\WinKeyEditor\settings.json`.

## Build

Requires the .NET 8 SDK.

```powershell
dotnet build -c Release
# or a single self-contained exe:
dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish
```

## Limitations

- When a program running as administrator has focus, Windows doesn't send its keystrokes to the hook. To block keys in those windows too (some games and launchers run elevated), run WinKey Editor as administrator.
- Ctrl+Alt+Del and Win+L can't be blocked by any keyboard hook.
