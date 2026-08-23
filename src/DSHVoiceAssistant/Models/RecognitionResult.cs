namespace DSHVoiceAssistant.Models;

/// <summary>语音识别结果</summary>
public sealed class RecognitionResult
{
    /// <summary>是否识别成功</summary>
    public bool IsSuccess { get; init; }

    /// <summary>识别成功但内容为空（如纯静音/噪音）</summary>
    public bool IsEmpty { get; init; }

    /// <summary>识别出的文本</summary>
    public string Text { get; init; } = "";

    /// <summary>失败原因（IsSuccess 为 false 时有值）</summary>
    public string ErrorMessage { get; init; } = "";

    public static RecognitionResult Ok(string text) => new() { IsSuccess = true, Text = text };

    public static RecognitionResult Empty(string message = "未识别到内容") => new() { IsEmpty = true, ErrorMessage = message };

    public static RecognitionResult Fail(string message) => new() { ErrorMessage = message };
}
