using DivaVoiceAssistant.Models;
using DivaVoiceAssistant.Utils;
using Microsoft.Extensions.DependencyInjection;

namespace DivaVoiceAssistant.Services;

/// <summary>
/// Diva 编排器（状态机实现）：
///
/// 状态流转：
///   Idle ──(唤醒词/快捷键)──▶ Recording ──(VAD 检测到一句话结束)──▶ Transcribing
///        ──▶ Thinking（DSH 决策）──▶ Executing ──▶ Speaking（TTS）──▶ Idle
///   任意环节失败 ──▶ Error ──(3秒后)──▶ Idle
///
/// 线程模型：
///   音频事件（LevelChanged/DataAvailable/VAD）在 NAudio 回调线程触发，
///   API 调用在 ThreadPool 上执行，UI 事件由 ViewModel 通过 SynchronizationContext 封送。
/// </summary>
public sealed class DivaOrchestrator : IDivaOrchestrator
{
    private readonly IAudioCapture _audio;
    private readonly IWakeWordDetection _apiWake;
    private readonly IWakeWordDetection _localWake;
    private readonly ISpeechRecognition _recognizer;
    private readonly IDSHCommandService _dsh;
    private readonly ICommandProcessor _processor;
    private readonly ITTSService _tts;
    private readonly DivaConfig _config;

    private readonly object _gate = new();
    private readonly List<byte[]> _chunks = [];

    private IWakeWordDetection? _wake;
    private VoiceActivityDetector? _vad;
    private CancellationTokenSource? _maxUtteranceCts;
    private CancellationTokenSource? _conversationCts;
    private DateTime _suppressVadUntil;
    private bool _started;
    private bool _collecting;
    private bool _processing;

    public DivaOrchestrator(
        IAudioCapture audio,
        [FromKeyedServices("api")] IWakeWordDetection apiWake,
        [FromKeyedServices("local")] IWakeWordDetection localWake,
        ISpeechRecognition recognizer,
        IDSHCommandService dsh,
        ICommandProcessor processor,
        ITTSService tts,
        DivaConfig config)
    {
        _audio = audio;
        _apiWake = apiWake;
        _localWake = localWake;
        _recognizer = recognizer;
        _dsh = dsh;
        _processor = processor;
        _tts = tts;
        _config = config;
    }

    public DivaState State { get; private set; } = DivaState.Idle;

    public bool IsMuted { get; private set; }

    public event Action<DivaState, string>? StateChanged;

    public event Action<float>? LevelChanged;

    public event Action<string>? TextRecognized;

    public event Action<string>? DSHReplied;

    public event Action<string>? ErrorOccurred;

    public event Action? WakeWordHeard;

    // ---------- 生命周期 ----------

    public void Start()
    {
        if (_started) return;
        _started = true;

        _vad = new VoiceActivityDetector(new VADSettings
        {
            StartThreshold = _config.VadThreshold,
            StartFrames = _config.VadStartFrames,
            EndSilence = TimeSpan.FromMilliseconds(_config.SilenceTimeoutMs),
            MinSpeech = TimeSpan.FromMilliseconds(_config.MinUtteranceMs),
            MaxSpeech = TimeSpan.FromMilliseconds(_config.MaxUtteranceMs)
        });

        _audio.LevelChanged += OnLevelChanged;
        _audio.DataAvailable += OnDataAvailable;
        _vad.SpeechStarted += OnSpeechStarted;
        _vad.SpeechEnded += OnSpeechEnded;

        // 选择并启动唤醒服务（本地识别优先，失败回退云端 API）
        _wake = SelectWakeService();
        _wake!.WakeWordDetected += OnWakeWordDetected;
        if (_config.WakeMode != "off") StartWakeService();

        try
        {
            _audio.Start();
        }
        catch (Exception ex)
        {
            RaiseError("麦克风启动失败：" + ex.Message);
            return;
        }

        SetState(DivaState.Idle, "待命中");
        Logger.Info("Diva 编排器已启动");
    }

