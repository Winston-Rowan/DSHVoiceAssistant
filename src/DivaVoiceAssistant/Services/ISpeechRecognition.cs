using DivaVoiceAssistant.Models;

namespace DivaVoiceAssistant.Services;

/// <summary>语音识别服务接口（对接百炼 compatible-mode /audio/transcriptions）</summary>
public interface ISpeechRecognition
{
    /// <summary>识别 WAV 音频数据，返回文本。</summary>
    Task<RecognitionResult> RecognizeAsync(byte[] wavData, CancellationToken cancellationToken = default);
}
