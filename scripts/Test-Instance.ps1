$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$executable = Join-Path $root 'artifacts/publish/Full/ClickShow.exe'
Add-Type @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class ClickShowWindowCheck {
    private delegate bool Callback(IntPtr window, IntPtr data);
    [DllImport("user32")] private static extern bool EnumWindows(Callback callback, IntPtr data);
    [DllImport("user32")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32", CharSet=CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int maximum);
    public static bool HasSettings(int process) {
        bool found = false;
        EnumWindows((window, data) => {
            GetWindowThreadProcessId(window, out uint owner);
            if (owner == process && IsWindowVisible(window)) {
                var title = new StringBuilder(256); GetWindowText(window, title, title.Capacity);
                if (title.ToString() == "ClickShow 设置") found = true;
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }
}
'@
$primary = Start-Process $executable -ArgumentList '--silent','--keep-permission' -WindowStyle Hidden -PassThru
try {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $pipeName = "ClickShow.$($identity.User.Value).$([Diagnostics.Process]::GetCurrentProcess().SessionId)"
    $identity.Dispose()
    $pipe = [IO.Pipes.NamedPipeClientStream]::new('.', $pipeName, [IO.Pipes.PipeDirection]::InOut)
    try {
        $pipe.Connect(8000)
        $pipe.Write([byte[]]@(4,112,105,110,103), 0, 5)
        $replyLength = $pipe.ReadByte()
        $reply = [byte[]]::new($replyLength)
        $pipe.ReadExactly($reply)
        if ([Text.Encoding]::UTF8.GetString($reply) -ne 'ok') { throw '主实例尚未就绪' }
    } finally { $pipe.Dispose() }
    $duplicate = Start-Process $executable -ArgumentList '--silent','--keep-permission' -WindowStyle Hidden -PassThru
    if (!$duplicate.WaitForExit(8000) -or $duplicate.ExitCode -ne 0) { throw '重复静默启动没有正常退出' }
    $primary.Refresh()
    if ($primary.HasExited) { throw '主实例意外退出；测试前请先关闭已有 ClickShow' }
    if ([ClickShowWindowCheck]::HasSettings($primary.Id)) { throw '静默启动打开了设置窗口' }
    $manual = Start-Process $executable -ArgumentList '--keep-permission' -WindowStyle Hidden -PassThru
    if (!$manual.WaitForExit(8000) -or $manual.ExitCode -ne 0) { throw '手动重复启动没有正常退出' }
    $primary.Refresh()
    if (![ClickShowWindowCheck]::HasSettings($primary.Id)) { throw '手动重复启动没有激活设置窗口' }
    $shutdown = Start-Process $executable -ArgumentList '--shutdown' -WindowStyle Hidden -PassThru
    if (!$shutdown.WaitForExit(8000) -or $shutdown.ExitCode -ne 0) { throw '退出指令没有正常确认' }
    if (!$primary.WaitForExit(8000) -or $primary.ExitCode -ne 0) { throw '主实例没有正常退出' }
    '通过：静默启动、重复启动、设置激活、退出确认、主实例正常退出。'
} finally {
    if (!$primary.HasExited) {
        $shutdown = Start-Process $executable -ArgumentList '--shutdown' -WindowStyle Hidden -PassThru
        $null = $shutdown.WaitForExit(8000)
    }
}
