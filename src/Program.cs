using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace ClickShow;

internal static class Program
{
    internal static bool Diagnostics;
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            Diagnostics = args.Contains("--diagnostics") || args.Contains("--stress");
            WinRT.ComWrappersSupport.InitializeComWrappers();
            if (args.Length == 4 && args[0] == "--startup")
            {
                if (args[3] != InstanceGate.UserId || args[1] is not ("on" or "off") || args[2] is not ("admin" or "normal")) throw new InvalidOperationException("不支持使用另一管理员账户的凭据配置当前用户自启。");
                StartupRegistration.ApplyLocal(args[1] == "on", args[2] == "admin"); return 0;
            }
            if (args.Contains("--shutdown"))
            {
                using var probe = new InstanceGate();
                if (!probe.Acquire() && (InstanceGate.Send("stop").GetAwaiter().GetResult() != "ok" || !probe.Acquire(10000)))
                    throw new InvalidOperationException("旧实例尚未完成退出，请稍后重试。");
                return 0;
            }
            if (args.Contains("--repair-startup"))
            {
                using var configuration = new SettingsStore();
                if (configuration.Current.Startup) StartupRegistration.Apply(configuration.Current.Startup, configuration.Current.Elevated).GetAwaiter().GetResult();
                return 0;
            }
            if (args.Contains("--cleanup"))
            {
                StartupRegistration.ApplyLocal(false, false);
                if (args.Contains("--delete-settings") && Directory.Exists(SettingsStore.DirectoryPath)) Directory.Delete(SettingsStore.DirectoryPath, true);
                return 0;
            }
            using var gate = new InstanceGate();
            bool handoff = args.Length == 4 && args[0] == "--handoff" && Guid.TryParseExact(args[1], "N", out _);
            if (handoff && args[2] != InstanceGate.UserId) throw new InvalidOperationException("不支持通过其他管理员账户切换权限，原实例仍保持运行。");
            if (!handoff && !gate.Acquire()) { InstanceGate.Send(args.Contains("--silent") ? "ping" : "show").GetAwaiter().GetResult(); return 0; }
            using var store = new SettingsStore();
            if (!handoff && !Diagnostics && store.Current.Elevated != Privileges.IsElevated && !args.Contains("--keep-permission"))
            {
                gate.Release();
                try { Privileges.Launch(store.Current.Elevated, (args.Contains("--silent") ? "--silent " : "") + "--keep-permission --identity " + InstanceGate.UserId)?.Dispose(); return 0; }
                catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223) { if (!gate.Acquire()) return 0; }
            }
            int identityIndex = Array.IndexOf(args, "--identity");
            if (identityIndex >= 0 && (identityIndex + 1 >= args.Length || args[identityIndex + 1] != InstanceGate.UserId)) throw new InvalidOperationException("不支持使用另一管理员账户运行当前用户的配置。");
            Application.Start(parameters =>
            {
                SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
                _ = new App(gate, store, args, handoff);
            });
            return 0;
        }
        catch (Exception ex)
        {
            if (Diagnostics) File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "diagnostics.log"), ex + Environment.NewLine);
            else Native.MessageBox(0, ex.Message, "ClickShow", 0x10);
            return 1;
        }
    }
}

