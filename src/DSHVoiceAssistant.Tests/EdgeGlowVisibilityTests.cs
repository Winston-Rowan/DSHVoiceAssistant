using DSHVoiceAssistant.Controls;
using DSHVoiceAssistant.Models;
using Xunit;

namespace DSHVoiceAssistant.Tests;

/// <summary>屏幕边缘光晕可见性判定测试</summary>
public class EdgeGlowVisibilityTests
{
    [Theory]
    [InlineData(DSHState.Recording)]
    [InlineData(DSHState.Transcribing)]
    [InlineData(DSHState.Thinking)]
    [InlineData(DSHState.Executing)]
    [InlineData(DSHState.Speaking)]
    public void ShouldShow_WhenBusyAndWindowHidden(DSHState state)
    {
        Assert.True(EdgeGlowVisibility.ShouldShow(enabled: true, mainWindowVisible: false, state));
    }

    [Theory]
    [InlineData(DSHState.Idle)]
    [InlineData(DSHState.WakeChecking)]
    [InlineData(DSHState.Error)]
    public void ShouldShow_WhenIdle_ReturnsFalse(DSHState state)
    {
        Assert.False(EdgeGlowVisibility.ShouldShow(true, false, state));
    }

    [Theory]
    [InlineData(DSHState.Idle)]
    [InlineData(DSHState.Recording)]
    [InlineData(DSHState.Speaking)]
    public void ShouldShow_WhenWindowVisible_StillShowsWhenBusy(DSHState state)
    {
        // 窗口打开时同样展示光效（只受忙碌状态约束）
        Assert.Equal(
            state is DSHState.Recording or DSHState.Transcribing or DSHState.Thinking
                or DSHState.Executing or DSHState.Speaking,
            EdgeGlowVisibility.ShouldShow(true, true, state));
    }

    [Theory]
    [InlineData(DSHState.Recording)]
    [InlineData(DSHState.Speaking)]
    [InlineData(DSHState.Idle)]
    public void ShouldShow_WhenDisabled_ReturnsFalse(DSHState state)
    {
        Assert.False(EdgeGlowVisibility.ShouldShow(false, false, state));
    }
}
