namespace DivaVoiceAssistant.Models;

/// <summary>Diva 助手运行状态（状态机）</summary>
public enum DivaState
{
    /// <summary>待命中（等待唤醒词或快捷键）</summary>
    Idle,

    /// <summary>检测唤醒词中（正在调用语音识别核对唤醒词）</summary>
    WakeChecking,

    /// <summary>倾听中（正在录制用户指令）</summary>
    Recording,

    /// <summary>语音识别中</summary>
    Transcribing,

    /// <summary>DSH 正在理解指令</summary>
    Thinking,

    /// <summary>执行指令中</summary>
    Executing,

    /// <summary>TTS 语音回复中</summary>
    Speaking,

    /// <summary>出错（短暂停留后自动回到待命）</summary>
    Error
}
