using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using DSHVoiceAssistant.Controls;
using DSHVoiceAssistant.Models;
using DSHVoiceAssistant.Services;
using DSHVoiceAssistant.Utils;
using DSHVoiceAssistant.ViewModels;
using WpfApplication = System.Windows.Application;

namespace DSHVoiceAssistant;

/// <summary>
/// 主窗口：负责标题栏交互、托盘事件接线、全局快捷键注册、最小化到托盘、
/// 屏幕边缘 Siri 光晕（静默运行时被唤起的可视反馈）。
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly IDSHOrchestrator _orchestrator;
    private readonly DSHConfig _config;
    private readonly TrayIconService _tray;
    private readonly EdgeGlowWindow _edgeGlow;
    private IDisposable? _hotkeyRegistration;
    private bool _allowClose;

    public MainWindow(MainViewModel vm, IDSHOrchestrator orchestrator, DSHConfig config, TrayIconService tray)
    {
        InitializeComponent();
        _vm = vm;
        _orchestrator = orchestrator;
        _config = config;
        _tray = tray;
        DataContext = vm;

        // 波形数据直连（音频线程 → 控件内部线程安全队列）
        _orchestrator.LevelChanged += Waveform.PushLevel;

        // ViewModel 事件 → 窗口动作
        vm.OpenSettingsRequested += OpenSettings;
        vm.ExitRequested += ShutdownApp;

        // 托盘事件
        _tray.ShowRequested += ShowMainWindow;
        _tray.ForceActivateRequested += () => vm.ForceActivateCommand.Execute(null);
        _tray.ToggleListeningRequested += () => vm.ToggleMuteCommand.Execute(null);
        _tray.ExitRequested += ShutdownApp;

        // 屏幕边缘光晕：状态变化（可能来自音频/线程池线程，封送到 UI 线程）+ 窗口可见性变化
        _edgeGlow = new EdgeGlowWindow();
        _orchestrator.StateChanged += (state, _) =>
            Dispatcher.BeginInvoke(() => UpdateEdgeGlow(state));
        IsVisibleChanged += (_, _) => UpdateEdgeGlow();
    }

    /// <summary>注册/重新注册全局快捷键（组合键可在设置中自定义，保存后即时生效）</summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ApplyHotKey();
    }

    /// <summary>
    /// 按当前配置（启用开关 + 组合键）注册全局快捷键；
    /// 释放旧注册后重新注册，可在设置保存后调用实现即时生效。
    /// </summary>
    private void ApplyHotKey()
    {
        _hotkeyRegistration?.Dispose();
        _hotkeyRegistration = null;

        if (!_config.HotKeyEnabled)
        {
            Logger.Info("全局快捷键已关闭");
            return;
        }
        if (!HotKeyService.TryParse(_config.HotKeyCombo, out var modifiers, out var vk, out var error))
        {
            Logger.Warn("快捷键组合无效，已忽略: " + error);
            return;
        }

        _hotkeyRegistration = HotKeyService.Register(this, modifiers, vk,
            () => _vm.ForceActivateCommand.Execute(null));
        Logger.Info("全局快捷键已注册: " + _config.HotKeyCombo);
    }

    /// <summary>关闭按钮 → 最小化到托盘（可通过设置关闭该行为）</summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose && _config.MinimizeToTray)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnClosing(e);
    }

    private void ShutdownApp()
    {
        _allowClose = true;
        WpfApplication.Current.Shutdown();
    }

    private void ShowMainWindow()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    private void OpenSettings()
    {
        var hotKeyEnabled = _config.HotKeyEnabled;
        var hotKeyCombo = _config.HotKeyCombo;

        var settings = new SettingsWindow(_config) { Owner = this };
        settings.ShowDialog();

        // 快捷键设置变化 → 立即重新注册，无需重启
        if (hotKeyEnabled != _config.HotKeyEnabled || hotKeyCombo != _config.HotKeyCombo)
        {
            ApplyHotKey();
        }
        // 光晕开关可能变化 → 即时生效
        UpdateEdgeGlow();
    }

    // ---------- 屏幕边缘光晕 ----------

    /// <summary>
    /// 按「配置开关 + 主窗口可见性 + 助手状态」计算并更新光晕显示。
    /// 规则见 <see cref="EdgeGlowVisibility.ShouldShow"/>。
    /// </summary>
    private void UpdateEdgeGlow(DSHState? state = null)
    {
        var s = state ?? _orchestrator.State;
        if (EdgeGlowVisibility.ShouldShow(_config.EdgeGlowEnabled, IsVisible, s))
        {
            _edgeGlow.ShowGlow(s);
        }
        else
        {
            _edgeGlow.HideGlow();
        }
    }

    // ---------- XAML 事件 ----------

    private void TitleBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }

    private void SettingsButton_OnClick(object sender, RoutedEventArgs e) => OpenSettings();

    private void MinimizeButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_config.MinimizeToTray) Hide();
        else WindowState = WindowState.Minimized;
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_config.MinimizeToTray) Hide();
        else
        {
            _allowClose = true;
            Close();
        }
    }
}