    public void Stop()
    {
        if (!_started) return;
        _started = false;

        _audio.LevelChanged -= OnLevelChanged;
        _audio.DataAvailable -= OnDataAvailable;
        if (_wake != null)
        {
            _wake.WakeWordDetected -= OnWakeWordDetected;
            _wake.Stop();
        }
        if (_vad != null)
        {
            _vad.SpeechStarted -= OnSpeechStarted;
            _vad.SpeechEnded -= OnSpeechEnded;
        }

        _maxUtteranceCts?.Cancel();
        CancelConversationTimer();
        _wake?.Stop();
        _audio.Stop();
        Logger.Info("Diva 编排器已停止");
    }

    // ---------- 外部触发 ----------

    public void ForceActivate()
    {
        bool activated;
        lock (_gate)
        {
            activated = State == DivaState.Idle;
            if (activated)
            {
                State = DivaState.Recording;
                // 若用户正在说话（如刚说完唤醒词），直接接管当前片段
                if (_vad?.IsInSpeech == true)
                {
                    _collecting = true;
                    StartMaxUtteranceTimer();
                }
            }
        }
        if (!activated) return;

        System.Media.SystemSounds.Asterisk.Play();
        SetState(DivaState.Recording, "倾听中，请说出您的指令…");
    }

    public void ToggleMute()
    {
        IsMuted = !IsMuted;

        lock (_gate)
        {
            _collecting = false;
            _chunks.Clear();
        }
        _maxUtteranceCts?.Cancel();
        _vad?.Reset();

        if (IsMuted)
        {
            _wake?.Stop();
            _audio.Stop();
            SetState(DivaState.Idle, "已静音");
            Logger.Info("已静音");
        }
        else
        {
            if (_config.WakeMode != "off") StartWakeService();
            try
            {
                _audio.Start();
                SetState(DivaState.Idle, "待命中");
            }
            catch (Exception ex)
            {
                RaiseError("麦克风恢复失败：" + ex.Message);
            }
            Logger.Info("取消静音");
        }
    }

    // ---------- 音频事件（NAudio 回调线程） ----------

    private void OnLevelChanged(float level)
    {
        LevelChanged?.Invoke(level);
        _vad?.Feed(level);
    }

    private void OnDataAvailable(byte[] data)
    {
        lock (_gate)
        {
            if (_collecting) _chunks.Add(data);
        }
    }

    private void OnSpeechStarted()
    {
        lock (_gate)
        {
            // 回声抑制：处理完指令刚回到倾听时，忽略 TTS 播报残留的误触发
            if (DateTime.UtcNow < _suppressVadUntil) return;

            // 🗣️ 插嘴（barge-in）：Diva 正在播报回复时用户开口 → 打断播报并接管语音
            if (State == DivaState.Speaking)
            {
                Logger.Info("检测到用户插嘴，打断播报并接管语音");
                _tts.Stop();
                State = DivaState.Recording;
                _collecting = true;
                StartMaxUtteranceTimer();
            }
            // 处于"倾听中"或"待命中/唤醒核对中"时开始积累音频
            else if (State is DivaState.Recording or DivaState.Idle or DivaState.WakeChecking)
            {
                _collecting = true;
                if (State == DivaState.Recording) StartMaxUtteranceTimer();
            }
        }
        // 用户开始说话 = 非沉默，刷新连续对话超时
        if (State == DivaState.Recording) RestartConversationTimer();
    }

    private void OnSpeechEnded()
    {
        byte[]? wav = null;
        lock (_gate)
        {
            if (!_collecting) return;

            if (State == DivaState.Recording)
            {
                wav = TakeChunksAsWav();
                _collecting = false;
            }
            else
            {
                // 空闲时的一句话（可能是唤醒词或环境噪音）：清空，交由唤醒服务处理
                _chunks.Clear();
                _collecting = false;
            }
        }

        if (wav != null) _ = ProcessUtteranceAsync(wav);
    }

