using DSHVoiceAssistant.Controls;
using DSHVoiceAssistant.Models;
using Xunit;

namespace DSHVoiceAssistant.Tests;

/// <summary>对话浮层可见性判定测试</summary>
public class ConversationOverlayVisibilityTests
{
    [Theory]
    [InlineData(DSHState.Transcribing)]
    [InlineData(DSHState.Thinking)]
    [InlineData(DSHState.Executing)]
    [InlineData(DSHState.Speaking)]
    public void ShouldShow_WhenBusyAndWindowHidden(DSHState state)
    {
        Assert.True(ConversationOverlayVisibility.ShouldShow(true, false, state));
    }

    [Theory]
    [InlineData(DSHState.Idle)]
    [InlineData(DSHState.WakeChecking)]
    [InlineData(DSHState.Recording)] // 倾听中尚无文字内容
    [InlineData(DSHState.Error)]
    public void ShouldShow_WhenIdleOrRecording_ReturnsFalse(DSHState state)
    {
        Assert.False(ConversationOverlayVisibility.ShouldShow(true, false, state));
    }

    [Theory]
    [InlineData(DSHState.Transcribing)]
    [InlineData(DSHState.Speaking)]
    public void ShouldShow_WhenWindowVisible_ReturnsFalse(DSHState state)
    {
        Assert.False(ConversationOverlayVisibility.ShouldShow(true, true, state));
    }

    [Theory]
    [InlineData(DSHState.Transcribing)]
    [InlineData(DSHState.Speaking)]
    [InlineData(DSHState.Idle)]
    public void ShouldShow_WhenDisabled_ReturnsFalse(DSHState state)
    {
        Assert.False(ConversationOverlayVisibility.ShouldShow(false, false, state));
    }
}
