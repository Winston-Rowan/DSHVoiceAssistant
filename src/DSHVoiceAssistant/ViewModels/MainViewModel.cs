using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using DSHVoiceAssistant.Models;
using DSHVoiceAssistant.Services;

namespace DSHVoiceAssistant.ViewModels;

/// <summary>
/// 主界面 ViewModel：订阅编排器事件并封送到 UI 线程，暴露状态/文本/命令。
/// </summary>
public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IDSHOrchestrator _orchestrator;
    private readonly SynchronizationContext? _ui;

    public MainViewModel(IDSHOrchestrator orchestrator, DSHConfig config)
    {
        _orchestrator = orchestrator;
        _ui = SynchronizationContext.Current; // 在 UI 线程创建时捕获
        WakeWordDisplay = "唤醒词: " + config.WakeWord;
        _wakeName = string.IsNullOrWhiteSpace(config.WakeWord) ? "助手" : config.WakeWord.Trim();

        _orchestrator.StateChanged += OnStateChanged;
        _orchestrator.TextRecognized += OnTextRecognized;
        _orchestrator.DSHReplied += OnDSHReplied;
        _orchestrator.ErrorOccurred += OnErrorOccurred;

        ToggleMuteCommand = new RelayCommand(ToggleMute);
        ForceActivateCommand = new RelayCommand(() => _orchestrator.ForceActivate(),
            () => _orchestrator.State is DSHState.Idle or DSHState.WakeChecking or DSHState.Speaking);
        OpenSettingsCommand = new RelayCommand(() => OpenSettingsRequested?.Invoke());
        ExitCommand = new RelayCommand(() => ExitRequested?.Invoke());
    }

    /// <summary>请求打开设置窗口（由 MainWindow 订阅）</summary>
    public event Action? OpenSettingsRequested;

    /// <summary>请求退出应用（由 MainWindow 订阅）</summary>
    public event Action? ExitRequested;

    // ---------- 绑定属性 ----------

    public string WakeWordDisplay { get; }

    private readonly string _wakeName;

    public DSHState State { get; private set; } = DSHState.Idle;

    public string RecognizedText { get; private set; } = "（等待指令…）";

    public string DSHReplyText { get; private set; } = "—";

    public bool IsMuted { get; private set; }

    public string MicButtonText => IsMuted ? "🔇 已静音" : "🎙️ 静音";

    /// <summary>是否处于处理中（用于忙碌提示）</summary>
    public bool IsBusy => State is DSHState.Recording or DSHState.Transcribing or DSHState.Thinking or DSHState.Executing or DSHState.Speaking;

    public ObservableCollection<string> History { get; } = [];

    public ICommand ToggleMuteCommand { get; }

    public ICommand ForceActivateCommand { get; }

    public ICommand OpenSettingsCommand { get; }

    public ICommand ExitCommand { get; }

    // ---------- 命令 ----------

    private void ToggleMute()
    {
        _orchestrator.ToggleMute();
        IsMuted = _orchestrator.IsMuted;
        OnPropertyChanged(nameof(IsMuted));
        OnPropertyChanged(nameof(MicButtonText));
    }

    // ---------- 事件（封送到 UI 线程） ----------

    private void OnStateChanged(DSHState state, string message) => RunOnUi(() =>
    {
        State = state;
        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(IsBusy));
    });

    private void OnTextRecognized(string text) => RunOnUi(() =>
    {
        RecognizedText = text;
        OnPropertyChanged(nameof(RecognizedText));
        AddHistory($"我：{text}");
    });

    private void OnDSHReplied(string reply) => RunOnUi(() =>
    {
        DSHReplyText = reply;
        OnPropertyChanged(nameof(DSHReplyText));
        AddHistory($"{_wakeName}：{reply}");
    });

    private void OnErrorOccurred(string error) => RunOnUi(() =>
    {
        DSHReplyText = "⚠ " + error;
        OnPropertyChanged(nameof(DSHReplyText));
    });

    private void AddHistory(string line)
    {
        History.Insert(0, $"{DateTime.Now:HH:mm:ss}  {line}");
        while (History.Count > 20) History.RemoveAt(History.Count - 1);
    }

    private void RunOnUi(Action action)
    {
        if (_ui != null && _ui != SynchronizationContext.Current)
            _ui.Post(_ => action(), null);
        else
            action();
    }

    // ---------- INotifyPropertyChanged ----------

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public void Dispose()
    {
        _orchestrator.StateChanged -= OnStateChanged;
        _orchestrator.TextRecognized -= OnTextRecognized;
        _orchestrator.DSHReplied -= OnDSHReplied;
        _orchestrator.ErrorOccurred -= OnErrorOccurred;
    }
}
