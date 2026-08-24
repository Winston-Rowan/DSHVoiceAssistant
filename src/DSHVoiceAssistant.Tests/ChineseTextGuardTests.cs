using DSHVoiceAssistant.Utils;
using Xunit;

namespace DSHVoiceAssistant.Tests;

/// <summary>中文回复保障检测测试</summary>
public class ChineseTextGuardTests
{
    [Theory]
    [InlineData("正在为您打开记事本")]
    [InlineData("好的，5分钟后提醒您喝水")]
    [InlineData("D盘剩余空间：30.5 GB")]     // 中文为主，英文术语少 → 不需要翻译
    [InlineData("")]
    [InlineData(null)]
    public void NeedsTranslation_ChineseText_ReturnsFalse(string? text)
    {
        Assert.False(ChineseTextGuard.NeedsTranslation(text));
    }

    [Theory]
    [InlineData("Hello, how are you?")]
    [InlineData("This is a very long English sentence that should be translated into Chinese")]
    [InlineData("こんにちは、元気ですか")]   // 日文假名 → 需要翻译
    [InlineData("안녕하세요")]                // 谚文 → 需要翻译
    public void NeedsTranslation_ForeignText_ReturnsTrue(string text)
    {
        Assert.True(ChineseTextGuard.NeedsTranslation(text));
    }

    [Theory]
    [InlineData("こんにちは")]
    [InlineData("テストです")]
    [InlineData("ハロー")]
    public void ContainsJapaneseKana_DetectsKana(string text)
    {
        Assert.True(ChineseTextGuard.ContainsJapaneseKana(text));
    }

    [Theory]
    [InlineData("你好")]
    [InlineData("Hello")]
    [InlineData("")]
    public void ContainsJapaneseKana_NoKana_ReturnsFalse(string text)
    {
        Assert.False(ChineseTextGuard.ContainsJapaneseKana(text));
    }
}
