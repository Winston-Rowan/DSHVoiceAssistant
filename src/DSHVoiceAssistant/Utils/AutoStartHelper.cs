using Microsoft.Win32;

namespace DSHVoiceAssistant.Utils;

/// <summary>
/// 开机自启动管理（HKCU\Software\Microsoft\Windows\CurrentVersion\Run）。
/// </summary>
public static class AutoStartHelper
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "DSHVoiceAssistant";

    /// <summary>是否已注册开机自启动</summary>
    public static bool IsRegistered()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(ValueName) != null;
        }
        catch (Exception ex)
        {
            Logger.Warn("读取自启动状态失败: " + ex.Message);
            return false;
        }
    }

    /// <summary>注册开机自启动（指向当前进程的可执行文件，带 --silent 静默参数）</summary>
    public static void Register()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
            key.SetValue(ValueName, $"\"{Environment.ProcessPath}\" --silent");
            Logger.Info("已注册开机自启动（静默）");
        }
        catch (Exception ex)
        {
            Logger.Warn("注册开机自启动失败: " + ex.Message);
        }
    }

    /// <summary>
    /// 兼容旧安装：已注册的自启动若缺少 --silent 静默参数，自动补上
    /// （老版本注册的是裸 exe 路径，开机启动会弹主窗口）。
    /// </summary>
    public static void EnsureSilentFlag()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            var value = key?.GetValue(ValueName) as string;
            if (string.IsNullOrWhiteSpace(value)) return; // 未注册自启动
            if (!value.Contains("--silent", StringComparison.OrdinalIgnoreCase))
            {
                key!.SetValue(ValueName, $"\"{Environment.ProcessPath}\" --silent");
                Logger.Info("已为开机自启动补充 --silent 静默参数");
            }
        }
        catch (Exception ex)
        {
            Logger.Warn("检查自启动静默参数失败: " + ex.Message);
        }
    }

    /// <summary>取消开机自启动</summary>
    public static void Unregister()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
            Logger.Info("已取消开机自启动");
        }
        catch (Exception ex)
        {
            Logger.Warn("取消开机自启动失败: " + ex.Message);
        }
    }
}
