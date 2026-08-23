using System.Diagnostics;
using System.IO;

namespace DivaVoiceAssistant.Utils;

/// <summary>
/// 简单文件日志。日志写入程序目录下的 logs\diva_yyyyMMdd.log。
/// 所有方法线程安全，可被任意线程调用。
/// </summary>
public static class Logger
{
    private static readonly object Gate = new();
    private static string? _directory;
    private static string? _currentFile;

    /// <summary>初始化日志目录（应用启动时调用一次）</summary>
    public static void Init()
    {
        try
        {
            _directory = Path.Combine(AppContext.BaseDirectory, "logs");
            Directory.CreateDirectory(_directory);
        }
        catch
        {
            // 目录创建失败时静默降级为仅 Debug 输出
        }
    }

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                _currentFile ??= Path.Combine(_directory ?? AppContext.BaseDirectory, $"diva_{DateTime.Now:yyyyMMdd}.log");
                File.AppendAllText(_currentFile, $"[{DateTime.Now:HH:mm:ss.fff}][{level}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // 日志失败不影响主流程
        }
        Debug.WriteLine($"[Diva][{level}] {message}");
    }

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message) => Write("ERROR", message);

    public static void Fatal(string message) => Write("FATAL", message);
}
