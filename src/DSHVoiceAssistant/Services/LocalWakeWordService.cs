using System.Globalization;
using System.IO;
using System.Speech.AudioFormat;
using System.Speech.Recognition;
using DSHVoiceAssistant.Models;
using DSHVoiceAssistant.Utils;

namespace DSHVoiceAssistant.Services;

/// <summary>
/// 本地唤醒词检测（零云端算力、完全离线）：
/// 基于 Windows 内置 SAPI 识别引擎 + 词表 Grammar，采用【滑动窗口批量识别】——
/// 保留最近约 4 秒的麦克风 PCM，每 2 秒把窗口快照（完整数据）交给引擎识别一次。
///
/// ⚠️ 为什么不用连续流喂入（已实测确认）：
/// SAPI 的 SpStreamWrapper 在启动时读取流的 Length 并据此读取，
/// 实时增长的流启动时 Length≈0，引擎读不到任何数据，识别静默失败；
/// 而"先填满数据再识别"（本方案）识别正常，实测置信度 0.98+。
///
/// 引擎不直接访问麦克风（每次用 MemoryStream 喂入），与 NAudio 采集完全解耦，
/// 无设备占用冲突；带 RMS 能量门控，静音时跳过识别以节省 CPU。
/// </summary>
public sealed class LocalWakeWordService : IWakeWordDetection, IDisposable
{
    private const int SampleRate = 16000;
    private const int WindowSeconds = 4;       // 滑动窗口时长
    private const int BatchIntervalMs = 1000;  // 识别批次间隔（越小响应越快，静音时被 RMS 门控跳过）

    private readonly IAudioCapture _audio;
    private readonly DSHConfig _config;

    private readonly object _gate = new();
    private readonly List<byte[]> _chunks = [];
    private int _chunkBytes;

    private Thread? _worker;
    private volatile bool _running;
    private bool _started;
    private DateTime _lastGateLog = DateTime.MinValue;
    private DateTime _lastNullLog = DateTime.MinValue;

    public LocalWakeWordService(IAudioCapture audio, DSHConfig config)
    {
        _audio = audio;
        _config = config;
    }

    public event EventHandler? WakeWordDetected;

    private bool _isEnabled = true;

