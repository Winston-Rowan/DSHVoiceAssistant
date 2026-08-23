using DivaVoiceAssistant.Services;
using Xunit;

namespace DivaVoiceAssistant.Tests;

/// <summary>语音识别（多模态网关转写）响应解析测试</summary>
public class SpeechRecognitionServiceTests
{
    [Fact]
    public void ExtractTranscribedText_ValidResponse_ReturnsText()
    {
        const string json = """
            {"output":{"choices":[{"message":{"role":"assistant","content":[{"text":"你好，请打开记事本"}]}}]}}
            """;
        Assert.Equal("你好，请打开记事本", SpeechRecognitionService.ExtractTranscribedText(json));
    }

    [Fact]
    public void ExtractTranscribedText_MultipleContentItems_TakesFirstText()
    {
        const string json = """
            {"output":{"choices":[{"message":{"content":[{"audio":"data:audio/wav;base64,AAA"},{"text":"打开计算器"}]}}]}}
            """;
        Assert.Equal("打开计算器", SpeechRecognitionService.ExtractTranscribedText(json));
    }

    [Fact]
    public void ExtractTranscribedText_NoTextItem_ReturnsNull()
    {
        const string json = """
            {"output":{"choices":[{"message":{"content":[{"audio":"data:audio/wav;base64,AAA"}]}}]}}
            """;
        Assert.Null(SpeechRecognitionService.ExtractTranscribedText(json));
    }

    [Fact]
    public void ExtractTranscribedText_EmptyChoices_ReturnsNull()
    {
        const string json = """{"output":{"choices":[]}}""";
        Assert.Null(SpeechRecognitionService.ExtractTranscribedText(json));
    }

    [Fact]
    public void ExtractTranscribedText_InvalidJson_ReturnsNull()
    {
        Assert.Null(SpeechRecognitionService.ExtractTranscribedText("not json at all"));
        Assert.Null(SpeechRecognitionService.ExtractTranscribedText(""));
    }
}