    private void OnWakeWordDetected(object? sender, EventArgs e)
    {
        var activated = false;
        lock (_gate)
        {
            if (State is DivaState.Idle or DivaState.WakeChecking)
            {
                State = DivaState.Recording;
                activated = true;
                if (_vad?.IsInSpeech == true)
                {
                    _collecting = true; // 唤醒词与指令一气呵成时，接管当前片段
                    StartMaxUtteranceTimer();
                }
            }
        }
        if (!activated) return;

        WakeWordHeard?.Invoke();
        System.Media.SystemSounds.Asterisk.Play();
        SetState(DivaState.Recording, "倾听中，请说出您的指令…");
    }

    // ---------- 指令处理流水线（ThreadPool） ----------

    private async Task ProcessUtteranceAsync(byte[] wav)
    {
        if (_processing) return;
        _processing = true;
        try
        {
            SetState(DivaState.Transcribing, "语音识别中…");
            var recognition = await _recognizer.RecognizeAsync(wav);
            if (!recognition.IsSuccess)
            {
                RaiseError(recognition.ErrorMessage);
                return;
            }

            var text = WakeWordMatcher.StripLeadingWakeWord(recognition.Text, _config.WakeWordVariants).Trim();
            if (string.IsNullOrEmpty(text))
            {
                Logger.Info("识别内容为空，忽略");
                ContinueListening();
                return;
            }

            TextRecognized?.Invoke(text);

            SetState(DivaState.Thinking, "DSH 正在理解指令…");
            var command = await _dsh.ExecuteAsync(text);
            if (!command.Success)
            {
                RaiseError(command.ErrorMessage);
                return;
            }

            DSHReplied?.Invoke(command.Response);

            SetState(DivaState.Executing, "正在执行…");
            var result = await _processor.ExecuteAsync(command);
            if (!result.Success)
            {
                RaiseError(result.Message);
                return;
            }

            var speak = !string.IsNullOrWhiteSpace(command.Response) ? command.Response : result.Message;
            if (!string.IsNullOrWhiteSpace(speak))
            {
                SetState(DivaState.Speaking, "回复中…");
                // 播报不阻塞流水线：期间用户可随时插嘴打断（见 OnSpeechStarted）
                _ = SpeakAndContinueAsync(speak);
            }
            else
            {
                ContinueListening();
            }
        }
        catch (Exception ex)
        {
            Logger.Error("指令处理异常: " + ex);
            RaiseError("处理指令时出错：" + ex.Message);
        }
        finally
        {
            _processing = false;
        }
    }

    /// <summary>
    /// 播报回复；播报自然结束后回到倾听（连续对话）。
    /// 被插嘴打断时状态已由 OnSpeechStarted 切换为 Recording，此处不再干预。
    /// </summary>
    private async Task SpeakAndContinueAsync(string speak)
    {
        try
        {
            await _tts.SpeakAsync(speak);
        }
        catch (Exception ex)
        {
            Logger.Warn("回复播报异常: " + ex.Message);
        }
        if (State == DivaState.Speaking)
        {
            ContinueListening();
        }
    }

    // ---------- 连续对话 ----------

    /// <summary>
    /// 处理完一条指令后：按配置进入连续倾听（无需再次呼叫）或回到待命。
    /// 进入倾听时设置短暂的回声抑制窗口，防止 TTS 播报残留被当作新指令。
    /// </summary>
    private void ContinueListening()
    {
        if (_config.ConversationTimeoutSeconds <= 0)
        {
            SetState(DivaState.Idle, "待命中");
            return;
        }
        _suppressVadUntil = DateTime.UtcNow.AddSeconds(1.5);
        SetState(DivaState.Recording, "倾听中…（连续对话）");
    }

