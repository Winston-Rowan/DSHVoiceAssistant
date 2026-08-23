using DSHVoiceAssistant.Utils;
using Xunit;

namespace DSHVoiceAssistant.Tests;

/// <summary>JsonUtils.ExtractJsonObject 测试</summary>
public class JsonUtilsTests
{
    [Fact]
    public void ExtractJsonObject_PlainJson_ReturnsJson()
    {
        const string input = "{\"action\":\"open_app\",\"target\":\"notepad.exe\"}";
        Assert.Equal(input, JsonUtils.ExtractJsonObject(input));
    }

    [Fact]
    public void ExtractJsonObject_MarkdownFenced_ReturnsJson()
    {
        const string input = "```json\n{\"action\":\"web_search\",\"target\":\"天气\"}\n```";
        Assert.Equal("{\"action\":\"web_search\",\"target\":\"天气\"}", JsonUtils.ExtractJsonObject(input));
    }

    [Fact]
    public void ExtractJsonObject_WithTrailingText_ReturnsJson()
    {
        const string input = "{\"action\":\"text_reply\",\"response\":\"你好\"} 好的，已处理完毕！";
        Assert.Equal("{\"action\":\"text_reply\",\"response\":\"你好\"}", JsonUtils.ExtractJsonObject(input));
    }

    [Fact]
    public void ExtractJsonObject_BracesInsideString_Ignored()
    {
        // 字符串里的 { } 不应破坏括号配对
        const string input = "{\"target\":\"a{b}c\",\"response\":\"x\"}";
        Assert.Equal(input, JsonUtils.ExtractJsonObject(input));
    }

    [Fact]
    public void ExtractJsonObject_NoJson_ReturnsNull()
    {
        Assert.Null(JsonUtils.ExtractJsonObject("今天天气不错"));
        Assert.Null(JsonUtils.ExtractJsonObject(""));
        Assert.Null(JsonUtils.ExtractJsonObject(null));
    }
}
