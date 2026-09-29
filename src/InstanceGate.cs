using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Principal;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace ClickShow;

internal sealed class InstanceGate : IDisposable
{
    internal static string UserId { get; } = GetUserId();
    internal static string Key { get; } = $"ClickShow.{UserId}.{Process.GetCurrentProcess().SessionId}";
    private static string GetUserId() { using var identity = WindowsIdentity.GetCurrent(); return identity.User!.Value; }
    private readonly Mutex mutex;
    private readonly CancellationTokenSource cancellation = new();
    private bool owned;
    private Task? server;
    public InstanceGate()
    {
        mutex = new(false);
        mutex.SafeWaitHandle.Dispose();
        mutex.SafeWaitHandle = new SafeWaitHandle(CreateSecuredHandle(false), true);
    }
    [StructLayout(LayoutKind.Sequential)] private struct SecurityAttributes { public int Size; public nint Descriptor; public int Inherit; }
    [DllImport("advapi32", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(string descriptor, uint revision, out nint result, out uint size);
    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint CreateMutexEx(ref SecurityAttributes attributes, string name, uint flags, uint access);
    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint CreateNamedPipe(string name, uint mode, uint pipeMode, uint instances, uint outputSize, uint inputSize, uint timeout, ref SecurityAttributes attributes);
    [DllImport("kernel32")] private static extern nint LocalFree(nint pointer);
    private static nint CreateSecuredHandle(bool pipe)
    {
        // 只允许同一 SID；显式中完整性标签允许该用户普通/提升进程交接。
        if (!ConvertStringSecurityDescriptorToSecurityDescriptor($"O:{UserId}D:P(A;;GA;;;{UserId})S:(ML;;NW;;;ME)", 1, out nint descriptor, out _)) throw new System.ComponentModel.Win32Exception();
        try
        {
            var attributes = new SecurityAttributes { Size = Marshal.SizeOf<SecurityAttributes>(), Descriptor = descriptor };
            nint handle = pipe
                ? CreateNamedPipe(@"\\.\pipe\" + Key, 3 | 0x40000000, 8, 2, 128, 128, 0, ref attributes)
                : CreateMutexEx(ref attributes, @"Local\" + Key, 0, 0x1F0001);
            if (handle == 0 || handle == -1) throw new System.ComponentModel.Win32Exception();
            return handle;
        }
        finally { LocalFree(descriptor); }
    }
    public bool Acquire(int milliseconds = 0)
    {
        try { return owned = mutex.WaitOne(milliseconds); }
        catch (AbandonedMutexException) { return owned = true; }
    }
    public void Release() { if (owned) { mutex.ReleaseMutex(); owned = false; } }
    public void Listen(Func<string, Task<string>> receive, Action<string> error, Action completed)
    {
        var token = cancellation.Token;
        // 首个管道同步创建，使启动错误能够交给主窗口明确报告。
        NamedPipeServerStream? first = new(PipeDirection.InOut, true, false, new SafePipeHandle(CreateSecuredHandle(true), true));
        server = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    using var pipe = first ?? new NamedPipeServerStream(PipeDirection.InOut, true, false, new SafePipeHandle(CreateSecuredHandle(true), true));
                    first = null;
                    await pipe.WaitForConnectionAsync(token);
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(5000);
                    string message = await ReadFrame(pipe, timeout.Token);
                    string reply = await receive(message);
                    await WriteFrame(pipe, reply, timeout.Token);
                    if (reply == "ok" && (message == "stop" || message.StartsWith("commit:", StringComparison.Ordinal))) { completed(); break; }
                }
                catch (Exception ex) when (ex is IOException or OperationCanceledException) { }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or UnauthorizedAccessException) { error("本地实例通信失败：" + ex.Message); break; }
            }
            first?.Dispose();
        });
    }
    public static async Task<string> Send(string message)
    {
        using var timeout = new CancellationTokenSource(5000);
        using var pipe = new NamedPipeClientStream(".", Key, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(timeout.Token);
        await WriteFrame(pipe, message, timeout.Token);
        return await ReadFrame(pipe, timeout.Token);
    }
    private static async Task<string> ReadFrame(Stream stream, CancellationToken token)
    {
        byte[] length = new byte[1]; await stream.ReadExactlyAsync(length, token);
        if (length[0] is 0 or > 64) throw new IOException("通信指令长度无效。");
        byte[] buffer = new byte[length[0]]; await stream.ReadExactlyAsync(buffer, token);
        return System.Text.Encoding.UTF8.GetString(buffer);
    }
    private static async Task WriteFrame(Stream stream, string message, CancellationToken token)
    {
        byte[] buffer = new byte[1 + System.Text.Encoding.UTF8.GetByteCount(message)];
        buffer[0] = checked((byte)(buffer.Length - 1));
        System.Text.Encoding.UTF8.GetBytes(message, buffer.AsSpan(1));
        await stream.WriteAsync(buffer, token);
    }
    public void StopListening() => cancellation.Cancel();
    public void Dispose() { cancellation.Cancel(); Release(); mutex.Dispose(); cancellation.Dispose(); }
}