    /// <summary>
    /// 是否启用识别。重新启用（false→true）时清空滑动窗口缓存，
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
                lock (_gate)
                {
                    _chunks.Clear();
                    _chunkBytes = 0;
                }
                Logger.Info("本地唤醒服务重新启用，已清空回声缓存窗口");
            }
        }
    }

    public void Start()
    {
        if (_started) return;
        _started = true;

        var info = SpeechRecognitionEngine.InstalledRecognizers()
            .FirstOrDefault(r => r.Culture.Name == "zh-CN")
            ?? SpeechRecognitionEngine.InstalledRecognizers().FirstOrDefault();
        if (info == null)
        {
            _started = false;
            throw new InvalidOperationException("系统未安装本地语音识别引擎（需要 Windows 语音识别组件）");
        }

        // 订阅原始（未增益）音频：SAPI 引擎自带 AGC，对增益/削波音频识别反而变差，
        // 回归原始音频（此前版本唤醒正常的输入）。
        _audio.RawDataAvailable += OnAudioData;

        _running = true;
        _worker = new Thread(WorkerLoop) { IsBackground = true, Name = "wake-worker" };
        _worker.Start();
        Logger.Info("本地唤醒词检测已启动（引擎: " + info.Description +
                    "，滑动窗口 " + WindowSeconds + "s / 批次 " + BatchIntervalMs + "ms，输入=原始音频）");
    }

    public void Stop()
    {
        if (!_started) return;
        _started = false;

        _running = false;
        _audio.RawDataAvailable -= OnAudioData;
        _worker?.Join(TimeSpan.FromSeconds(4));
        _worker = null;

        lock (_gate) _chunks.Clear();
        Logger.Info("本地唤醒词检测已停止");
    }

    // ---------- 音频积累（NAudio 回调线程） ----------

    private void OnAudioData(byte[] pcm)
    {
        lock (_gate)
        {
            _chunks.Add(pcm);
            _chunkBytes += pcm.Length;
            var maxBytes = SampleRate * 2 * (WindowSeconds + 2);
            while (_chunkBytes > maxBytes && _chunks.Count > 0)
            {
                _chunkBytes -= _chunks[0].Length;
                _chunks.RemoveAt(0);
            }
        }
    }

    // ---------- 识别工作线程 ----------

    private void WorkerLoop()
    {
        SpeechRecognitionEngine? engine = null;
        try
        {
            engine = CreateEngine(); // 引擎在工作线程上创建/使用，避免线程亲和问题
            while (_running)
            {
                Thread.Sleep(BatchIntervalMs);
                if (!_running) break;
                TryRecognizeWindow(engine);
            }
        }
        catch (Exception ex)
        {
            Logger.Error("本地唤醒工作线程异常: " + ex);
        }
        finally
        {
            try { engine?.Dispose(); } catch { /* 忽略 */ }
        }
    }

    private SpeechRecognitionEngine CreateEngine()
    {
        var info = SpeechRecognitionEngine.InstalledRecognizers()
            .FirstOrDefault(r => r.Culture.Name == "zh-CN")
            ?? SpeechRecognitionEngine.InstalledRecognizers().FirstOrDefault()
            ?? throw new InvalidOperationException("系统未安装本地语音识别引擎");

        var engine = new SpeechRecognitionEngine(info);
        var choices = new Choices();
        choices.Add(_config.WakeWordVariants);
        if (!string.IsNullOrWhiteSpace(_config.WakeWord))
            choices.Add(_config.WakeWord);
        var builder = new GrammarBuilder(choices) { Culture = CultureInfo.GetCultureInfo(info.Culture.Name) };
        engine.LoadGrammar(new Grammar(builder) { Name = "wake-words" });
        return engine;
    }

    private void TryRecognizeWindow(SpeechRecognitionEngine engine)
    {
        byte[] wav;
        double avgRms;
        lock (_gate)
        {
            if (_chunks.Count == 0) return;

            // 取最近 WindowSeconds 秒的快照
            var snapshot = new List<byte[]>();
            var take = 0;
            for (var i = _chunks.Count - 1; i >= 0; i--)
            {
                var chunk = _chunks[i];
                snapshot.Add(chunk);
                take += chunk.Length;
                if (take >= SampleRate * 2 * WindowSeconds) break;
            }
            snapshot.Reverse();
            wav = AudioUtils.BuildWavBytes(snapshot);

            // RMS 门控：窗口内几乎没有声音就跳过（省 CPU）
            avgRms = 0;
            foreach (var chunk in snapshot) avgRms += AudioUtils.ComputeRms(chunk);
            avgRms /= Math.Max(1, snapshot.Count);
        }

        // RMS 门控：窗口内几乎没有声音就跳过（省 CPU）。
        // 阈值取用户阈值与实时底噪的较高者：低增益麦克风/环境噪声自适应。
        var gateThreshold = Math.Max(0.006, Math.Max(_config.VadThreshold * 0.5, _audio.CurrentNoiseFloor * 2));
        if (avgRms < gateThreshold)
        {
            // 限频诊断：帮助定位"唤醒不了"（音频过小 / 底噪异常抬高阈值）
            if (DateTime.UtcNow - _lastGateLog > TimeSpan.FromSeconds(10))
            {
                _lastGateLog = DateTime.UtcNow;
                Logger.Info($"唤醒门控跳过（窗口RMS {avgRms:0.0000} < 阈值 {gateThreshold:0.0000}，底噪 {_audio.CurrentNoiseFloor:0.0000}）");
            }
            return;
        }
        if (!IsEnabled) return;

        try
        {
            using var ms = new MemoryStream(wav);
            ms.Position = 0;
            engine.SetInputToAudioStream(ms,
                new SpeechAudioFormatInfo(SampleRate, AudioBitsPerSample.Sixteen, AudioChannel.Mono));

            var result = engine.Recognize();
            if (result == null || !IsEnabled)
            {
                // 限频诊断：引擎对窗口音频无识别结果（增益过高削波 / 音量过小 / 引擎异常）
                if (result == null && DateTime.UtcNow - _lastNullLog > TimeSpan.FromSeconds(10))
                {
                    _lastNullLog = DateTime.UtcNow;
                    Logger.Info($"唤醒引擎无识别结果（窗口 {WindowSeconds}s，RMS {avgRms:0.0000}）");
                }
                return;
            }

            var text = result.Text;
            Logger.Info("本地唤醒引擎识别: " + text + "（conf=" + result.Confidence.ToString("0.00") + "）");

            if (WakeWordMatcher.ContainsWakeWord(text, _config.WakeWordVariants) ||
                (!string.IsNullOrWhiteSpace(_config.WakeWord) &&
                 text.Contains(_config.WakeWord, StringComparison.OrdinalIgnoreCase)))
            {
                Logger.Info("本地唤醒成功: " + text);
                WakeWordDetected?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception ex)
        {
            Logger.Warn("本地唤醒识别异常: " + ex.Message);
        }
    }

    public void Dispose() => Stop();
}
