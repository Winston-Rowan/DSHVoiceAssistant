using Microsoft.Win32;

namespace DivaVoiceAssistant.Utils;

/// <summary>
/// 开机自启动管理（HKCU\Software\Microsoft\Windows\CurrentVersion\Run）。
/// </summary>
public static class AutoStartHelper
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "DivaVoiceAssistant";

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

    /// <summary>注册开机自启动（指向当前进程的可执行文件）</summary>
    public static void Register()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
            key.SetValue(ValueName, $"\"{Environment.ProcessPath}\"");
            Logger.Info("已注册开机自启动");
        }
        catch (Exception ex)
        {
            Logger.Warn("注册开机自启动失败: " + ex.Message);
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
