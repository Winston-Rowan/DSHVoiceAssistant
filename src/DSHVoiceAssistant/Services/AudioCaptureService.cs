using DSHVoiceAssistant.Models;
using DSHVoiceAssistant.Utils;
using NAudio.Wave;

namespace DSHVoiceAssistant.Services;

/// <summary>
/// NAudio 麦克风采集实现。以 16kHz / 16bit / 单声道采集，每 100ms 推送一帧。
/// 事件在 NAudio 回调线程触发，调用方注意线程调度。
/// </summary>
public sealed class AudioCaptureService : IAudioCapture, IDisposable
{
    private static readonly AudioConfig Audio = new();
    private readonly DSHConfig _config;
    private readonly object _lock = new();
    private WaveInEvent? _waveIn;
    private double _noiseFloor = 0.002;

    public AudioCaptureService(DSHConfig config) => _config = config;

    public bool IsCapturing { get; private set; }

    /// <summary>实时噪声底噪估计（归一化 RMS，EMA 自适应；安静时缓慢更新，说话时不污染）</summary>
    public double CurrentNoiseFloor => Volatile.Read(ref _noiseFloor);

    public int DeviceNumber
    {
        get => _config.MicDeviceNumber;
        set => _config.MicDeviceNumber = value;
    }

    public event Action<byte[]>? DataAvailable;

    /// <summary>原始（未增益）PCM 帧（音频线程触发）。供本地 SAPI 唤醒等对增益音频不兼容的消费方使用。</summary>
    public event Action<byte[]>? RawDataAvailable;

    public event Action<float>? LevelChanged;

    /// <summary>枚举系统麦克风设备名称（设置窗口用）</summary>
    public static IReadOnlyList<string> GetDeviceNames()
    {
        try
        {
            var names = new List<string>();
            for (var i = 0; i < WaveInEvent.DeviceCount; i++)
            {
                var caps = WaveInEvent.GetCapabilities(i);
                names.Add(caps.ProductName);
            }
            return names;
        }
        catch (Exception ex)
        {
            Logger.Warn("枚举麦克风设备失败: " + ex.Message);
            return Array.Empty<string>();
        }
    }

    public void Start()
    {
        lock (_lock)
        {
            if (IsCapturing) return;

            try
            {
                var device = Math.Clamp(_config.MicDeviceNumber, 0, Math.Max(0, WaveInEvent.DeviceCount - 1));
                _waveIn = new WaveInEvent
                {
                    DeviceNumber = device,
                    WaveFormat = new WaveFormat(Audio.SampleRate, Audio.BitsPerSample, Audio.Channels),
                    BufferMilliseconds = Audio.BufferMilliseconds
                };
                _waveIn.DataAvailable += OnDataAvailable;
                _waveIn.RecordingStopped += OnRecordingStopped;
                _waveIn.StartRecording();
                IsCapturing = true;
                Volatile.Write(ref _noiseFloor, 0.002); // 底噪从零开始重新自适应
                Logger.Info($"麦克风已启动（设备 {device}，数字增益 {_config.MicGain:0.0}x）");
            }
            catch (Exception ex)
            {
                Logger.Error("麦克风启动失败: " + ex.Message);
                IsCapturing = false;
                _waveIn?.Dispose();
                _waveIn = null;
                throw;
            }
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            try { _waveIn?.StopRecording(); } catch { /* 忽略停止异常 */ }
            try { _waveIn?.Dispose(); } catch { /* 忽略释放异常 */ }
            _waveIn = null;
            IsCapturing = false;
            Logger.Info("麦克风已停止");
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception != null) Logger.Warn("录音意外停止: " + e.Exception.Message);
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        try
        {
            if (e.BytesRecorded <= 0) return;
            var raw = new byte[e.BytesRecorded];
            Buffer.BlockCopy(e.Buffer, 0, raw, 0, e.BytesRecorded);

            // 低增益麦克风适配：采集源头统一放大（含削波钳位），云端识别等下游受益。
            // 本地 SAPI 唤醒引擎对增益/削波音频识别不佳，单独走原始数据（RawDataAvailable）。
            RawDataAvailable?.Invoke(raw);
            var data = AudioUtils.ApplyGain(raw, _config.MicGain);

            var rms = AudioUtils.ComputeRms(data);
            UpdateNoiseFloor(rms);
            DataAvailable?.Invoke(data);
            LevelChanged?.Invoke(rms);
        }
        catch (Exception ex)
        {
            Logger.Error("音频回调异常: " + ex.Message);
        }
    }

    /// <summary>
    /// EMA 自适应底噪：安静帧（低于 4 倍底噪，即低于语音判定阈值）缓慢更新，
    /// 说话帧不参与，避免底噪被语音抬高。封顶 0.008：防止底噪估计异常爬升，
    /// 把唤醒门控/VAD 阈值顶到说话音量都够不着的高度。
    /// </summary>
    private void UpdateNoiseFloor(float rms)
    {
        const double floorCap = 0.008;
        var floor = Volatile.Read(ref _noiseFloor);
        if (rms < floor * 4)
        {
            var next = floor * 0.98 + rms * 0.02;
            Volatile.Write(ref _noiseFloor, Math.Min(floorCap, next));
        }
    }

    public void Dispose() => Stop();
}
