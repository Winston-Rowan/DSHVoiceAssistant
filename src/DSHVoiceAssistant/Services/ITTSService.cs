namespace DSHVoiceAssistant.Services;

/// <summary>TTS 语音合成服务接口（Windows 内置 SpeechSynthesizer）</summary>
public interface ITTSService
{
    /// <summary>是否正在朗读</summary>
    bool IsSpeaking { get; }

    /// <summary>异步朗读文本（等待朗读结束；可随时 Stop 打断）</summary>
    Task SpeakAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>打断当前朗读</summary>
    void Stop();
}
