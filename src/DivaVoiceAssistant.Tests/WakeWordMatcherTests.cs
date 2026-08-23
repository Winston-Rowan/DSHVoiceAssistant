using DivaVoiceAssistant.Utils;
using Xunit;

namespace DivaVoiceAssistant.Tests;

/// <summary>唤醒词匹配测试</summary>
public class WakeWordMatcherTests
{
    private static readonly string[] Variants = ["diva", "迪瓦", "迪娃", "黛娃", "蒂娃"];

    [Theory]
    [InlineData("diva 打开记事本")]
    [InlineData("Diva，帮我搜索天气")]
    [InlineData("迪娃打开计算器")]
    [InlineData("你好迪瓦")]
    public void ContainsWakeWord_Matches(string text)
    {
        Assert.True(WakeWordMatcher.ContainsWakeWord(text, Variants));
    }

    [Theory]
    [InlineData("打开记事本")]
    [InlineData("今天天气怎么样")]
    [InlineData("")]
    [InlineData(null)]
    public void ContainsWakeWord_NotMatches(string? text)
    {
        Assert.False(WakeWordMatcher.ContainsWakeWord(text, Variants));
    }

    [Theory]
    [InlineData("Diva，打开记事本", "打开记事本")]
    [InlineData("迪娃 帮我搜索天气", "帮我搜索天气")]
    [InlineData("diva打开计算器", "打开计算器")]
    public void StripLeadingWakeWord_RemovesLeadingWakeWord(string input, string expected)
    {
        Assert.Equal(expected, WakeWordMatcher.StripLeadingWakeWord(input, Variants));
    }

    [Fact]
    public void StripLeadingWakeWord_MiddleWakeWord_Untouched()
    {
        const string input = "你好Diva";
        Assert.Equal(input, WakeWordMatcher.StripLeadingWakeWord(input, Variants));
    }
}
