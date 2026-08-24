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
    public void ShouldShow_WhenWindowVisible_ReturnsFalse(DSHState state)
    {
        Assert.False(EdgeGlowVisibility.ShouldShow(true, true, state));
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
