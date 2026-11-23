using System.IO;
using System.Text;

namespace DoubleClickHealer.App.Services;

/// <summary>
/// 极简滚动日志。只记录低频率事件（未处理异常、钩子失败、配置错误），
/// 不参与输入热路径，避免影响钩子回调时序。
/// </summary>
internal static class AppLog
{
    private const long MaxBytes = 1024 * 1024;

    private static readonly object Gate = new();
    private static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DoubleClickHealer",
        "logs");
    private static readonly string LogPath = Path.Combine(LogDirectory, "app.log");

    public static void Write(string message, Exception? exception = null)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(LogDirectory);

                var info = new FileInfo(LogPath);
                if (info.Exists && info.Length > MaxBytes)
                    File.Move(LogPath, LogPath + ".old", overwrite: true);

                var text = new StringBuilder()
                    .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"))
                    .Append(" [").Append(Environment.ProcessId).Append("] ")
                    .Append(message);

                if (exception != null)
                    text.AppendLine().Append(exception);

                text.AppendLine();
                File.AppendAllText(LogPath, text.ToString(), Encoding.UTF8);
            }
        }
        catch (Exception)
        {
            // 日志失败不能影响主流程。
        }
    }
}
