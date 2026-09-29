using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ClickShow;

internal sealed class TrayIcon : IDisposable
{
    private readonly Native.WindowProc proc;
    private readonly Action show, toggle, exit;
    private readonly Action<bool> reset;
    private readonly nint hwnd, enabledIcon, disabledIcon;
    private readonly uint taskbarCreated = Native.RegisterWindowMessage("TaskbarCreated");
    private Native.NotifyIcon data;
    private bool enabled;
    private bool visible;
    public TrayIcon(bool enabled, Action show, Action toggle, Action exit, Action<bool> reset, bool visible = true)
    {
        this.show = show; this.toggle = toggle; this.exit = exit; this.reset = reset;
        proc = WndProc; Native.Register("ClickShow.Tray", proc);
        hwnd = Native.CreateWindowEx(0x80, "ClickShow.Tray", "ClickShow", 0, 0, 0, 0, 0, 0, 0, Native.GetModuleHandle(null), 0);
        if (hwnd == 0) throw new System.ComponentModel.Win32Exception();
        enabledIcon = Native.LoadImage(0, Path.Combine(AppContext.BaseDirectory, "Assets", "ClickShow.ico"), 1, 0, 0, 0x10 | 0x40);
        disabledIcon = Native.LoadImage(0, Path.Combine(AppContext.BaseDirectory, "Assets", "ClickShow-disabled.ico"), 1, 0, 0, 0x10 | 0x40);
        data = new() { Size = (uint)Marshal.SizeOf<Native.NotifyIcon>(), Hwnd = hwnd, Id = 1, Flags = 1 | 2 | 4, Callback = Native.Tray, Tip = "", Info = "", Title = "" };
        this.enabled = enabled;
        SetVisible(visible);
        Native.WTSRegisterSessionNotification(hwnd, 0);
    }
    public void Update(bool value, bool add = false)
    {
        enabled = value; data.Icon = value ? enabledIcon : disabledIcon; data.Tip = "ClickShow · " + (value ? "已启用" : "已停用");
        if (visible && !Native.Shell_NotifyIcon(add ? 0u : 1u, ref data)) throw new InvalidOperationException("无法创建或更新系统托盘图标。");
    }
    public void SetVisible(bool value)
    {
        visible = value;
        if (value) Update(enabled, true); else Native.Shell_NotifyIcon(2, ref data);
    }
    private nint WndProc(nint window, uint message, nuint wParam, nint lParam)
    {
        if (message == taskbarCreated) { Update(enabled, true); return 0; }
        if (message == 0x2B1 || message == 0x218)
        {
            reset(message == 0x2B1 ? wParam is 1 or 3 or 8 : wParam is 7 or 18);
            return 0;
        }
        if (message == 0x11) return 1;
        if (message == 0x10) { exit(); return 0; }
        if (message == 0x16 && wParam != 0) { exit(); return 0; }
        if (message == Native.Tray)
        {
            if ((uint)lParam == 0x202) show();
            if ((uint)lParam == 0x205)
            {
                nint menu = Native.CreatePopupMenu();
                try
                {
                    Native.AppendMenu(menu, 0, 1, enabled ? "停用" : "启用"); Native.AppendMenu(menu, 0, 2, "设置"); Native.AppendMenu(menu, 0, 3, "关于"); Native.AppendMenu(menu, 0x800, 0, null); Native.AppendMenu(menu, 0, 4, "退出");
                    Native.GetCursorPos(out var point); Native.SetForegroundWindow(hwnd);
                    uint selected = Native.TrackPopupMenu(menu, 0x100 | 0x2, point.X, point.Y, 0, hwnd, 0);
                    Native.PostMessage(hwnd, 0, 0, 0);
                    switch (selected) { case 1: toggle(); break; case 2: show(); break; case 3: Process.Start(new ProcessStartInfo("https://github.com/March7thGo/ClickShow") { UseShellExecute = true }); break; case 4: exit(); break; }
                }
                finally { Native.DestroyMenu(menu); }
            }
            return 0;
        }
        return Native.DefWindowProc(window, message, wParam, lParam);
    }
    public void Dispose() { Native.WTSUnRegisterSessionNotification(hwnd); Native.Shell_NotifyIcon(2, ref data); Native.DestroyIcon(enabledIcon); Native.DestroyIcon(disabledIcon); Native.DestroyWindow(hwnd); }
}
