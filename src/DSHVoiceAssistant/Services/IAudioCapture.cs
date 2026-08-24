namespace DSHVoiceAssistant.Services;

/// <summary>麦克风采集服务接口（16kHz / 16bit / 单声道 PCM）</summary>
public interface IAudioCapture
{
    /// <summary>是否正在采集</summary>
    bool IsCapturing { get; }

    /// <summary>实时噪声底噪估计（归一化 RMS，自适应；VAD/唤醒门控用于兜底阈值）</summary>
    double CurrentNoiseFloor { get; }

    /// <summary>当前使用的麦克风设备编号（可在设置中修改）</summary>
    int DeviceNumber { get; set; }

    /// <summary>采集到一帧 PCM 原始数据（音频线程触发）</summary>
    event Action<byte[]>? DataAvailable;

    /// <summary>音量变化（归一化 RMS 0~1，音频线程触发，用于波形与 VAD）</summary>
    event Action<float>? LevelChanged;

    /// <summary>开始采集</summary>
    void Start();

    /// <summary>停止采集</summary>
    void Stop();
}
