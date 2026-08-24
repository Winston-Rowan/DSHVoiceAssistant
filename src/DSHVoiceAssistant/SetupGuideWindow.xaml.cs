using System.Diagnostics;
using System.Windows;
using DSHVoiceAssistant.Models;
using WpfMessageBox = System.Windows.MessageBox;

namespace DSHVoiceAssistant;

/// <summary>
/// 首次运行新手引导：未配置百炼 API 密钥时弹出，
/// 引导小白用户完成 获取密钥 → 填入设置 → 开始使用。
/// </summary>
public partial class SetupGuideWindow : Window
{
    private readonly DSHConfig _config;

    public SetupGuideWindow(DSHConfig config)
    {
        InitializeComponent();
        _config = config;
    }

    private void OpenBailianConsole_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            // 阿里云百炼控制台（API-KEY 管理入口）
            Process.Start(new ProcessStartInfo("https://bailian.console.aliyun.com/") { UseShellExecute = true });
        }
        catch
        {
            WpfMessageBox.Show("无法打开浏览器，请手动访问：https://bailian.console.aliyun.com/",
                "DSH", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void OpenSettings_OnClick(object sender, RoutedEventArgs e)
    {
        var settings = new SettingsWindow(_config) { Owner = this };
        settings.ShowDialog();
    }

    private void Later_OnClick(object sender, RoutedEventArgs e) => Close();
}
