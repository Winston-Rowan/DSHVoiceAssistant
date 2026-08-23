using DivaVoiceAssistant.Models;
using DivaVoiceAssistant.Utils;
using Xunit;

namespace DivaVoiceAssistant.Tests;

/// <summary>DivaCommandParser 测试（DSH 返回内容解析）</summary>
public class DivaCommandParserTests
{
    [Fact]
    public void Parse_ValidJson_ReturnsAllFields()
    {
        var result = DivaCommandParser.Parse("{\"action\":\"open_app\",\"target\":\"notepad.exe\",\"response\":\"好的，正在为您打开记事本\"}");

        Assert.True(result.Success);
        Assert.Equal("open_app", result.Action);
        Assert.Equal("notepad.exe", result.Target);
        Assert.Equal("好的，正在为您打开记事本", result.Response);
    }

    [Fact]
    public void Parse_ActionUppercase_NormalizedToLower()
    {
        // 解析器只做小写化（不补下划线）；"OpenApp" 与 "open_app" 均可被 FromString 识别
        var result = DivaCommandParser.Parse("{\"action\":\"OpenApp\",\"target\":\"calc.exe\"}");
        Assert.Equal("openapp", result.Action);
        Assert.Equal(CommandAction.OpenApp, CommandActionExtensions.FromString(result.Action));
    }

    [Fact]
    public void Parse_FencedJson_Works()
    {
        var result = DivaCommandParser.Parse("```json\n{\"action\":\"web_search\",\"target\":\"深圳天气\"}\n```");
        Assert.Equal("web_search", result.Action);
        Assert.Equal("深圳天气", result.Target);
    }

    [Fact]
    public void Parse_NoAction_FallsBackToTextReply()
    {
        var result = DivaCommandParser.Parse("{\"target\":\"x\"}");
        Assert.Equal("text_reply", result.Action);
    }

    [Fact]
    public void Parse_PlainText_FallsBackToTextReply()
    {
        var result = DivaCommandParser.Parse("你好呀，我是Diva");
        Assert.Equal("text_reply", result.Action);
        Assert.Equal("你好呀，我是Diva", result.Response);
    }

    [Fact]
    public void Parse_Params_Kept()
    {
        var result = DivaCommandParser.Parse("{\"action\":\"file_operation\",\"target\":\"C:\\\\a.txt\",\"params\":{\"operation\":\"open\"}}");
        Assert.Equal("open", result.Params!["operation"]);
    }
}
