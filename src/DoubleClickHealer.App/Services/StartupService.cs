using Microsoft.Win32;

namespace DoubleClickHealer.App.Services;

public static class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "DoubleClickHealer";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string;
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        if (enabled)
            key.SetValue(ValueName, $"{ExecutableCommand()} --minimized");
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    private static string ExecutableCommand()
    {
        string path = Environment.ProcessPath ?? string.Empty;
        return path.Contains(' ') ? $"\"{path}\"" : path;
    }
}
