using DSHVoiceAssistant.Models;
using DSHVoiceAssistant.Services;
using Xunit;

namespace DSHVoiceAssistant.Tests;

/// <summary>DSH 多轮记忆与消息组装测试</summary>
public class DSHCommandServiceTests
{
    [Fact]
    public void BuildMessages_NoHistory_SystemPlusUser()
    {
        var messages = DSHCommandService.BuildMessages("系统提示", [], "打开记事本", 6);
        Assert.Equal(2, messages.Count);
        Assert.Equal("system", messages[0].Role);
        Assert.Equal("user", messages[1].Role);
        Assert.Equal("打开记事本", messages[1].Content);
    }

    [Fact]
    public void BuildMessages_HistoryIncluded_InOrder()
    {
        var history = new List<DSHChatMessage>
        {
            new() { Role = "user", Content = "你好" },
            new() { Role = "assistant", Content = "你好呀" }
        };
        var messages = DSHCommandService.BuildMessages("系统提示", history, "今天天气", 6);

        Assert.Equal(4, messages.Count);
        Assert.Equal("system", messages[0].Role);
        Assert.Equal("你好", messages[1].Content);
        Assert.Equal("你好呀", messages[2].Content);
        Assert.Equal("今天天气", messages[3].Content);
    }

    [Fact]
    public void BuildMessages_HistoryBeyondRounds_TrimsOldest()
    {
        // 7 轮历史（14 条），maxRounds=6 → 只保留最近 6 轮（12 条）
        var history = new List<DSHChatMessage>();
        for (var i = 0; i < 7; i++)
        {
            history.Add(new DSHChatMessage { Role = "user", Content = "u" + i });
            history.Add(new DSHChatMessage { Role = "assistant", Content = "a" + i });
        }
        var messages = DSHCommandService.BuildMessages("系统提示", history, "当前", 6);

        Assert.Equal(1 + 12 + 1, messages.Count);
        // 最旧的 u0/a0 被丢弃，从 u1 开始
        Assert.Equal("u1", messages[1].Content);
        Assert.Equal("当前", messages[^1].Content);
    }

    [Fact]
    public void BuildMessages_ZeroRounds_DisablesHistory()
    {
        var history = new List<DSHChatMessage>
        {
            new() { Role = "user", Content = "旧对话" },
            new() { Role = "assistant", Content = "旧回复" }
        };
        var messages = DSHCommandService.BuildMessages("系统提示", history, "新指令", 0);
        Assert.Equal(2, messages.Count); // 只有 system + 当前指令
    }
}
