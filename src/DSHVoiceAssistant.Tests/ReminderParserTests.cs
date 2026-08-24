using DSHVoiceAssistant.Utils;
using Xunit;

namespace DSHVoiceAssistant.Tests;

/// <summary>闹钟/提醒参数解析测试</summary>
public class ReminderParserTests
{
    [Theory]
    [InlineData("5", 5)]
    [InlineData("0.5", 0.5)]
    [InlineData("1440", 1440)]
    public void ParseDelay_Minutes_Valid(string minutes, double expected)
    {
        var delay = ReminderParser.ParseDelay(new Dictionary<string, string> { ["minutes"] = minutes });
        Assert.NotNull(delay);
        Assert.Equal(expected, delay!.Value.TotalMinutes, precision: 3);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("abc")]
    [InlineData("1441")]
    public void ParseDelay_Minutes_Invalid_ReturnsNull(string minutes)
    {
        Assert.Null(ReminderParser.ParseDelay(new Dictionary<string, string> { ["minutes"] = minutes }));
    }

    [Fact]
    public void ParseDelay_At_TomorrowWhenPast()
    {
        // at 解析为今天 0 点起的时刻；若已过则顺延一天
        var at = new TimeSpan(23, 59, 0);
        var delay = ReminderParser.ParseDelay(new Dictionary<string, string> { ["at"] = at.ToString(@"hh\:mm") });
        Assert.NotNull(delay);
        // 现在要么是 23:59 前（今天）要么已过（明天），间隔都是正数
        Assert.True(delay!.Value.TotalMinutes > 0);
    }

    [Fact]
    public void ParseDelay_NullParams_ReturnsNull()
    {
        Assert.Null(ReminderParser.ParseDelay(null));
    }

    [Fact]
    public void GetMessage_ReturnsTrimmed()
    {
        Assert.Equal("该喝水啦", ReminderParser.GetMessage(new Dictionary<string, string> { ["message"] = " 该喝水啦 " }));
    }

    [Fact]
    public void GetMessage_Missing_ReturnsNull()
    {
        Assert.Null(ReminderParser.GetMessage(new Dictionary<string, string> { ["minutes"] = "5" }));
        Assert.Null(ReminderParser.GetMessage(null));
    }
}
