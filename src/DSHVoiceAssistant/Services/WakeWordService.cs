using DSHVoiceAssistant.Models;
using DSHVoiceAssistant.Utils;

namespace DSHVoiceAssistant.Services;

/// <summary>
/// 唤醒词检测实现（API 方案，即需求文档中的"备选方案"）：
/// 空闲时通过 VAD 检测说话片段 → 调用语音识别 API → 文本命中唤醒词变体即触发唤醒。
/// 优点：无需本地模型与额外授权；缺点：每次语音片段消耗一次识别额度（可用快捷键替代）。
/// 如需更低的延迟与成本，可在此类之外按 IWakeWordDetection 接口接入 Porcupine 等本地唤醒词引擎。
/// </summary>
public sealed class WakeWordService : IWakeWordDetection, IDisposable
{
    private readonly IAudioCapture _audio;
    private readonly ISpeechRecognition _recognizer;
    private readonly DSHConfig _config;
    private readonly object _gate = new();
    private readonly List<byte[]> _chunks = [];

    private VoiceActivityDetector? _vad;
    private bool _started;
    private bool _checking;

    public WakeWordService(IAudioCapture audio, ISpeechRecognition recognizer, DSHConfig config)
    {
        _audio = audio;
        _recognizer = recognizer;
        _config = config;
    }

    public event EventHandler? WakeWordDetected;

    private bool _isEnabled = true;

    /// <summary>
    /// 是否启用识别。重新启用（false→true）时清空音频缓存并复位 VAD，
    /// 丢弃播报回复期间积累的扬声器回声，防止回声残留被误识别为唤醒词。
    /// </summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value) return;
            _isEnabled = value;
            if (value)
            {
                lock (_gate) _chunks.Clear();
                _vad?.Reset();
                Logger.Info("云端唤醒服务重新启用，已清空回声缓存");
            }
        }
    }

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
        Logger.Info("唤醒词检测已启动");
    }

    public void Stop()
    {
        if (!_started) return;
        _started = false;

        _audio.LevelChanged -= OnLevelChanged;
        _audio.DataAvailable -= OnDataAvailable;
        if (_vad != null)
        {
            _vad.SpeechStarted -= OnSpeechStarted;
            _vad.SpeechEnded -= OnSpeechEnded;
        }
        lock (_gate) _chunks.Clear();
        Logger.Info("唤醒词检测已停止");
    }

    private void OnLevelChanged(float level)
    {
        if (!IsEnabled) return; // 禁用期间不喂 VAD，防止播报回声污染检测状态
        _vad?.Feed(level);
    }

    private void OnDataAvailable(byte[] data)
    {
        if (_vad is { IsInSpeech: true })
        {
            lock (_gate) _chunks.Add(data);
        }
    }

    private void OnSpeechStarted()
    {
        lock (_gate) _chunks.Clear();
    }

    private void OnSpeechEnded()
    {
        if (!IsEnabled || _checking) return;

        byte[] wav;
        lock (_gate)
        {
            if (_chunks.Count == 0) return;
            var snapshot = _chunks.ToArray();
            _chunks.Clear();
            wav = AudioUtils.BuildWavBytes(snapshot);
        }

        _checking = true;
        _ = CheckAsync(wav);
    }

    /// <summary>异步核对唤醒词（内部已捕获所有异常，不会抛出）</summary>
    private async Task CheckAsync(byte[] wav)
    {
        try
        {
            var result = await _recognizer.RecognizeAsync(wav);
            if (result.IsSuccess && WakeWordMatcher.ContainsWakeWord(result.Text, _config.WakeWordVariants))
            {
                Logger.Info($"唤醒成功：{result.Text}");
                WakeWordDetected?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception ex)
        {
            Logger.Warn("唤醒词检测异常: " + ex.Message);
        }
        finally
        {
            _checking = false;
        }
    }

    public void Dispose() => Stop();
}
