using System.Windows;
using DivaVoiceAssistant.Config;
using DivaVoiceAssistant.Models;
using DivaVoiceAssistant.Services;
using DivaVoiceAssistant.Utils;
using DivaVoiceAssistant.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using WpfApplication = System.Windows.Application;
using WpfMessageBox = System.Windows.MessageBox;

namespace DivaVoiceAssistant;

/// <summary>
/// 应用入口：依赖注入装配、全局异常处理、单实例保护、托盘图标。
/// </summary>
public partial class App : WpfApplication
{
    private Mutex? _mutex;
    private bool _mutexOwned;
    private ServiceProvider? _services;
    private IDivaOrchestrator? _orchestrator;
    private TrayIconService? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // 单实例保护
        _mutex = new Mutex(true, "DivaVoiceAssistant_SingleInstance", out _mutexOwned);
        if (!_mutexOwned)
        {
            WpfMessageBox.Show("Diva 语音助手已在运行。", "Diva", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        // 全局异常兜底（只记录，不崩溃）
        AppDomain.CurrentDomain.UnhandledException += (_, ev) => Logger.Fatal("未处理异常: " + ev.ExceptionObject);
        DispatcherUnhandledException += (_, ev) =>
        {
            Logger.Error("UI 线程异常: " + ev.Exception);
            ev.Handled = true;
        };
        TaskScheduler.UnobservedTaskException += (_, ev) =>
        {
            Logger.Error("未观察任务异常: " + ev.Exception);
            ev.SetObserved();
        };

        Logger.Init();
        Logger.Info("========== Diva 语音助手启动 ==========");

        // ---------- 依赖注入装配 ----------
        var configService = new ConfigService();
        var config = configService.Load();

        var services = new ServiceCollection();
        services.AddSingleton(config);
        services.AddSingleton(configService);
        services.AddSingleton<IAudioCapture, AudioCaptureService>();
        services.AddSingleton<ISpeechRecognition, SpeechRecognitionService>();
        services.AddKeyedSingleton<IWakeWordDetection, WakeWordService>("api");
        services.AddKeyedSingleton<IWakeWordDetection, LocalWakeWordService>("local");
        services.AddSingleton<IDSHCommandService, DSHCommandService>();
        services.AddSingleton<ICommandProcessor, CommandProcessor>();
        services.AddSingleton<ITTSService, TTSService>();
        services.AddSingleton<IDivaOrchestrator, DivaOrchestrator>();
        services.AddSingleton<MainViewModel>();
        _services = services.BuildServiceProvider();

        _orchestrator = _services.GetRequiredService<IDivaOrchestrator>();
        _tray = new TrayIconService();
        _orchestrator.StateChanged += (state, _) => _tray.UpdateState(state, _orchestrator.IsMuted);

        var viewModel = _services.GetRequiredService<MainViewModel>();
        var window = new MainWindow(viewModel, _orchestrator, config, _tray);
        MainWindow = window;
        window.Show();

        _orchestrator.Start();

        // 后台归档已安装软件（桌面/开始菜单快捷方式、注册表、Steam/Epic），供 open_game/open_app 查找
        _ = Task.Run(() =>
        {
            try { AppInventory.Refresh(); }
            catch (Exception ex) { Logger.Warn("软件归档失败: " + ex.Message); }
        });
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _orchestrator?.Stop();
            _tray?.Dispose();
            _services?.Dispose();
            _mutex?.Dispose();
            Logger.Info("Diva 语音助手已退出");
        }
        catch
        {
            // 退出阶段异常不影响进程结束
        }
        base.OnExit(e);
    }
}
