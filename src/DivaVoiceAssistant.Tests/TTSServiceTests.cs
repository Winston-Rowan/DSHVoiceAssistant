using DivaVoiceAssistant.Services;
using Xunit;

namespace DivaVoiceAssistant.Tests;

/// <summary>云端 TTS 合成响应解析测试</summary>
public class TTSServiceTests
{
    [Fact]
    public void ExtractAudioUrl_ValidResponse_ReturnsUrl()
    {
        const string json = """
            {"output":{"audio":{"data":"","expires_at":1787545578,"id":"audio_abc","url":"http://dashscope-result-bj.oss-cn-beijing.aliyuncs.com/prod/qwen3-tts/123/abc.wav?Expires=1787545578"}}}
            """;
        var url = TTSService.ExtractAudioUrl(json);
        Assert.NotNull(url);
        Assert.StartsWith("http", url);
    }

    [Fact]
    public void ExtractAudioUrl_Base64Data_ReturnsDataUri()
    {
        const string json = """
            {"output":{"audio":{"data":"data:audio/wav;base64,UklGRg==","url":""}}}
            """;
        var url = TTSService.ExtractAudioUrl(json);
        Assert.NotNull(url);
        Assert.StartsWith("data:", url);
    }

    [Fact]
    public void ExtractAudioUrl_NoAudio_ReturnsNull()
    {
        const string json = """{"output":{"choices":[]}}""";
        Assert.Null(TTSService.ExtractAudioUrl(json));
    }

    [Fact]
    public void ExtractAudioUrl_InvalidJson_ReturnsNull()
    {
        Assert.Null(TTSService.ExtractAudioUrl("not json"));
        Assert.Null(TTSService.ExtractAudioUrl(""));
    }
}
