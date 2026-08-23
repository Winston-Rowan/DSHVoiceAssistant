using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using DivaVoiceAssistant.Models;
using DivaVoiceAssistant.Services;
using DivaVoiceAssistant.ViewModels;
using WpfApplication = System.Windows.Application;

namespace DivaVoiceAssistant;

/// <summary>
/// 主窗口：负责标题栏交互、托盘事件接线、全局快捷键注册、最小化到托盘。
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly IDivaOrchestrator _orchestrator;
    private readonly DivaConfig _config;
    private readonly TrayIconService _tray;
    private bool _allowClose;

    public MainWindow(MainViewModel vm, IDivaOrchestrator orchestrator, DivaConfig config, TrayIconService tray)
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
    }

    /// <summary>注册全局快捷键 Ctrl+Alt+D（'D' = 0x44）</summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        if (_config.HotKeyEnabled)
        {
            HotKeyService.Register(this, HotKeyService.ModControl | HotKeyService.ModAlt, 0x44,
                () => _vm.ForceActivateCommand.Execute(null));
        }
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
        var settings = new SettingsWindow(_config) { Owner = this };
        settings.ShowDialog();
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
