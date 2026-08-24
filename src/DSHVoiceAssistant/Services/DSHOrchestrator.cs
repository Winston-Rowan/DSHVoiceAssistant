using DSHVoiceAssistant.Models;
using DSHVoiceAssistant.Utils;
using Microsoft.Extensions.DependencyInjection;

namespace DSHVoiceAssistant.Services;

/// <summary>
/// DSH 编排器（状态机实现）：
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
public sealed class DSHOrchestrator : IDSHOrchestrator
{
    private readonly IAudioCapture _audio;
    private readonly IWakeWordDetection _apiWake;
    private readonly IWakeWordDetection _localWake;
    private readonly ISpeechRecognition _recognizer;
    private readonly IDSHCommandService _dsh;
    private readonly ICommandProcessor _processor;
    private readonly ITTSService _tts;
    private readonly DSHConfig _config;

    private readonly object _gate = new();
    private readonly List<byte[]> _chunks = [];
    private readonly List<CancellationTokenSource> _reminders = []; // 活跃的闹钟提醒

    // 前置缓冲（pre-roll）：始终滚动缓存最近 ~0.6s 音频，
    // VAD 判定"开始说话"时回填进录音——VAD 需连续 3 帧（约 300ms）才触发，
    // 若没有缓冲，句首 1~2 个字会被截掉
    private const double PreRollSeconds = 0.6;
    private const int SampleRate = 16000; // 与 AudioConfig.SampleRate 一致（16kHz/16bit/单声道）
    private readonly List<byte[]> _preRoll = [];
    private int _preRollBytes;

    private IWakeWordDetection? _wake;
    private VoiceActivityDetector? _vad;
    private CancellationTokenSource? _maxUtteranceCts;
    private CancellationTokenSource? _conversationCts;
    private DateTime _suppressVadUntil;
    private bool _started;
    private bool _collecting;
    private bool _processing;
    private int _sessionVersion; // 会话版本号：结束对话时自增，中断在途流水线

    public DSHOrchestrator(
        IAudioCapture audio,
        [FromKeyedServices("api")] IWakeWordDetection apiWake,
        [FromKeyedServices("local")] IWakeWordDetection localWake,
        ISpeechRecognition recognizer,
        IDSHCommandService dsh,
        ICommandProcessor processor,
        ITTSService tts,
        DSHConfig config)
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

    public DSHState State { get; private set; } = DSHState.Idle;

    public bool IsMuted { get; private set; }

    public event Action<DSHState, string>? StateChanged;

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
            MaxSpeech = TimeSpan.FromMilliseconds(_config.MaxUtteranceMs),
            // 自适应底噪：低增益麦克风也能可靠触发（阈值 = max(用户阈值, 底噪×4)）
            NoiseFloorProvider = () => _audio.CurrentNoiseFloor
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

