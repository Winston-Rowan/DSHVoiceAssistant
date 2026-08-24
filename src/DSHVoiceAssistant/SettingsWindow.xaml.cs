using System.Windows;
using System.Windows.Input;
using DSHVoiceAssistant.Config;
using DSHVoiceAssistant.Models;
using DSHVoiceAssistant.Services;
using DSHVoiceAssistant.Utils;
using WpfMessageBox = System.Windows.MessageBox;
using InputKeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace DSHVoiceAssistant;

/// <summary>
/// 设置窗口：读写 DSHConfig 并保存到 appsettings.json。
/// 说明：麦克风设备、模型名等改动需要重启应用后生效；快捷键改动保存后立即生效。
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly DSHConfig _config;
    private bool _capturingHotKey;

    public SettingsWindow(DSHConfig config)
    {
        InitializeComponent();
        _config = config;
        PreviewKeyDown += OnPreviewKeyDown;
        LoadValues();
    }

    private void LoadValues()
    {
        ApiKeyBox.Password = _config.ApiKey;
        ApiHostBox.Text = _config.ApiHost;
        SpeechModelBox.Text = _config.SpeechModel;

        DshApiKeyBox.Password = _config.DshApiKey;
        DshApiHostBox.Text = _config.DshApiHost;

        // DSH 指令模型：下拉选择（DeepSeek 官方 / 百炼工作台常用模型）
        DSHModelBox.ItemsSource = new[]
        {
            "deepseek-v4-flash", "deepseek-chat", "deepseek-reasoner", "deepseek-r1",
            "qwen3-32b", "qwen-plus", "qwen-max"
        };
        DSHModelBox.SelectedItem = DSHModelBox.Items.Contains(_config.DSHModel)
            ? _config.DSHModel
            : "deepseek-v4-flash";
        WakeWordBox.Text = _config.WakeWord;
        WakeVariantsBox.Text = string.Join(", ", _config.WakeWordVariants);
        NameBox.Text = _config.AssistantName;

        SearchEngineBox.ItemsSource = new[] { "baidu", "bing", "google" };
        SearchEngineBox.SelectedItem = _config.SearchEngine.ToLowerInvariant();

        PermissionModeBox.ItemsSource = new[]
        {
            new { Key = "full", Label = "完全访问（任意命令/文件/系统操作直接执行，推荐）" },
            new { Key = "workspace", Label = "工作区受限（文件与命令仅限 Git 项目目录）" },
            new { Key = "readonly", Label = "只读（仅查询/打开/搜索，禁止执行与修改）" }
        };
        PermissionModeBox.DisplayMemberPath = "Label";
        PermissionModeBox.SelectedValuePath = "Key";
        PermissionModeBox.SelectedValue = _config.PermissionMode;

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
        MicGainSlider.Value = Math.Clamp(_config.MicGain, 1, 8);
        MicGainValue.Text = MicGainSlider.Value.ToString("0.0") + "x";
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

        ReportLangBox.ItemsSource = new[]
        {
            new { Key = "zh", Label = "中文（普通话）" },
            new { Key = "en", Label = "English（英文）" }
        };
        ReportLangBox.DisplayMemberPath = "Label";
        ReportLangBox.SelectedValuePath = "Key";
        ReportLangBox.SelectedValue = _config.ReportLanguage;

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
        HistorySlider.Value = _config.DshHistoryRounds;
        HistoryValue.Text = HistorySlider.Value <= 0
            ? "关闭"
            : ((int)HistorySlider.Value) + " 轮";

        AliasesBox.Text = string.Join("\n", _config.AppAliases.Select(kv => kv.Key + "=" + kv.Value));
        GitPathBox.Text = _config.GitProjectPath;
        try
        {
            InventoryInfo.Text = $"已归档 {AppInventory.GetEntries().Count} 个软件条目（{AppInventory.MarkdownPath}）";
        }
        catch
        {
            InventoryInfo.Text = "软件清单尚未生成";
        }

        HotKeyCheck.IsChecked = _config.HotKeyEnabled;
        HotKeyDisplay.Text = _config.HotKeyCombo;
        SelfVoiceFilterCheck.IsChecked = _config.SelfVoiceFilter;
        EdgeGlowCheck.IsChecked = _config.EdgeGlowEnabled;
        OverlayCheck.IsChecked = _config.ConversationOverlayEnabled;

        // 浮层文字样式
        OverlayFontBox.ItemsSource = new[]
        {
            "Microsoft YaHei UI", "微软雅黑", "黑体", "楷体", "宋体", "Consolas"
        };
        OverlayFontBox.SelectedItem = _config.OverlayFontFamily;
        OverlaySizeSlider.Value = Math.Clamp(_config.OverlayFontSize, 14, 48);
        OverlaySizeValue.Text = ((int)OverlaySizeSlider.Value) + "px";
        var colors = new Dictionary<string, string>
        {
            ["白色"] = "#FFFFFF",
            ["浅灰"] = "#E8E8E8",
            ["青色"] = "#5EE0D9",
            ["天蓝"] = "#7EC8FF",
            ["粉色"] = "#FF8FB1",
            ["浅绿"] = "#A8F0A8",
            ["橙色"] = "#FFB34D",
            ["黄色"] = "#FFE066"
        };
        OverlayColorBox.ItemsSource = colors.Keys.ToList();
        OverlayColorBox.SelectedItem = colors.FirstOrDefault(kv => kv.Value.Equals(_config.OverlayTextColor, StringComparison.OrdinalIgnoreCase)).Key;
        OverlayColorText.Text = _config.OverlayTextColor;
        OverlayShadowCheck.IsChecked = _config.OverlayTextShadow;

        TrayCheck.IsChecked = _config.MinimizeToTray;
        AutoStartCheck.IsChecked = AutoStartHelper.IsRegistered();

        // 助手人格（默认梁文峰；空=默认）
        PersonaBox.Text = string.IsNullOrWhiteSpace(_config.AssistantPersona)
            ? DSHConfig.DefaultPersona
            : _config.AssistantPersona;
    }

    private void RestoreDefaultPersona_OnClick(object sender, RoutedEventArgs e)
    {
        PersonaBox.Text = DSHConfig.DefaultPersona;
        ApplyStatus.Text = "已恢复为默认梁文峰人格，点击「应用」或「保存」生效";
    }

    /// <summary>应用按钮：保存设置（窗口保持打开），状态栏提示；部分设置需重启生效</summary>
    private void ApplyButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!ApplySettings()) return;
        ApplyStatus.Text = "✓ 设置已应用（麦克风设备、模型名等部分设置需重启应用后生效）";
        Logger.Info("设置已应用");
    }

    /// <summary>保存按钮：保存设置并关闭窗口</summary>
    private void SaveButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!ApplySettings()) return;
        DialogResult = true;
    }

    /// <summary>把界面值写入配置并保存；快捷键非法时提示并保留原值。返回是否成功。</summary>
    private bool ApplySettings()
    {
        _config.ApiKey = ApiKeyBox.Password.Trim();
        _config.ApiHost = ApiHostBox.Text.Trim();
        _config.SpeechModel = SpeechModelBox.Text.Trim();
        _config.DshApiKey = DshApiKeyBox.Password.Trim();
        _config.DshApiHost = DshApiHostBox.Text.Trim();
        _config.DSHModel = DSHModelBox.SelectedItem?.ToString() ?? "deepseek-v4-flash";
        _config.WakeWord = WakeWordBox.Text.Trim();
        _config.AssistantName = NameBox.Text.Trim();
        _config.AssistantPersona = string.Equals(PersonaBox.Text.Trim(), DSHConfig.DefaultPersona, StringComparison.Ordinal)
            ? ""
            : PersonaBox.Text.Trim();

        // 唤醒词变体：逗号/顿号/空格分隔；留空则按主词自动生成
        var variantText = (WakeVariantsBox.Text ?? "").Trim();
        var variants = variantText
            .Split(new[] { ',', '，', '、', ';', '；' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(v => v.Length > 0)
            .Distinct()
            .ToList();
        _config.WakeWordVariants = variants.Count > 0
            ? variants.ToArray()
            : WakeWordMatcher.BuildDefaultVariants(_config.WakeWord);
        _config.SearchEngine = SearchEngineBox.SelectedItem?.ToString() ?? "baidu";
        _config.PermissionMode = PermissionModeBox.SelectedValue?.ToString() ?? "full";
        _config.MicDeviceNumber = MicBox.SelectedIndex >= 0 ? MicBox.SelectedIndex : 0;
        _config.MicGain = MicGainSlider.Value;
        _config.VadThreshold = VadSlider.Value;
        _config.SilenceTimeoutMs = (int)SilenceSlider.Value;
        _config.MaxUtteranceMs = (int)MaxUtteranceSlider.Value;
        _config.TtsMode = TtsModeBox.SelectedValue?.ToString() ?? "cloud";
        _config.TtsVoice = TtsVoiceBox.Text.Trim();
        _config.ReportLanguage = ReportLangBox.SelectedValue?.ToString() ?? "zh";
        _config.TtsRate = (int)TtsRateSlider.Value;
        _config.TtsVolume = (int)TtsVolumeSlider.Value;
        _config.WakeMode = WakeModeBox.SelectedValue?.ToString() ?? "local";
        _config.HotKeyEnabled = HotKeyCheck.IsChecked == true;
        var combo = HotKeyDisplay.Text.Trim();
        if (!HotKeyService.TryParse(combo, out _, out _, out var comboError))
        {
            WpfMessageBox.Show("快捷键无效：" + comboError + "\n已保留原快捷键。",
                "DSH", MessageBoxButton.OK, MessageBoxImage.Warning);
            combo = _config.HotKeyCombo;
        }
        _config.HotKeyCombo = combo;
        _config.SelfVoiceFilter = SelfVoiceFilterCheck.IsChecked == true;
        _config.EdgeGlowEnabled = EdgeGlowCheck.IsChecked == true;
        _config.ConversationOverlayEnabled = OverlayCheck.IsChecked == true;
        _config.OverlayFontFamily = OverlayFontBox.SelectedItem?.ToString() ?? "Microsoft YaHei UI";
        _config.OverlayFontSize = OverlaySizeSlider.Value;
        _config.OverlayTextColor = (OverlayColorText.Text ?? "#FFFFFF").Trim();
        _config.OverlayTextShadow = OverlayShadowCheck.IsChecked == true;
        _config.MinimizeToTray = TrayCheck.IsChecked == true;
        _config.ConversationTimeoutSeconds = (int)ConvTimeoutSlider.Value;
        _config.DshHistoryRounds = (int)HistorySlider.Value;

        // 应用别名（每行 名称=路径）
        var aliases = new Dictionary<string, string>();
        foreach (var line in (AliasesBox.Text ?? "").Split('\n'))
        {
            if (AppFinder.TryParseAliasLine(line, out var aliasName, out var aliasValue))
                aliases[aliasName] = aliasValue;
        }
        _config.AppAliases = aliases;
        _config.GitProjectPath = GitPathBox.Text.Trim();

        // 开机自启动
        if (AutoStartCheck.IsChecked == true) AutoStartHelper.Register();
        else AutoStartHelper.Unregister();
        _config.AutoStart = AutoStartCheck.IsChecked == true;

        new ConfigService().Save(_config);
        return true;
    }

    private void CancelButton_OnClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private void RescanSoftwareButton_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var entries = AppInventory.Refresh();
            WpfMessageBox.Show(
                $"扫描完成：已归档 {entries.Count} 个软件条目。\n清单位置：\n{AppInventory.MarkdownPath}",
                "DSH", MessageBoxButton.OK, MessageBoxImage.Information);
            InventoryInfo.Text = $"已归档 {entries.Count} 个软件条目（{AppInventory.MarkdownPath}）";
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show("扫描失败：" + ex.Message, "DSH", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void VadSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        => VadValue.Text = VadSlider.Value.ToString("0.000");

    private void MicGainSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        => MicGainValue.Text = MicGainSlider.Value.ToString("0.0") + "x";

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

    private void HistorySlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        => HistoryValue.Text = HistorySlider.Value <= 0
            ? "关闭"
            : ((int)HistorySlider.Value) + " 轮";

    private void OverlaySizeSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        => OverlaySizeValue.Text = ((int)OverlaySizeSlider.Value) + "px";

    // ---------- 快捷键捕获 ----------

    private void HotKeyCaptureButton_OnClick(object sender, RoutedEventArgs e)
    {
        _capturingHotKey = true;
        HotKeyCaptureButton.Content = "按下组合键…（Esc 取消）";
        HotKeyDisplay.Text = "";
        HotKeyCaptureButton.Focus();
    }

    private void OnPreviewKeyDown(object sender, InputKeyEventArgs e)
    {
        if (!_capturingHotKey) return;

        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            EndCapture(resetToCurrent: true);
            return;
        }

        var combo = BuildComboFromKey(e);
        if (combo == null) return; // 只按了修饰键，继续等待

        if (!HotKeyService.TryParse(combo, out _, out _, out var error))
        {
            HotKeyDisplay.Text = error; // 提示原因，保持捕获状态继续等待
            return;
        }

        e.Handled = true;
        HotKeyDisplay.Text = combo;
        EndCapture(resetToCurrent: false);
    }

    private void EndCapture(bool resetToCurrent)
    {
        _capturingHotKey = false;
        HotKeyCaptureButton.Content = "点击后按下新组合键…";
        if (resetToCurrent) HotKeyDisplay.Text = _config.HotKeyCombo;
    }

    /// <summary>把当前按键事件组合为 "Win+F2" 形式的字符串；纯修饰键按下返回 null</summary>
    private static string? BuildComboFromKey(InputKeyEventArgs e)
    {
        var mods = new List<string>();
        var m = Keyboard.Modifiers;
        if (m.HasFlag(ModifierKeys.Control)) mods.Add("Ctrl");
        if (m.HasFlag(ModifierKeys.Alt)) mods.Add("Alt");
        if (m.HasFlag(ModifierKeys.Shift)) mods.Add("Shift");
        if (m.HasFlag(ModifierKeys.Windows)) mods.Add("Win");

        var main = KeyToToken(e.Key);
        if (main == null) return null;
        return string.Join("+", mods.Append(main));
    }

    /// <summary>WPF 按键 → 快捷键名称（与 HotKeyService.TryParse 的词表对应）</summary>
    private static string? KeyToToken(Key key)
    {
        if (key >= Key.A && key <= Key.Z) return key.ToString();
        if (key >= Key.D0 && key <= Key.D9) return ((char)('0' + (key - Key.D0))).ToString();
        if (key >= Key.NumPad0 && key <= Key.NumPad9) return ((char)('0' + (key - Key.NumPad0))).ToString();
        if (key >= Key.F1 && key <= Key.F12) return key.ToString();
        return key switch
        {
            Key.Space => "Space",
            Key.Enter => "Enter",
            Key.Tab => "Tab",
            Key.Back => "Back",
            Key.Delete => "Del",
            Key.Insert => "Ins",
            Key.Up => "Up",
            Key.Down => "Down",
            Key.Left => "Left",
            Key.Right => "Right",
            Key.Home => "Home",
            Key.End => "End",
            Key.PageUp => "PgUp",
            Key.PageDown => "PgDn",
            Key.OemMinus => "-",
            Key.OemPlus => "=",
            Key.OemOpenBrackets => "[",
            Key.OemCloseBrackets => "]",
            Key.OemBackslash => "\\",
            Key.OemSemicolon => ";",
            Key.OemQuotes => "'",
            Key.OemComma => ",",
            Key.OemPeriod => ".",
            Key.OemQuestion => "/",
            Key.OemTilde => "`",
            _ => null
        };
    }
}
