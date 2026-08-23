namespace DivaVoiceAssistant.Models;

/// <summary>
/// 音频采集参数。采样率统一使用 16kHz / 16bit / 单声道（语音识别 API 的标准输入）。
/// </summary>
public sealed class AudioConfig
{
    /// <summary>采样率（Hz）</summary>
    public int SampleRate { get; init; } = 16000;

    /// <summary>位深（bit）</summary>
    public int BitsPerSample { get; init; } = 16;

    /// <summary>声道数</summary>
    public int Channels { get; init; } = 1;

    /// <summary>每次回调的缓冲时长（毫秒）</summary>
    public int BufferMilliseconds { get; init; } = 100;

    /// <summary>单帧字节数 = 采样率 × 位深/8 × 声道 × 秒数</summary>
    public int BytesPerBuffer => SampleRate * (BitsPerSample / 8) * Channels * BufferMilliseconds / 1000;
}