        SetState(DSHState.Idle, "待命中");
        Logger.Info("DSH 编排器已启动");
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
        // 取消所有未触发的闹钟提醒
        lock (_gate)
        {
            foreach (var cts in _reminders) cts.Cancel();
            _reminders.Clear();
        }
        Logger.Info("DSH 编排器已停止");
    }

    // ---------- 外部触发 ----------

    public void ForceActivate()
    {
        bool activated;
        lock (_gate)
        {
            if (State == DSHState.Speaking)
            {
                // 快捷键打断播报并接管倾听（键盘打断不受自声过滤影响）
                Logger.Info("快捷键打断播报并开始倾听");
                _tts.Stop();
                State = DSHState.Recording;
                StartCollectingWithPreRoll();
                StartMaxUtteranceTimer();
                activated = true;
            }
            else
            {
                activated = State is DSHState.Idle or DSHState.WakeChecking;
                if (activated)
                {
                    State = DSHState.Recording;
                    // 若用户正在说话（如刚说完唤醒词），直接接管当前片段
                    if (_vad?.IsInSpeech == true)
                    {
                        StartCollectingWithPreRoll();
                        StartMaxUtteranceTimer();
                    }
                }
            }
        }
        if (!activated) return;

        SetState(DSHState.Recording, "倾听中，请说出您的指令…");
    }

    /// <summary>
    /// 结束当前对话（键盘 ESC / 语音结束词条共用）：中断在途流水线、停止播报、
    /// 清空录音与前置缓冲、回到待命。静默结束，不播报。
    /// </summary>
    public void EndConversation()
    {
        lock (_gate)
        {
            if (State == DSHState.Idle && !_processing) return; // 本就没有对话
            _sessionVersion++; // 在途流水线检测到版本变化后自行中止
            _collecting = false;
            _chunks.Clear();
            ClearPreRoll();
        }
        _maxUtteranceCts?.Cancel();
        CancelConversationTimer();
        _vad?.Reset();
        if (State == DSHState.Speaking) _tts.Stop();
        SetState(DSHState.Idle, "待命中");
        Logger.Info("对话已结束（ESC/结束指令）");
    }

    public void ToggleMute()
    {
        IsMuted = !IsMuted;

        lock (_gate)
        {
            _collecting = false;
            _chunks.Clear();
            ClearPreRoll();
        }
        _maxUtteranceCts?.Cancel();
        _vad?.Reset();

        if (IsMuted)
        {
            _wake?.Stop();
            _audio.Stop();
            SetState(DSHState.Idle, "已静音");
            Logger.Info("已静音");
        }
        else
        {
            if (_config.WakeMode != "off") StartWakeService();
            try
            {
                _audio.Start();
                SetState(DSHState.Idle, "待命中");
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
        // 自声过滤：播报回复期间不喂 VAD，扬声器回声不可能被误判为用户说话
        // （开启过滤时播报中语音插嘴失效，可用快捷键打断；关闭则保持插嘴行为）
        if (_config.SelfVoiceFilter && _tts.IsSpeaking) return;
        _vad?.Feed(level);
    }

    private void OnDataAvailable(byte[] data)
    {
        lock (_gate)
        {
            // 维护前置缓冲（最近 PreRollSeconds 秒）
            _preRoll.Add(data);
            _preRollBytes += data.Length;
            var maxBytes = (int)(SampleRate * 2 * PreRollSeconds);
            while (_preRollBytes > maxBytes && _preRoll.Count > 0)
            {
                _preRollBytes -= _preRoll[0].Length;
                _preRoll.RemoveAt(0);
            }

            if (_collecting) _chunks.Add(data);
        }
    }

    /// <summary>开始收集录音时把前置缓冲回填，保住句首音节</summary>
    private void StartCollectingWithPreRoll()
    {
        _chunks.AddRange(_preRoll);
        _collecting = true;
    }

    /// <summary>清空前置缓冲（播报开始/结束、静音时调用，防止回声进入缓冲）</summary>
    private void ClearPreRoll()
    {
        _preRoll.Clear();
        _preRollBytes = 0;
    }

    private void OnSpeechStarted()
    {
        lock (_gate)
        {
            // 回声抑制：处理完指令刚回到倾听时，忽略 TTS 播报残留的误触发
            if (DateTime.UtcNow < _suppressVadUntil) return;

            // 🗣️ 插嘴（barge-in）：助手正在播报回复时用户开口 → 打断播报并接管语音
            if (State == DSHState.Speaking)
            {
                Logger.Info("检测到用户插嘴，打断播报并接管语音");
                _tts.Stop();
                State = DSHState.Recording;
                StartCollectingWithPreRoll();
                StartMaxUtteranceTimer();
            }
            // 处于"倾听中"或"待命中/唤醒核对中"时开始积累音频
            else if (State is DSHState.Recording or DSHState.Idle or DSHState.WakeChecking)
            {
                StartCollectingWithPreRoll();
                if (State == DSHState.Recording) StartMaxUtteranceTimer();
            }
        }
        // 用户开始说话 = 非沉默，刷新连续对话超时
        if (State == DSHState.Recording) RestartConversationTimer();
    }

    private void OnSpeechEnded()
    {
        byte[]? wav = null;
        lock (_gate)
        {
            if (!_collecting) return;

            if (State == DSHState.Recording)
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
            if (State is DSHState.Idle or DSHState.WakeChecking)
            {
                State = DSHState.Recording;
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
        SetState(DSHState.Recording, "倾听中，请说出您的指令…");
    }

    // ---------- 指令处理流水线（ThreadPool） ----------

    private async Task ProcessUtteranceAsync(byte[] wav)
    {
        if (_processing) return;
        _processing = true;
        var session = _sessionVersion; // 记录会话版本：ESC/结束指令中断后不再继续
        try
        {
            SetState(DSHState.Transcribing, "语音识别中…");
            var recognition = await _recognizer.RecognizeAsync(wav);
            if (session != _sessionVersion) return; // 对话已被结束
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

            // 语音结束对话：命中词条（没事了/退下/滚吧等）→ 静默结束当前对话回到待命，
            // 不消耗 DSH API 额度、不播报
            if (EndConversationMatcher.IsEndPhrase(text))
            {
                Logger.Info("检测到结束对话指令，结束当前对话: " + text);
                TextRecognized?.Invoke(text);
                SetState(DSHState.Idle, "待命中");
                return;
            }

            TextRecognized?.Invoke(text);

            SetState(DSHState.Thinking, "DSH 正在理解指令…");
            var command = await _dsh.ExecuteAsync(text);
            if (session != _sessionVersion) return; // 对话已被结束
            if (!command.Success)
            {
                RaiseError(command.ErrorMessage);
                return;
            }

            DSHReplied?.Invoke(command.Response);

            SetState(DSHState.Executing, "正在执行…");
            var result = await _processor.ExecuteAsync(command);
            if (session != _sessionVersion) return; // 对话已被结束
            if (!result.Success)
            {
                RaiseError(result.Message);
                return;
            }

            var speak = !string.IsNullOrWhiteSpace(command.Response) ? command.Response : result.Message;
            // 命令/脚本执行类（如 git 推送）：播报真实执行结果（输出摘要/失败原因），截断保持简洁
            if (command.Action == "custom_script" && !string.IsNullOrWhiteSpace(result.Message))
            {
                speak = result.Message.Length > 120 ? result.Message[..120] : result.Message;
            }

            // 中文回复保障：任何回复强制中文（提示词已约束，此处兜底翻译）
            if (!string.IsNullOrWhiteSpace(speak))
            {
                speak = await _dsh.EnsureChineseAsync(speak);
            }

            // 闹钟/提醒：本地定时，到点语音播报（不依赖 DSH）
            if (string.Equals(command.Action, "reminder", StringComparison.OrdinalIgnoreCase))
            {
                var delay = ReminderParser.ParseDelay(command.Params);
                if (delay != null)
                {
                    var msg = ReminderParser.GetMessage(command.Params) ?? "您设置的提醒时间到了";
                    ScheduleReminder(delay.Value, msg);
                    Logger.Info($"已设置提醒（{delay.Value.TotalMinutes:0.#} 分钟后）：{msg}");
                }
                else
                {
                    speak = "提醒参数无法识别，请说清楚几分钟后或几点提醒";
                }
            }

            if (!string.IsNullOrWhiteSpace(speak))
            {
                SetState(DSHState.Speaking, "回复中…");
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

    /// <summary>到点后语音播报提醒内容（后台任务，不阻塞流水线；应用退出时取消）</summary>
    private void ScheduleReminder(TimeSpan delay, string message)
    {
        var cts = new CancellationTokenSource();
        lock (_gate) _reminders.Add(cts);
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay, cts.Token);
                Logger.Info("提醒触发: " + message);
                // 提醒内容同样强制中文
                var zh = await _dsh.EnsureChineseAsync(message);
                await _tts.SpeakAsync(zh);
            }
            catch (TaskCanceledException)
            {
                // 应用退出/已取消
            }
            finally
            {
                lock (_gate) _reminders.Remove(cts);
            }
        }, cts.Token);
    }

    /// <summary>
    /// 播报回复；播报自然结束后回到倾听（连续对话）。
    /// 被插嘴打断时状态已由 OnSpeechStarted 切换为 Recording，此处不再干预。
    /// </summary>
    private async Task SpeakAndContinueAsync(string speak)
    {
        try
        {
            // 进入播报前复位 VAD 并清空前置缓冲（防止自己的回声进入句首缓冲）
            _vad?.Reset();
            ClearPreRoll();
            await _tts.SpeakAsync(speak);
        }
        catch (Exception ex)
        {
            Logger.Warn("回复播报异常: " + ex.Message);
        }
        if (State == DSHState.Speaking)
        {
            // 播报结束复位 VAD、清空缓冲：丢弃回声可能留下的状态，防止误判/句首污染
            _vad?.Reset();
            ClearPreRoll();
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
            SetState(DSHState.Idle, "待命中");
            return;
        }
        _suppressVadUntil = DateTime.UtcNow.AddSeconds(1.5);
        SetState(DSHState.Recording, "倾听中…（连续对话）");
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
            if (State != DSHState.Recording || _vad?.IsInSpeech == true) return;
        }
        Logger.Info("连续对话超时，退出监听模式，等待再次呼叫");
        // 静默退出，不播报任何提示
        SetState(DSHState.Idle, "待命中");
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
            if (State == DSHState.Recording && _collecting)
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

    private void SetState(DSHState state, string message)
    {
        State = state;
        Logger.Info("状态变更: " + state + " - " + message);
        // 处理指令期间关闭唤醒检测，避免重复唤醒
        if (_wake != null) _wake.IsEnabled = state == DSHState.Idle;
        // 连续对话计时：倾听中计时，其他状态暂停
        if (state == DSHState.Recording) RestartConversationTimer();
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
        SetState(DSHState.Error, message);
        _ = RecoverFromErrorAsync();
    }

    private async Task RecoverFromErrorAsync()
    {
        await Task.Delay(3000);
        if (State != DSHState.Error) return;
        // 错误后按配置回到连续倾听或待命
        if (_config.ConversationTimeoutSeconds <= 0)
        {
            SetState(DSHState.Idle, "待命中");
        }
        else
        {
            _suppressVadUntil = DateTime.UtcNow.AddSeconds(1.5);
            SetState(DSHState.Recording, "倾听中…（连续对话）");
        }
    }

    public void Dispose() => Stop();
}
