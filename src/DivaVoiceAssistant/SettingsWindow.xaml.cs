using System.Windows;
using DivaVoiceAssistant.Config;
using DivaVoiceAssistant.Models;
using DivaVoiceAssistant.Services;
using DivaVoiceAssistant.Utils;
using WpfMessageBox = System.Windows.MessageBox;

namespace DivaVoiceAssistant;

/// <summary>
/// 设置窗口：读写 DivaConfig 并保存到 appsettings.json。
/// 说明：麦克风设备、模型名等改动需要重启应用后生效。
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly DivaConfig _config;

    public SettingsWindow(DivaConfig config)
    {
        InitializeComponent();
        _config = config;
        LoadValues();
    }

    private void LoadValues()
    {
        ApiKeyBox.Password = _config.ApiKey;
        ApiHostBox.Text = _config.ApiHost;
        SpeechModelBox.Text = _config.SpeechModel;
        DSHModelBox.Text = _config.DSHModel;
        WakeWordBox.Text = _config.WakeWord;

        SearchEngineBox.ItemsSource = new[] { "baidu", "bing", "google" };
        SearchEngineBox.SelectedItem = _config.SearchEngine.ToLowerInvariant();

        var devices = AudioCaptureService.GetDeviceNames();
        if (devices.Count > 0)
        {
            MicBox.ItemsSource = devices;
            MicBox.SelectedIndex = Math.Clamp(_config.MicDeviceNumber, 0, devices.Count - 1);
        }
        else
        {
            MicBox.Items.Add("默认设备");
            MicBox.SelectedIndex = 0;
        }

        VadSlider.Value = _config.VadThreshold;
        SilenceSlider.Value = _config.SilenceTimeoutMs;
        MaxUtteranceSlider.Value = _config.MaxUtteranceMs;
        TtsModeBox.ItemsSource = new[]
        {
            new { Key = "cloud", Label = "云端自然音色（推荐，听感接近豆包）" },
            new { Key = "local", Label = "本地系统语音（离线）" }
        };
        TtsModeBox.DisplayMemberPath = "Label";
        TtsModeBox.SelectedValuePath = "Key";
        TtsModeBox.SelectedValue = _config.TtsMode;
        TtsVoiceBox.Text = _config.TtsVoice;

        TtsRateSlider.Value = _config.TtsRate;
        TtsVolumeSlider.Value = _config.TtsVolume;

        WakeModeBox.ItemsSource = new[]
        {
            new { Key = "local", Label = "本地识别（免费 · 离线 · 推荐）" },
            new { Key = "api", Label = "云端识别（消耗 API 额度）" },
            new { Key = "off", Label = "关闭（仅快捷键唤醒）" }
        };
        WakeModeBox.DisplayMemberPath = "Label";
        WakeModeBox.SelectedValuePath = "Key";
        WakeModeBox.SelectedValue = _config.WakeMode;

        ConvTimeoutSlider.Value = _config.ConversationTimeoutSeconds;

        AliasesBox.Text = string.Join("\n", _config.AppAliases.Select(kv => kv.Key + "=" + kv.Value));
        try
        {
            InventoryInfo.Text = $"已归档 {AppInventory.GetEntries().Count} 个软件条目（{AppInventory.MarkdownPath}）";
        }
        catch
        {
            InventoryInfo.Text = "软件清单尚未生成";
        }

        HotKeyCheck.IsChecked = _config.HotKeyEnabled;
        TrayCheck.IsChecked = _config.MinimizeToTray;
        AutoStartCheck.IsChecked = AutoStartHelper.IsRegistered();
    }

    private void SaveButton_OnClick(object sender, RoutedEventArgs e)
    {
        _config.ApiKey = ApiKeyBox.Password.Trim();
        _config.ApiHost = ApiHostBox.Text.Trim();
        _config.SpeechModel = SpeechModelBox.Text.Trim();
        _config.DSHModel = DSHModelBox.Text.Trim();
        _config.WakeWord = WakeWordBox.Text.Trim();
        _config.SearchEngine = SearchEngineBox.SelectedItem?.ToString() ?? "baidu";
        _config.MicDeviceNumber = MicBox.SelectedIndex >= 0 ? MicBox.SelectedIndex : 0;
        _config.VadThreshold = VadSlider.Value;
        _config.SilenceTimeoutMs = (int)SilenceSlider.Value;
        _config.MaxUtteranceMs = (int)MaxUtteranceSlider.Value;
        _config.TtsMode = TtsModeBox.SelectedValue?.ToString() ?? "cloud";
        _config.TtsVoice = TtsVoiceBox.Text.Trim();
        _config.TtsRate = (int)TtsRateSlider.Value;
        _config.TtsVolume = (int)TtsVolumeSlider.Value;
        _config.WakeMode = WakeModeBox.SelectedValue?.ToString() ?? "local";
        _config.HotKeyEnabled = HotKeyCheck.IsChecked == true;
        _config.MinimizeToTray = TrayCheck.IsChecked == true;
        _config.ConversationTimeoutSeconds = (int)ConvTimeoutSlider.Value;

        // 应用别名（每行 名称=路径）
        var aliases = new Dictionary<string, string>();
        foreach (var line in (AliasesBox.Text ?? "").Split('\n'))
        {
            if (AppFinder.TryParseAliasLine(line, out var aliasName, out var aliasValue))
                aliases[aliasName] = aliasValue;
        }
        _config.AppAliases = aliases;

        // 开机自启动
        if (AutoStartCheck.IsChecked == true) AutoStartHelper.Register();
        else AutoStartHelper.Unregister();
        _config.AutoStart = AutoStartCheck.IsChecked == true;

        new ConfigService().Save(_config);
        Logger.Info("设置已保存");
        WpfMessageBox.Show("设置已保存。\n部分设置（麦克风设备、模型名等）需要重启应用后生效。",
            "Diva", MessageBoxButton.OK, MessageBoxImage.Information);
        DialogResult = true;
    }

    private void CancelButton_OnClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private void RescanSoftwareButton_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var entries = AppInventory.Refresh();
            WpfMessageBox.Show(
                $"扫描完成：已归档 {entries.Count} 个软件条目。\n清单位置：\n{AppInventory.MarkdownPath}",
                "Diva", MessageBoxButton.OK, MessageBoxImage.Information);
            InventoryInfo.Text = $"已归档 {entries.Count} 个软件条目（{AppInventory.MarkdownPath}）";
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show("扫描失败：" + ex.Message, "Diva", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void VadSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        => VadValue.Text = VadSlider.Value.ToString("0.000");

    private void SilenceSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        => SilenceValue.Text = ((int)SilenceSlider.Value) + "ms";

    private void MaxUtteranceSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        => MaxUtteranceValue.Text = ((int)MaxUtteranceSlider.Value) + "ms";

    private void TtsRateSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        => TtsRateValue.Text = ((int)TtsRateSlider.Value).ToString("+0;-0;0");

    private void TtsVolumeSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        => TtsVolumeValue.Text = ((int)TtsVolumeSlider.Value) + "%";

    private void ConvTimeoutSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        => ConvTimeoutValue.Text = ConvTimeoutSlider.Value <= 0
            ? "关闭"
            : ((int)ConvTimeoutSlider.Value) + "s";
}
