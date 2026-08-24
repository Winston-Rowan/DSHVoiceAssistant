using DSHVoiceAssistant.Utils;
using Xunit;

namespace DSHVoiceAssistant.Tests;

/// <summary>语音结束对话匹配测试</summary>
public class EndConversationMatcherTests
{
    [Theory]
    [InlineData("没事了")]
    [InlineData("没事了。")]
    [InlineData("没事儿了")]
    [InlineData("退下")]
    [InlineData("退下吧")]
    [InlineData("你退下吧")]
    [InlineData("退下吧你")]
    [InlineData("滚吧")]
    [InlineData("滚开")]
    [InlineData("滚蛋")]
    [InlineData("不聊了")]
    [InlineData("结束对话")]
    [InlineData("结束吧")]
    [InlineData("再见")]
    [InlineData("再见吧")]
    [InlineData("拜拜")]
    [InlineData("没你事了")]
    [InlineData("没别的事了")]
    [InlineData("就这样吧")]
    [InlineData("到此为止")]
    [InlineData("不用了")]
    [InlineData("不用了谢谢")]
    [InlineData(" 退下 ")]
    [InlineData("退下！")]
    public void IsEndPhrase_Matches(string text)
    {
        Assert.True(EndConversationMatcher.IsEndPhrase(text));
    }

    [Theory]
    [InlineData("打开记事本")]
    [InlineData("搜索深圳天气")]
    [InlineData("今天天气怎么样")]
    [InlineData("滚动的天空怎么打开")]   // 含"滚"但不以"滚吧/滚开/滚蛋"开头
    [InlineData("我现在没事了")]         // 不以词条开头
    [InlineData("")]
    [InlineData(null)]
    [InlineData("你好二狗")]
    public void IsEndPhrase_NotMatches(string? text)
    {
        Assert.False(EndConversationMatcher.IsEndPhrase(text));
    }

    [Theory]
    [InlineData("没事了。", "没事了")]
    [InlineData(" 退下 吧 ", "退下吧")]
    [InlineData("再见，拜拜！", "再见拜拜")]
    public void Normalize_RemovesWhitespaceAndPunctuation(string input, string expected)
    {
        Assert.Equal(expected, EndConversationMatcher.Normalize(input));
    }
}
