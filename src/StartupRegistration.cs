using Microsoft.Win32;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ClickShow;

internal static class StartupRegistration
{
    private const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private static string TaskName => "ClickShow-" + InstanceGate.UserId;
    private static dynamic Scheduler()
    {
        dynamic service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")!)!;
        service.Connect(); return service;
    }
    private static dynamic? FindTask(dynamic folder)
    {
        try { return folder.GetTask(TaskName); }
        catch (COMException ex) when ((uint)ex.HResult == 0x80070002) { return null; }
    }
    public static async Task Apply(bool enabled, bool elevated)
    {
        dynamic service = Scheduler(); dynamic folder = service.GetFolder(@"\");
        bool hasTask = FindTask(folder) is not null;
        if ((enabled && elevated || hasTask) && !Privileges.IsElevated)
        {
            using var helper = Privileges.Launch(true, $"--startup {(enabled ? "on" : "off")} {(elevated ? "admin" : "normal")} {InstanceGate.UserId}") ?? throw new InvalidOperationException("无法启动自启配置程序。");
            await helper.WaitForExitAsync();
            if (helper.ExitCode != 0) throw new InvalidOperationException("自启配置失败或授权被取消，原设置已保留。");
        }
        else ApplyLocal(enabled, elevated);
    }
    internal static void ApplyLocal(bool enabled, bool elevated)
    {
        dynamic service = Scheduler(); dynamic folder = service.GetFolder(@"\"); dynamic? oldTask = FindTask(folder);
        string? oldXml = oldTask?.Xml;
        using var key = Registry.CurrentUser.CreateSubKey(RunPath);
        string? oldRun = key.GetValue("ClickShow") as string;
        try
        {
            if (enabled && elevated)
            {
                dynamic task = service.NewTask(0);
                task.RegistrationInfo.Description = "ClickShow 当前用户登录后以管理员权限运行";
                task.Principal.UserId = InstanceGate.UserId; task.Principal.LogonType = 3; task.Principal.RunLevel = 1;
                task.Settings.DisallowStartIfOnBatteries = false; task.Settings.StopIfGoingOnBatteries = false;
                task.Settings.ExecutionTimeLimit = "PT0S"; task.Settings.MultipleInstances = 2;
                dynamic trigger = task.Triggers.Create(9); trigger.UserId = InstanceGate.UserId;
                dynamic action = task.Actions.Create(0); action.Path = Privileges.Executable; action.Arguments = "--silent"; action.WorkingDirectory = AppContext.BaseDirectory;
                folder.RegisterTaskDefinition(TaskName, task, 6, InstanceGate.UserId, null, 3, null);
                key.DeleteValue("ClickShow", false);
            }
            else
            {
                if (enabled) key.SetValue("ClickShow", $"\"{Privileges.Executable}\" --silent"); else key.DeleteValue("ClickShow", false);
                if (oldTask is not null) folder.DeleteTask(TaskName, 0);
            }
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException or System.IO.IOException)
        {
            // 外部系统配置是事务边界，失败时恢复原有有效入口。
            if (oldRun is null) key.DeleteValue("ClickShow", false); else key.SetValue("ClickShow", oldRun);
            if (oldXml is not null) folder.RegisterTask(TaskName, oldXml, 6, InstanceGate.UserId, null, 3, null);
            else if (FindTask(folder) is not null) folder.DeleteTask(TaskName, 0);
            throw new InvalidOperationException("更新开机自启失败：" + ex.Message, ex);
        }
    }
}
