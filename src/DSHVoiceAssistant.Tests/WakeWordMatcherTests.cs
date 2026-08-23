using DSHVoiceAssistant.Utils;
using Xunit;

namespace DSHVoiceAssistant.Tests;

/// <summary>唤醒词匹配测试</summary>
public class WakeWordMatcherTests
{
    private static readonly string[] Variants = ["二狗", "尔苟", "ergou"];

    [Theory]
    [InlineData("二狗 打开记事本")]
    [InlineData("二狗，帮我搜索天气")]
    [InlineData("尔苟打开计算器")]
    [InlineData("你好ergou")]
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
    [InlineData("二狗，打开记事本", "打开记事本")]
    [InlineData("尔苟 帮我搜索天气", "帮我搜索天气")]
    [InlineData("二狗打开计算器", "打开计算器")]
    public void StripLeadingWakeWord_RemovesLeadingWakeWord(string input, string expected)
    {
        Assert.Equal(expected, WakeWordMatcher.StripLeadingWakeWord(input, Variants));
    }

    [Fact]
    public void StripLeadingWakeWord_MiddleWakeWord_Untouched()
    {
        const string input = "你好二狗";
        Assert.Equal(input, WakeWordMatcher.StripLeadingWakeWord(input, Variants));
    }

    [Theory]
    [InlineData("二狗", 1)]              // 中文无大小写/空格变化
    [InlineData("Er Gou", 3)]            // 原词 + 小写 + 去空格
    [InlineData("", 0)]
    [InlineData(null, 0)]
    public void BuildDefaultVariants_Works(string? word, int expectedCount)
    {
        var variants = WakeWordMatcher.BuildDefaultVariants(word);
        Assert.Equal(expectedCount, variants.Length);
        if (word is { Length: > 0 })
        {
            Assert.Contains(word, variants);
            Assert.Equal(variants.Distinct().Count(), variants.Length); // 无重复
        }
    }
}
