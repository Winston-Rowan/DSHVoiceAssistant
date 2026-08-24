using System.Linq;
using System.Windows;
using DSHVoiceAssistant.Config;
using DSHVoiceAssistant.Models;
using DSHVoiceAssistant.Services;
using DSHVoiceAssistant.Utils;
using DSHVoiceAssistant.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using WpfApplication = System.Windows.Application;
using WpfMessageBox = System.Windows.MessageBox;

namespace DSHVoiceAssistant;

/// <summary>
/// 应用入口：依赖注入装配、全局异常处理、单实例保护、托盘图标。
/// </summary>
public partial class App : WpfApplication
{
    private Mutex? _mutex;
    private bool _mutexOwned;
    private ServiceProvider? _services;
    private IDSHOrchestrator? _orchestrator;
    private TrayIconService? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // 单实例保护
        _mutex = new Mutex(true, "DSHVoiceAssistant_SingleInstance", out _mutexOwned);
        if (!_mutexOwned)
        {
            WpfMessageBox.Show("DSH 语音助手已在运行。", "DSH", MessageBoxButton.OK, MessageBoxImage.Information);
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
        Logger.Info("========== DSH 语音助手启动 ==========");
        Logger.Info("命令行参数: " + (e.Args.Length > 0 ? string.Join(" ", e.Args) : "（无）"));

        // 静默启动（开机自启携带 --silent）：不显示主窗口，仅驻留托盘
        var silent = e.Args.Any(a => string.Equals(a, "--silent", StringComparison.OrdinalIgnoreCase));

        // 兼容旧安装：已有自启动注册但缺 --silent 参数时自动补上
        AutoStartHelper.EnsureSilentFlag();

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
        services.AddSingleton<IDSHOrchestrator, DSHOrchestrator>();
        services.AddSingleton<MainViewModel>();
        _services = services.BuildServiceProvider();

        _orchestrator = _services.GetRequiredService<IDSHOrchestrator>();
        _tray = new TrayIconService();
        _orchestrator.StateChanged += (state, _) => _tray.UpdateState(state, _orchestrator.IsMuted);

        var viewModel = _services.GetRequiredService<MainViewModel>();
        var window = new MainWindow(viewModel, _orchestrator, config, _tray);
        MainWindow = window;
        if (!silent) window.Show(); // 静默启动不显示界面，托盘双击/右键可随时调出

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
            Logger.Info("DSH 语音助手已退出");
        }
        catch
        {
            // 退出阶段异常不影响进程结束
        }
        base.OnExit(e);
    }
}
