using System.Diagnostics;
using System.Security.Principal;

namespace DoubleClickHealer.App.Services;

/// <summary>
/// 增强模式：通过登录任务计划以最高权限启动，覆盖以管理员运行的程序。
/// 注册任务需要一次 UAC 确认，之后每次登录静默提权。
/// </summary>
public static class ElevationService
{
    private const string TaskName = "DoubleClickHealer";

    public static bool IsElevated
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    public static bool IsTaskInstalled()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("schtasks.exe")
            {
                Arguments = $"/Query /TN \"{TaskName}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });

            if (process == null) return false;
            process.WaitForExit(4000);
            return process.HasExited && process.ExitCode == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>注册提权任务。返回 false 表示用户取消了 UAC 或注册失败。</summary>
    public static bool Enable()
    {
        string executable = Environment.ProcessPath ?? string.Empty;
        string command = executable.Contains(' ') ? $"\"{executable}\" --minimized" : $"{executable} --minimized";

        string arguments =
            $"/Create /F /SC ONLOGON /RL HIGHEST /TN \"{TaskName}\" /TR \"{command.Replace("\"", "\\\"")}\"";

        return RunElevated(arguments);
    }

    public static bool Disable()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("schtasks.exe")
            {
                Arguments = $"/Delete /F /TN \"{TaskName}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            });

            if (process == null) return false;
            process.WaitForExit(4000);
            return process.HasExited && process.ExitCode == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool RunElevated(string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("schtasks.exe")
            {
                Arguments = arguments,
                UseShellExecute = true,
                Verb = "runas",
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            });

            if (process == null) return false;
            process.WaitForExit(15000);
            return process.HasExited && process.ExitCode == 0;
        }
        catch (Exception)
        {
            // 用户在 UAC 中点了取消。
            return false;
        }
    }
}