    /// <summary>重启连续对话沉默计时（说话/回到倾听时调用）</summary>
    private void RestartConversationTimer()
    {
        lock (_gate)
        {
            _conversationCts?.Cancel();
            if (_config.ConversationTimeoutSeconds <= 0) return;
            _conversationCts = new CancellationTokenSource();
            var token = _conversationCts.Token;
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(_config.ConversationTimeoutSeconds), token);
                    OnConversationTimeout();
                }
                catch (TaskCanceledException)
                {
                    // 正常取消
                }
            }, token);
        }
    }

    private void CancelConversationTimer()
    {
        lock (_gate)
        {
            _conversationCts?.Cancel();
            _conversationCts = null;
        }
    }

    private void OnConversationTimeout()
    {
        lock (_gate)
        {
            // 仅在安静的倾听状态下超时才退出（说话中/处理中不退出）
            if (State != DivaState.Recording || _vad?.IsInSpeech == true) return;
        }
        Logger.Info("连续对话超时，退出监听模式，等待再次呼叫");
        SetState(DivaState.Idle, "待命中");
        SpeakFriendly("好的，有需要再叫我");
    }

    private void SpeakFriendly(string text)
    {
        _ = Task.Run(async () =>
        {
            try { await _tts.SpeakAsync(text); }
            catch (Exception ex) { Logger.Warn("提示语音失败: " + ex.Message); }
        });
    }

    // ---------- 辅助 ----------

    private void StartMaxUtteranceTimer()
    {
        _maxUtteranceCts?.Cancel();
        _maxUtteranceCts = new CancellationTokenSource();
        var token = _maxUtteranceCts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(_config.MaxUtteranceMs, token);
                OnMaxUtteranceTimeout();
            }
            catch (TaskCanceledException)
            {
                // 正常取消
            }
        }, token);
    }

    private void OnMaxUtteranceTimeout()
    {
        byte[]? wav = null;
        lock (_gate)
        {
            if (State == DivaState.Recording && _collecting)
            {
                wav = TakeChunksAsWav();
                _collecting = false;
            }
        }
        if (wav != null) _ = ProcessUtteranceAsync(wav);
    }

    private byte[] TakeChunksAsWav()
    {
        var snapshot = _chunks.ToArray();
        _chunks.Clear();
        return AudioUtils.BuildWavBytes(snapshot);
    }

    private void SetState(DivaState state, string message)
    {
        State = state;
        Logger.Info("状态变更: " + state + " - " + message);
        // 处理指令期间关闭唤醒检测，避免重复唤醒
        if (_wake != null) _wake.IsEnabled = state == DivaState.Idle;
        // 连续对话计时：倾听中计时，其他状态暂停
        if (state == DivaState.Recording) RestartConversationTimer();
        else CancelConversationTimer();
        StateChanged?.Invoke(state, message);
    }

    // ---------- 唤醒服务选择 ----------

    private IWakeWordDetection SelectWakeService() => _config.WakeMode switch
    {
        "api" => _apiWake,
        _ => _localWake
    };

    private void StartWakeService()
    {
        try
        {
            _wake?.Start();
        }
        catch (Exception ex)
        {
            Logger.Warn("唤醒服务启动失败: " + ex.Message);
            if (_config.WakeMode == "local" && _wake != _apiWake)
            {
                Logger.Warn("本地唤醒不可用，回退到云端 API 唤醒");
                _wake = _apiWake;
                try
                {
                    _wake.Start();
                }
                catch (Exception ex2)
                {
                    RaiseError("唤醒服务不可用：" + ex2.Message);
                }
            }
            else
            {
                RaiseError("唤醒服务不可用：" + ex.Message);
            }
        }
    }

    private void RaiseError(string message)
    {
        Logger.Error(message);
        ErrorOccurred?.Invoke(message);
        SetState(DivaState.Error, message);
        _ = RecoverFromErrorAsync();
    }

    private async Task RecoverFromErrorAsync()
    {
        await Task.Delay(3000);
        if (State != DivaState.Error) return;
        // 错误后按配置回到连续倾听或待命
        if (_config.ConversationTimeoutSeconds <= 0)
        {
            SetState(DivaState.Idle, "待命中");
        }
        else
        {
            _suppressVadUntil = DateTime.UtcNow.AddSeconds(1.5);
            SetState(DivaState.Recording, "倾听中…（连续对话）");
        }
    }

    public void Dispose() => Stop();
}
