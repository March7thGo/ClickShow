using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Windows.UI.Composition;
using Windows.UI.Composition.Desktop;
using WinRT;

namespace ClickShow;

internal sealed class RippleRenderer : IDisposable
{
    private const int IdleLimit = 16;
    [ComImport, Guid("29E691FA-4567-4DCA-B319-D0F207EB6807"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDesktopInterop
    {
        void CreateDesktopWindowTarget(nint hwnd, [MarshalAs(UnmanagedType.Bool)] bool topmost, out nint target);
        void EnsureOnThread(uint thread);
    }
    [StructLayout(LayoutKind.Sequential)] private struct QueueOptions { public uint Size, ThreadType, Apartment; }
    [DllImport("CoreMessaging.dll")] private static extern int CreateDispatcherQueueController(QueueOptions options, out nint controller);
    private sealed record Overlay(nint Hwnd, DesktopWindowTarget Target, ContainerVisual Root);
    private sealed record Active(long End, CompositionPropertySet Progress, List<(Overlay Window, ShapeVisual Visual, CompositionEllipseGeometry Geometry, CompositionSpriteShape Shape, CompositionColorBrush Brush)> Parts);
    private readonly RippleQueue pending = new();
    private readonly Thread thread;
    private readonly Native.WindowProc windowProc;
    private readonly Action<string> error;
    private readonly List<PixelRect> monitors = [];
    private readonly List<Overlay> windows = [];
    private readonly Stack<Overlay> idle = new();
    private readonly Queue<Active> active = new();
    private Compositor compositor = null!;
    private nint control;
    private uint threadId;
    private Windows.System.DispatcherQueueController? dispatcher;
    public RippleRenderer(Action<string> error)
    {
        this.error = error; windowProc = WndProc;
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        thread = new Thread(() =>
        {
            try
            {
                threadId = Native.GetCurrentThreadId();
                Marshal.ThrowExceptionForHR(CreateDispatcherQueueController(new() { Size = 12, ThreadType = 2, Apartment = 2 }, out var raw));
                dispatcher = MarshalInterface<Windows.System.DispatcherQueueController>.FromAbi(raw); Marshal.Release(raw);
                compositor = new();
                Native.Register("ClickShow.Overlay", windowProc);
                control = Native.CreateWindowEx(0x80, "ClickShow.Overlay", "", 0, 0, 0, 0, 0, 0, 0, Native.GetModuleHandle(null), 0);
                if (control == 0) throw new System.ComponentModel.Win32Exception();
                Rebuild(); ready.SetResult(); Native.Pump();
            }
            catch (Exception ex) { if (!ready.TrySetException(ex)) error($"波纹绘制失败：{ex.Message}"); }
            finally
            {
                Clear(); DestroyWindows();
                if (control != 0) Native.DestroyWindow(control);
                compositor?.Dispose();
                // 关闭专用 DispatcherQueue，继续泵消息直到其资源释放完毕。
                if (dispatcher is not null)
                {
                    var shutdown = dispatcher.ShutdownQueueAsync();
                    shutdown.Completed = (_, _) => Native.PostThreadMessage(threadId, 0x12, 0, 0);
                    Native.Pump();
                }
            }
        }) { IsBackground = true, Name = "ClickShow.Composition" };
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); ready.Task.GetAwaiter().GetResult();
    }
    public void Enqueue(RippleRequest request) { if (pending.Push(request)) Native.PostMessage(control, Native.Work, 0, 0); }
    public void Reset(bool rebuild = false) => Native.PostMessage(control, Native.Reset, rebuild ? 1u : 0u, 0);
    private nint WndProc(nint hwnd, uint message, nuint wParam, nint lParam)
    {
        try
        {
            switch (message)
            {
                case 0x84: return -1; // HTTRANSPARENT；配合 layered/transparent 使其他进程也可点击穿透。
                case 0x21: return 3; // MA_NOACTIVATE
                case Native.Work:
                    while (pending.TryPop(out var request)) Add(request);
                    return 0;
                case Native.Reset: Clear(); pending.Clear(); if (wParam != 0) Rebuild(); return 0;
                case 0x113: Collect(); return 0;
                case 0x7E: if (hwnd == control) Native.PostMessage(control, Native.Reset, 1, 0); return 0;
                case 0x2E0: return 0; // 小窗口移动到另一块屏幕时无需重建。
            }
        }
        catch (Exception ex) { Clear(); error($"波纹绘制失败：{ex.Message}"); }
        return Native.DefWindowProc(hwnd, message, wParam, lParam);
    }
    private void Rebuild()
    {
        Clear(); DestroyWindows(); monitors.Clear();
        Native.EnumDisplayMonitors(0, 0, (nint monitor, nint dc, ref Native.Rect bounds, nint data) => { monitors.Add(new(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom)); return true; }, 0);
    }
    private Overlay Rent(int x, int y, int width, int height)
    {
        Overlay window;
        if (idle.TryPop(out var reused)) window = reused;
        else
        {
            nint hwnd = Native.CreateWindowEx(0x00200000 | 0x00080000 | 0x08000000 | 0x80 | 0x20 | 0x8, "ClickShow.Overlay", "", 0x80000000, x, y, width, height, 0, 0, Native.GetModuleHandle(null), 0);
            if (hwnd == 0) throw new System.ComponentModel.Win32Exception();
            Native.SetLayeredWindowAttributes(hwnd, 0, 255, 2);
            compositor.As<IDesktopInterop>().CreateDesktopWindowTarget(hwnd, true, out var raw);
            var target = MarshalInterface<DesktopWindowTarget>.FromAbi(raw); Marshal.Release(raw);
            var root = compositor.CreateContainerVisual(); target.Root = root;
            window = new(hwnd, target, root); windows.Add(window);
        }
        window.Root.Size = new(width, height);
        Native.SetWindowPos(window.Hwnd, -1, x, y, width, height, 0x10);
        return window;
    }
    private void Add(RippleRequest request)
    {
        double age = Stopwatch.GetElapsedTime(request.Timestamp).TotalMilliseconds;
        if (age >= request.Duration) return;
        Collect(); if (active.Count == 100) Remove(active.Dequeue());
        nint monitor = Native.MonitorFromPoint(new(request.X, request.Y), 2);
        Marshal.ThrowExceptionForHR(Native.GetDpiForMonitor(monitor, 0, out uint dpi, out _));
        float scale = dpi / 96f, diameter = request.Diameter * scale * (request.Small ? .5f : 1f), radius = diameter / 2, stroke = 3 * scale;
        var progress = compositor.CreatePropertySet(); progress.InsertScalar("Growth", 0); progress.InsertScalar("Alpha", .8f);
        var parts = new List<(Overlay, ShapeVisual, CompositionEllipseGeometry, CompositionSpriteShape, CompositionColorBrush)>();
        uint rgb = Convert.ToUInt32(request.Color[1..], 16);
        float extent = radius + stroke;
        var area = PixelRect.Around(request.X, request.Y, extent);
        foreach (var monitorBounds in monitors)
        {
            var part = area.Intersect(monitorBounds);
            if (part.IsEmpty) continue;
            var window = Rent(part.Left, part.Top, part.Width, part.Height);
            var geometry = compositor.CreateEllipseGeometry(); geometry.Center = new(radius + stroke, radius + stroke);
            var brush = compositor.CreateColorBrush(Windows.UI.Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
            var shape = compositor.CreateSpriteShape(geometry); shape.StrokeBrush = brush; shape.StrokeThickness = stroke;
            var visual = compositor.CreateShapeVisual(); visual.Size = new(diameter + stroke * 2); visual.Offset = new(request.X - part.Left - extent, request.Y - part.Top - extent, 0); visual.Shapes.Add(shape);
            // 同一逻辑波纹共享进度；相邻屏幕不按自己的 DPI 再次缩放。
            using var growth = compositor.CreateExpressionAnimation("Vector2(Max(0, r * (0.15 + 0.85 * p.Growth) - w), Max(0, r * (0.15 + 0.85 * p.Growth) - w))"); growth.SetScalarParameter("r", radius); growth.SetScalarParameter("w", stroke / 2); growth.SetReferenceParameter("p", progress); geometry.StartAnimation("Radius", growth);
            using var alpha = compositor.CreateExpressionAnimation("p.Alpha"); alpha.SetReferenceParameter("p", progress); visual.StartAnimation("Opacity", alpha);
            window.Root.Children.InsertAtTop(visual); parts.Add((window, visual, geometry, shape, brush));
            Native.ShowWindow(window.Hwnd, 4);
        }
        using var easing = request.Easing switch
        {
            RippleEasing.Linear => (CompositionEasingFunction)compositor.CreateLinearEasingFunction(),
            RippleEasing.EaseIn => compositor.CreateCubicBezierEasingFunction(new(1f / 3, 0), new(2f / 3, 0)),
            RippleEasing.EaseInOut => compositor.CreateCubicBezierEasingFunction(new(.65f, 0), new(.35f, 1)),
            _ => compositor.CreateCubicBezierEasingFunction(new(1f / 3, 1), new(2f / 3, 1))
        };
        using var animation = compositor.CreateScalarKeyFrameAnimation(); animation.Duration = TimeSpan.FromMilliseconds(request.Duration); animation.InsertKeyFrame(0, 0); animation.InsertKeyFrame(1, 1, easing);
        using var fade = compositor.CreateScalarKeyFrameAnimation(); fade.Duration = animation.Duration; using var linear = compositor.CreateLinearEasingFunction(); fade.InsertKeyFrame(0, .8f); fade.InsertKeyFrame(1, 0, linear);
        progress.StartAnimation("Growth", animation); progress.StartAnimation("Alpha", fade);
        // 延迟输入跳到真实经过的时间，不补播过期动画。
        using var growthController = progress.TryGetAnimationController("Growth"); using var fadeController = progress.TryGetAnimationController("Alpha");
        growthController.Progress = (float)(age / request.Duration); fadeController.Progress = (float)(age / request.Duration);
        active.Enqueue(new(request.Timestamp + (long)(request.Duration * Stopwatch.Frequency / 1000d), progress, parts));
        Native.SetTimer(control, 1, 50, 0);
    }
    private void Collect()
    {
        // 小波纹寿命更短，因此不能只检查队首。
        long now = Stopwatch.GetTimestamp(); int count = active.Count;
        for (int i = 0; i < count; i++) { var item = active.Dequeue(); if (item.End <= now) Remove(item); else active.Enqueue(item); }
        if (active.Count == 0) Native.KillTimer(control, 1);
    }
    private void Remove(Active item)
    {
        foreach (var part in item.Parts)
        {
            Native.ShowWindow(part.Window.Hwnd, 0);
            part.Window.Root.Children.Remove(part.Visual);
            part.Visual.Dispose(); part.Shape.Dispose(); part.Geometry.Dispose(); part.Brush.Dispose();
            if (idle.Count < IdleLimit) idle.Push(part.Window);
            else DestroyWindow(part.Window);
        }
        item.Progress.Dispose();
    }
    private void Clear() { while (active.TryDequeue(out var item)) Remove(item); if (control != 0) Native.KillTimer(control, 1); }
    private void DestroyWindow(Overlay window) { window.Target.Root = null; window.Root.Dispose(); window.Target.Dispose(); Native.DestroyWindow(window.Hwnd); windows.Remove(window); }
    private void DestroyWindows() { while (windows.Count > 0) DestroyWindow(windows[^1]); idle.Clear(); }
    public void Dispose() { Native.PostThreadMessage(threadId, 0x12, 0, 0); thread.Join(); }
}
