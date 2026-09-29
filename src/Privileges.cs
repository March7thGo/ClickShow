using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;

namespace ClickShow;

internal static class Privileges
{
    public static bool IsElevated { get { using var identity = WindowsIdentity.GetCurrent(); return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator); } }
    public static string Executable => Environment.ProcessPath!;
    public static Process? Launch(bool elevated, string arguments)
    {
        if (elevated) return Process.Start(new ProcessStartInfo(Executable, arguments) { UseShellExecute = true, Verb = "runas" });
        if (!IsElevated) return Process.Start(new ProcessStartInfo(Executable, arguments) { UseShellExecute = false });
        // 使用当前桌面 Explorer 的普通令牌，不能从管理员进程直接继承令牌。
        GetWindowThreadProcessId(GetShellWindow(), out uint pid);
        using var shell = Process.GetProcessById((int)pid);
        if (!OpenProcessToken(shell.Handle, 0x0002 | 0x0008, out nint token)) throw new Win32Exception();
        try
        {
            using var identity = new WindowsIdentity(token);
            if (identity.User!.Value != InstanceGate.UserId) throw new InvalidOperationException("不支持通过其他管理员账户切换权限，请使用当前账户的 UAC 提权。");
            if (!DuplicateTokenEx(token, 0x02000000, 0, 2, 1, out nint primary)) throw new Win32Exception();
            try
            {
                var startup = new StartupInfo { Size = Marshal.SizeOf<StartupInfo>() };
                if (!CreateProcessWithTokenW(primary, 0, Executable, new StringBuilder($"\"{Executable}\" {arguments}"), 0, 0, AppContext.BaseDirectory, ref startup, out var info)) throw new Win32Exception();
                CloseHandle(info.Thread); CloseHandle(info.Process); return Process.GetProcessById((int)info.ProcessId);
            }
            finally { CloseHandle(primary); }
        }
        finally { CloseHandle(token); }
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct StartupInfo
    {
        public int Size; public string? Reserved, Desktop, Title; public uint X, Y, Width, Height, XChars, YChars, Fill, Flags; public ushort Show, ReservedSize; public nint ReservedPointer, Input, Output, Error;
    }
    [StructLayout(LayoutKind.Sequential)] private struct ProcessInfo { public nint Process, Thread; public uint ProcessId, ThreadId; }
    [DllImport("user32")] private static extern nint GetShellWindow();
    [DllImport("user32")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [DllImport("advapi32", SetLastError = true)] private static extern bool OpenProcessToken(nint process, uint access, out nint token);
    [DllImport("advapi32", SetLastError = true)] private static extern bool DuplicateTokenEx(nint token, uint access, nint attributes, int level, int type, out nint duplicate);
    [DllImport("advapi32", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool CreateProcessWithTokenW(nint token, uint flags, string application, StringBuilder command, uint creation, nint environment, string directory, ref StartupInfo startup, out ProcessInfo process);
    [DllImport("kernel32")] private static extern bool CloseHandle(nint handle);
}
