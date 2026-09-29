using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ClickShow;

internal static class Native
{
    internal const uint Work = 0x8001, Reset = 0x8002, Tray = 0x8003;
    internal delegate nint WindowProc(nint hwnd, uint message, nuint wParam, nint lParam);
    internal delegate nint HookProc(int code, nuint wParam, nint lParam);
    internal delegate bool MonitorProc(nint monitor, nint dc, ref Rect rectangle, nint data);
    [StructLayout(LayoutKind.Sequential)] internal struct Point { public int X, Y; public Point(int x, int y) { X = x; Y = y; } }
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] internal struct Message { public nint Hwnd; public uint Id; public nuint WParam; public nint LParam; public uint Time; public Point Point; public uint Private; }
    [StructLayout(LayoutKind.Sequential)] internal struct MouseData { public Point Point; public uint Data, Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] internal struct WindowClass
    {
        public uint Size, Style; public WindowProc Proc; public int ClassExtra, WindowExtra; public nint Instance, Icon, Cursor, Background;
        public string? Menu; public string Name; public nint SmallIcon;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] internal struct NotifyIcon
    {
        public uint Size; public nint Hwnd; public uint Id, Flags, Callback; public nint Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string Title;
        public uint InfoFlags; public Guid Guid; public nint BalloonIcon;
    }
    [DllImport("user32", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern ushort RegisterClassEx(ref WindowClass value);
    [DllImport("user32", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern nint CreateWindowEx(uint exStyle, string className, string title, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);
    [DllImport("user32")] internal static extern nint DefWindowProc(nint hwnd, uint message, nuint wParam, nint lParam);
    [DllImport("user32")] internal static extern bool DestroyWindow(nint hwnd);
    [DllImport("user32")] internal static extern bool ShowWindow(nint hwnd, int command);
    [DllImport("user32")] internal static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32")] internal static extern bool SetLayeredWindowAttributes(nint hwnd, uint key, byte alpha, uint flags);
    [DllImport("user32")] internal static extern int GetMessage(out Message message, nint hwnd, uint min, uint max);
    [DllImport("user32")] internal static extern bool PeekMessage(out Message message, nint hwnd, uint min, uint max, uint remove);
    [DllImport("user32")] internal static extern bool TranslateMessage(ref Message message);
    [DllImport("user32")] internal static extern nint DispatchMessage(ref Message message);
    [DllImport("user32", SetLastError = true)] internal static extern bool PostThreadMessage(uint thread, uint message, nuint wParam, nint lParam);
    [DllImport("user32")] internal static extern bool PostMessage(nint hwnd, uint message, nuint wParam, nint lParam);
    [DllImport("user32")] internal static extern void PostQuitMessage(int code);
    [DllImport("user32")] internal static extern nuint SetTimer(nint hwnd, nuint id, uint interval, nint callback);
    [DllImport("user32")] internal static extern bool KillTimer(nint hwnd, nuint id);
    [DllImport("kernel32")] internal static extern uint GetCurrentThreadId();
    [DllImport("kernel32", CharSet = CharSet.Unicode)] internal static extern nint GetModuleHandle(string? module);
    [DllImport("user32", SetLastError = true)] internal static extern nint SetWindowsHookEx(int id, HookProc callback, nint module, uint thread);
    [DllImport("user32")] internal static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32")] internal static extern nint CallNextHookEx(nint hook, int code, nuint wParam, nint lParam);
    [DllImport("user32")] internal static extern bool EnumDisplayMonitors(nint dc, nint clip, MonitorProc callback, nint data);
    [DllImport("user32")] internal static extern nint MonitorFromPoint(Point point, uint flags);
    [DllImport("shcore")] internal static extern int GetDpiForMonitor(nint monitor, int type, out uint x, out uint y);
    [DllImport("user32", CharSet = CharSet.Unicode)] internal static extern uint RegisterWindowMessage(string name);
    [DllImport("shell32", CharSet = CharSet.Unicode)] internal static extern bool Shell_NotifyIcon(uint command, ref NotifyIcon data);
    [DllImport("user32", CharSet = CharSet.Unicode)] internal static extern nint LoadImage(nint instance, string path, uint type, int width, int height, uint flags);
    [DllImport("user32")] internal static extern bool DestroyIcon(nint icon);
    [DllImport("user32")] internal static extern bool GetCursorPos(out Point point);
    [DllImport("user32")] internal static extern nint CreatePopupMenu();
    [DllImport("user32", CharSet = CharSet.Unicode)] internal static extern bool AppendMenu(nint menu, uint flags, nuint id, string? text);
    [DllImport("user32")] internal static extern uint TrackPopupMenu(nint menu, uint flags, int x, int y, int reserved, nint hwnd, nint rectangle);
    [DllImport("user32")] internal static extern bool DestroyMenu(nint menu);
    [DllImport("user32")] internal static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32", CharSet = CharSet.Unicode)] internal static extern int MessageBox(nint hwnd, string text, string title, uint type);
    [DllImport("wtsapi32")] internal static extern bool WTSRegisterSessionNotification(nint hwnd, uint flags);
    [DllImport("wtsapi32")] internal static extern bool WTSUnRegisterSessionNotification(nint hwnd);
    internal static void Register(string name, WindowProc proc)
    {
        var wc = new WindowClass { Size = (uint)Marshal.SizeOf<WindowClass>(), Name = name, Proc = proc, Instance = GetModuleHandle(null) };
        if (RegisterClassEx(ref wc) == 0) throw new Win32Exception();
    }
    internal static void Pump()
    {
        int result;
        while ((result = GetMessage(out var message, 0, 0, 0)) > 0) { TranslateMessage(ref message); DispatchMessage(ref message); }
        if (result < 0) throw new Win32Exception();
    }
}
