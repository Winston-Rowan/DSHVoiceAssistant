using DSHVoiceAssistant.Models;

namespace DSHVoiceAssistant.Services;

/// <summary>
/// DSH 编排器接口：串联 唤醒 → 录音 → 识别 → DSH 决策 → 执行 → TTS 全流程的状态机。
/// </summary>
public interface IDSHOrchestrator : IDisposable
{
    /// <summary>当前状态</summary>
    DSHState State { get; }

    /// <summary>是否已静音（暂停采集）</summary>
    bool IsMuted { get; }

    /// <summary>启动监听（应用启动时调用）</summary>
    void Start();

    /// <summary>停止监听（应用退出时调用）</summary>
    void Stop();

    /// <summary>立即进入倾听状态（快捷键/按钮触发，跳过唤醒词）</summary>
    void ForceActivate();

    /// <summary>静音/取消静音（暂停或恢复麦克风采集与唤醒检测）</summary>
    void ToggleMute();

    /// <summary>结束当前对话（ESC/语音结束词条）：中断在途流水线、停止播报、回到待命</summary>
    void EndConversation();

    /// <summary>状态变化（msg 为状态说明文字）</summary>
    event Action<DSHState, string>? StateChanged;

    /// <summary>音量变化（0~1，音频线程触发，供波形可视化）</summary>
    event Action<float>? LevelChanged;

    /// <summary>识别出用户指令文本</summary>
    event Action<string>? TextRecognized;

    /// <summary>DSH 返回回复内容</summary>
    event Action<string>? DSHReplied;

    /// <summary>发生错误（msg 为错误描述）</summary>
    event Action<string>? ErrorOccurred;

    /// <summary>检测到唤醒词</summary>
    event Action? WakeWordHeard;
}
