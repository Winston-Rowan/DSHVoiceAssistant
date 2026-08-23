using DSHVoiceAssistant.Models;
using DSHVoiceAssistant.Services;
using Xunit;

namespace DSHVoiceAssistant.Tests;

/// <summary>CommandAction 映射与 CommandProcessor 纯函数测试</summary>
public class CommandProcessorTests
{
    [Theory]
    [InlineData("open_app", CommandAction.OpenApp)]
    [InlineData("OpenApp", CommandAction.OpenApp)]
    [InlineData("open app", CommandAction.OpenApp)]
    [InlineData("web_search", CommandAction.WebSearch)]
    [InlineData("system_command", CommandAction.SystemCommand)]
    [InlineData("file_operation", CommandAction.FileOperation)]
    [InlineData("text_reply", CommandAction.TextReply)]
    [InlineData("custom_script", CommandAction.CustomScript)]
    [InlineData("control_media", CommandAction.ControlMedia)]
    [InlineData("open_url", CommandAction.OpenUrl)]
    [InlineData("随便什么", CommandAction.Unknown)]
    [InlineData("", CommandAction.Unknown)]
    [InlineData(null, CommandAction.Unknown)]
    public void FromString_MapsCorrectly(string? action, CommandAction expected)
    {
        Assert.Equal(expected, CommandActionExtensions.FromString(action));
    }

    [Theory]
    [InlineData("红烧肉的做法", "baidu", "https://www.baidu.com/s?wd=%E7%BA%A2%E7%83%A7%E8%82%89%E7%9A%84%E5%81%9A%E6%B3%95")]
    [InlineData("weather", "bing", "https://www.bing.com/search?q=weather")]
    [InlineData("test", "google", "https://www.google.com/search?q=test")]
    [InlineData("x", "unknown-engine", "https://www.baidu.com/s?wd=x")]
    public void BuildSearchUrl_Works(string query, string engine, string expected)
    {
        Assert.Equal(expected, CommandProcessor.BuildSearchUrl(query, engine));
    }

    [Theory]
    [InlineData("shutdown", "/s /t 5")]
    [InlineData("关机", "/s /t 5")]
    [InlineData("restart", "/r /t 5")]
    [InlineData("reboot", "/r /t 5")]
    [InlineData("重启", "/r /t 5")]
    [InlineData("hibernate", "/h")]
    [InlineData("休眠", "/h")]
    [InlineData("logoff", "/l")]
    [InlineData("注销", "/l")]
    [InlineData("lock", "")]
    [InlineData("sleep", "")]
    [InlineData("unknown", "")]
    public void BuildSystemCommandArgs_Works(string target, string expected)
    {
        Assert.Equal(expected, CommandProcessor.BuildSystemCommandArgs(target));
    }
}
