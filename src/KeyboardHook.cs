using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WinKeyEditor;

/// <summary>
/// Global low-level keyboard hook that swallows blocked Win+key combinations.
/// Must be created on a thread that pumps messages (the UI thread).
/// </summary>
public sealed class KeyboardHook : IDisposable
{
    const int WH_KEYBOARD_LL = 13;
    const int WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105;
    const int VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12;
    const int INPUT_KEYBOARD = 1;
    const uint KEYEVENTF_KEYUP = 0x0002;

    // Unassigned virtual key. Tapping it while Win is held makes the shell treat
    // Win as part of a combo, so releasing Win doesn't open the Start menu.
    const ushort DummyVk = 0xE8;
    static readonly IntPtr InjectedMarker = new(0x57494E4B); // "WINK"

    readonly LowLevelKeyboardProc proc; // keep the delegate alive while the hook is installed
    readonly IntPtr hook;
    readonly HashSet<uint> swallowed = [];
    volatile HashSet<int> blocked = [];
    readonly HashSet<uint> swallowedWin = [];
    bool winDown;
    bool otherKeyDuringWin;

    public bool Enabled { get; set; } = true;
    public bool BlockStartMenu { get; set; }

    /// <summary>When true the Win keys are swallowed outright (game mode).</summary>
    public bool WinKeyOff { get; set; }

    public KeyboardHook()
    {
        proc = Callback;
        using var module = Process.GetCurrentProcess().MainModule!;
        hook = SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(module.ModuleName), 0);
        if (hook == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Failed to install keyboard hook");
    }

    public void SetBlocked(HashSet<int> combos) => blocked = combos;

    IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0)
            return CallNextHookEx(hook, nCode, wParam, lParam);

        var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
        if (info.dwExtraInfo == InjectedMarker)
            return CallNextHookEx(hook, nCode, wParam, lParam);

        var key = (Keys)info.vkCode;
        int msg = (int)wParam;
        bool down = msg is WM_KEYDOWN or WM_SYSKEYDOWN;
        bool up = msg is WM_KEYUP or WM_SYSKEYUP;

        if (key is Keys.LWin or Keys.RWin)
        {
            if (down && (swallowedWin.Contains(info.vkCode) || (Enabled && WinKeyOff && !winDown)))
            {
                swallowedWin.Add(info.vkCode);
                return 1;
            }
            if (up && swallowedWin.Remove(info.vkCode))
                return 1;

            if (down && !winDown)
            {
                winDown = true;
                otherKeyDuringWin = false;
            }
            else if (up && winDown)
            {
                winDown = false;
                if (Enabled && BlockStartMenu && !otherKeyDuringWin)
                {
                    // Replay the release after a dummy tap so the shell sees a "combo".
                    SendKeys([(DummyVk, false), (DummyVk, true), ((ushort)key, true)]);
                    return 1;
                }
            }
            return CallNextHookEx(hook, nCode, wParam, lParam);
        }

        if (down && winDown)
        {
            otherKeyDuringWin = true;
            if (Enabled && !Shortcut.IsModifier(key) && blocked.Contains(Shortcut.Combo(key, CurrentMods())))
            {
                swallowed.Add(info.vkCode);
                SendKeys([(DummyVk, false), (DummyVk, true)]);
                return 1;
            }
        }
        else if (up && swallowed.Remove(info.vkCode))
        {
            return 1;
        }

        return CallNextHookEx(hook, nCode, wParam, lParam);
    }

    static Mods CurrentMods()
    {
        var mods = Mods.None;
        if (GetAsyncKeyState(VK_SHIFT) < 0) mods |= Mods.Shift;
        if (GetAsyncKeyState(VK_CONTROL) < 0) mods |= Mods.Ctrl;
        if (GetAsyncKeyState(VK_MENU) < 0) mods |= Mods.Alt;
        return mods;
    }

    static void SendKeys((ushort vk, bool up)[] keys)
    {
        var inputs = keys.Select(k => new INPUT
        {
            type = INPUT_KEYBOARD,
            ki = new KEYBDINPUT { wVk = k.vk, dwFlags = k.up ? KEYEVENTF_KEYUP : 0, dwExtraInfo = InjectedMarker },
        }).ToArray();
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    public void Dispose() => UnhookWindowsHookEx(hook);

    delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    struct KBDLLHOOKSTRUCT
    {
        public uint vkCode, scanCode, flags, time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct KEYBDINPUT
    {
        public ushort wVk, wScan;
        public uint dwFlags, time;
        public IntPtr dwExtraInfo;
    }

    // INPUT is a union; pad to the size of its largest member (MOUSEINPUT).
    [StructLayout(LayoutKind.Sequential)]
    struct INPUT
    {
        public int type;
        public KEYBDINPUT ki;
        readonly long pad;
    }

    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll")]
    static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("user32.dll")]
    static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll", SetLastError = true)]
    static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
}
