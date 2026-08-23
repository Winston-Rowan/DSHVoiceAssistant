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

    public AudioCaptureService(DSHConfig config) => _config = config;

    public bool IsCapturing { get; private set; }

    public int DeviceNumber
    {
        get => _config.MicDeviceNumber;
        set => _config.MicDeviceNumber = value;
    }

    public event Action<byte[]>? DataAvailable;

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
                Logger.Info($"麦克风已启动（设备 {device}）");
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
            var data = new byte[e.BytesRecorded];
            Buffer.BlockCopy(e.Buffer, 0, data, 0, e.BytesRecorded);

            var rms = AudioUtils.ComputeRms(data);
            DataAvailable?.Invoke(data);
            LevelChanged?.Invoke(rms);
        }
        catch (Exception ex)
        {
            Logger.Error("音频回调异常: " + ex.Message);
        }
    }

    public void Dispose() => Stop();
}
