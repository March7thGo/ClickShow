using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ClickShow;

internal sealed class MouseHook : IDisposable
{
    private readonly Thread thread;
    private readonly Native.HookProc callback;
    private readonly ClickTracker tracker = new();
    private readonly Action<RippleRequest> emit;
    private Settings settings;
    private int resetMask;
    private uint threadId;
    private nint hook;
    public MouseHook(Settings settings, Action<RippleRequest> emit)
    {
        this.settings = settings; this.emit = emit; callback = OnMouse;
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        thread = new Thread(() =>
        {
            try
            {
                threadId = Native.GetCurrentThreadId();
                Native.PeekMessage(out _, 0, 0, 0, 0);
                hook = Native.SetWindowsHookEx(14, callback, Native.GetModuleHandle(null), 0);
                if (hook == 0) throw new Win32Exception();
                ready.SetResult();
                while (Native.GetMessage(out var message, 0, 0, 0) > 0)
                {
                    if (message.Id == Native.Reset) tracker.Clear(Interlocked.Exchange(ref resetMask, 0));
                    Native.TranslateMessage(ref message); Native.DispatchMessage(ref message);
                }
            }
            catch (Exception ex) { ready.TrySetException(ex); }
            finally { if (hook != 0) Native.UnhookWindowsHookEx(hook); }
        }) { IsBackground = true, Name = "ClickShow.Input" };
        thread.Start(); ready.Task.GetAwaiter().GetResult();
    }
    public void Update(Settings value)
    {
        int mask = !value.Enabled ? 31 : 0;
        for (int i = 0; i < 5; i++) if (!value.Buttons[i].Enabled) mask |= 1 << i;
        Interlocked.Or(ref resetMask, mask); Volatile.Write(ref settings, value);
        if (mask != 0) Native.PostThreadMessage(threadId, Native.Reset, 0, 0);
    }
    public void Reset() { Interlocked.Or(ref resetMask, 31); Native.PostThreadMessage(threadId, Native.Reset, 0, 0); }
    private nint OnMouse(int code, nuint message, nint data)
    {
        // 移动和滚轮事件零分配快速通过，不向绘制线程发消息。
        if (code >= 0 && message is 0x201 or 0x202 or 0x204 or 0x205 or 0x207 or 0x208 or 0x20B or 0x20C)
        {
            var input = Marshal.PtrToStructure<Native.MouseData>(data);
            var current = Volatile.Read(ref settings);
            tracker.Clear(Interlocked.Exchange(ref resetMask, 0));
            MouseButton button = message switch { 0x201 or 0x202 => MouseButton.Left, 0x204 or 0x205 => MouseButton.Right, 0x207 or 0x208 => MouseButton.Middle, _ => (input.Data >> 16) == 1 ? MouseButton.Back : MouseButton.Forward };
            bool down = message is 0x201 or 0x204 or 0x207 or 0x20B;
            if (tracker.Handle(button, down, input.Point.X, input.Point.Y, Stopwatch.GetTimestamp(), current) is { } request) emit(request);
        }
        return Native.CallNextHookEx(hook, code, message, data);
    }
    public void Dispose() { Native.PostThreadMessage(threadId, 0x12, 0, 0); thread.Join(); }
}