public sealed partial class App : Application
{
    private readonly InstanceGate gate;
    private readonly SettingsStore store;
    private readonly DispatcherQueue dispatcher = DispatcherQueue.GetForCurrentThread();
    private RippleRenderer renderer = null!;
    private MouseHook? hook;
    private TrayIcon? tray;
    private SettingsWindow? window;
    private string? transition;
    private bool relinquished, exiting;
    internal App(InstanceGate gate, SettingsStore store, string[] args, bool handoff)
    {
        this.gate = gate; this.store = store;
        UnhandledException += (_, e) => { if (Program.Diagnostics) File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "diagnostics.log"), e.Exception + Environment.NewLine); };
        InitializeComponent();
        store.Error += Report;
        dispatcher.TryEnqueue(async () =>
        {
            try
            {
                renderer = new(Report);
                tray = new(store.Current.Enabled, ShowSettings, () => Change(store.Current with { Enabled = !store.Current.Enabled }), Quit, Reset, !handoff);
                if (handoff) window = new(this);
                if (handoff)
                {
                    if ((args[3] == "admin") != Privileges.IsElevated) throw new InvalidOperationException("新实例未获得目标运行权限。");
                    if (await InstanceGate.Send("ready:" + args[1]) != "released") throw new InvalidOperationException("权限交接已失效，原实例继续运行。");
                    if (!gate.Acquire()) throw new InvalidOperationException("无法接管原实例。");
                    hook = new(store.Current, renderer.Enqueue);
                    tray.SetVisible(true);
                    if (await InstanceGate.Send("commit:" + args[1]) != "ok") throw new InvalidOperationException("无法完成权限交接。");
                }
                else hook = new(store.Current, renderer.Enqueue);
                gate.Listen(Receive, Report, () => dispatcher.TryEnqueue(Quit));
                if (!args.Contains("--silent")) ShowSettings();
                if (store.LoadError is { } loadError) Report(loadError);
                if (Program.Diagnostics) await RunDiagnostics(args.Contains("--stress"));
            }
            catch (Exception ex)
            {
                // 交接失败先释放监听和互斥量，旧实例才能自动恢复，不能被错误弹窗阻塞。
                if (handoff) Quit(); else { Report(ex.Message); Quit(); }
            }
        });
    }
    public Settings Settings => store.Current;
    public void Change(Settings value)
    {
        bool pause = store.Current.Enabled && !value.Enabled;
        store.Set(value); hook?.Update(value); if (pause) renderer.Reset(); tray?.Update(value.Enabled); window?.Refresh();
    }
    public async Task ChangeStartup(bool startup, bool elevated)
    {
        if (startup || store.Current.Startup) await StartupRegistration.Apply(startup, elevated);
        Change(store.Current with { Startup = startup, Elevated = elevated });
    }
    private void Reset(bool resume)
    {
        hook?.Reset(); renderer.Reset(true);
        if (resume && hook is not null)
        {
            // 会话恢复后重装钩子，不能仅凭旧句柄断言系统仍在调用它。
            hook.Dispose(); hook = null;
            try { hook = new(store.Current, renderer.Enqueue); }
            catch (Exception ex) { Report($"恢复鼠标监听失败，请重启应用：{ex.Message}"); }
        }
    }
    public void ShowSettings() { window ??= new(this); window.Activate(); Native.SetForegroundWindow(WinRT.Interop.WindowNative.GetWindowHandle(window)); }
    public void Report(string message)
    {
        if (Program.Diagnostics) { File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "diagnostics.log"), "ERROR: " + message + Environment.NewLine); return; }
        void Show() => Native.MessageBox(window is null ? 0 : WinRT.Interop.WindowNative.GetWindowHandle(window), message, "ClickShow", 0x10);
        if (dispatcher.HasThreadAccess) Show(); else dispatcher.TryEnqueue(Show);
    }
    private async Task RunDiagnostics(bool stress)
    {
        string log = Path.Combine(AppContext.BaseDirectory, "diagnostics.log");
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        File.AppendAllText(log, $"Started {DateTimeOffset.Now:O}; elevated={Privileges.IsElevated}\n");
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var baseline = process.TotalProcessorTime;
        var points = new List<Native.Point>();
        Native.EnumDisplayMonitors(0, 0, (nint monitor, nint dc, ref Native.Rect bounds, nint data) =>
        {
            int x = bounds.Left + (bounds.Right - bounds.Left) / 2, y = bounds.Top + (bounds.Bottom - bounds.Top) / 2;
            points.Add(new(x, y));
            points.Add(new(bounds.Right - 1, y));
            return true;
        }, 0);
        int count = stress ? 6000 : 20;
        for (int i = 0; i < count; i++)
        {
            // 按单调时钟调度，避免 Windows 定时器粒度把 100 次/秒降为约 64 次/秒。
            double due = i * (stress ? 10d : 30d);
            if (due > watch.Elapsed.TotalMilliseconds) await Task.Delay(TimeSpan.FromMilliseconds(due - watch.Elapsed.TotalMilliseconds));
            var point = points[i % points.Count];
            renderer.Enqueue(new(point.X, point.Y, System.Diagnostics.Stopwatch.GetTimestamp(), 100, "#3B82F6", i % 2 == 0));
            if (i % 1000 == 0) { process.Refresh(); File.AppendAllText(log, $"Sample requests={i}; workingSet={process.WorkingSet64}; handles={process.HandleCount}\n"); }
        }
        await Task.Delay(1000); process.Refresh();
        File.AppendAllText(log, $"Completed requests={count}; elapsed={watch.Elapsed.TotalSeconds:F2}s; CPU={(process.TotalProcessorTime - baseline).TotalSeconds:F2}s; workingSet={process.WorkingSet64}; handles={process.HandleCount}\n");
        var idleCpu = process.TotalProcessorTime;
        await Task.Delay(2000); process.Refresh();
        File.AppendAllText(log, $"Idle CPU={(process.TotalProcessorTime - idleCpu).TotalMilliseconds:F0}ms/2000ms; workingSet={process.WorkingSet64}; handles={process.HandleCount}\n");
        Quit();
    }
    private Task<string> Receive(string message)
    {
        var response = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        dispatcher.TryEnqueue(() =>
        {
            string result = "invalid";
            switch (message)
            {
                case "ping": result = "ok"; break;
                case "show": ShowSettings(); result = "ok"; break;
                case "stop": result = "ok"; break;
                default:
                    if (transition is not null && message == "ready:" + transition && !relinquished)
                    {
                        hook?.Dispose(); hook = null; renderer.Reset(); tray?.SetVisible(false); gate.Release(); relinquished = true; result = "released";
                    }
                    else if (transition is not null && message == "commit:" + transition && relinquished)
                    {
                        result = "ok";
                    }
                    break;
            }
            response.SetResult(result);
        });
        return response.Task;
    }
    public async Task Restart()
    {
        if (transition is not null) return;
        store.Flush(); transition = Guid.NewGuid().ToString("N");
        try
        {
            using var child = Privileges.Launch(store.Current.Elevated, $"--handoff {transition} {InstanceGate.UserId} {(store.Current.Elevated ? "admin" : "normal")}") ?? throw new InvalidOperationException("新实例未启动。");
            await Task.WhenAny(Task.Delay(10000), child.WaitForExitAsync());
            if (exiting) return;
            if (relinquished)
            {
                // 新进程接管失败时，先结束该次子进程，再恢复旧实例，避免双监听。
                if (!child.HasExited) { child.Kill(); await child.WaitForExitAsync(); }
                if (!gate.Acquire()) throw new InvalidOperationException("无法恢复实例锁，请重新启动 ClickShow。");
                hook = new(store.Current, renderer.Enqueue); tray?.SetVisible(true); relinquished = false;
            }
            throw new InvalidOperationException("权限切换未完成，当前实例继续运行。");
        }
        finally { transition = null; }
    }
    public void Quit()
    {
        if (exiting) return; exiting = true;
        hook?.Dispose(); tray?.Dispose(); renderer?.Dispose(); store.Flush(); gate.StopListening(); gate.Release(); Exit();
    }
}
