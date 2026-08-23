namespace DSHVoiceAssistant.Services;

/// <summary>唤醒词检测服务接口</summary>
public interface IWakeWordDetection
{
    /// <summary>检测到唤醒词（线程不保证为 UI 线程）</summary>
    event EventHandler? WakeWordDetected;

    /// <summary>是否启用检测（处理指令期间由编排器关闭，避免重复唤醒）</summary>
    bool IsEnabled { get; set; }

    /// <summary>开始监听（订阅音频事件）</summary>
    void Start();

    /// <summary>停止监听</summary>
    void Stop();
}
