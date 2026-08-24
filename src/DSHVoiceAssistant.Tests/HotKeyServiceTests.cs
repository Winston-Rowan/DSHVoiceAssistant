using DSHVoiceAssistant.Services;
using Xunit;

namespace DSHVoiceAssistant.Tests;

/// <summary>全局快捷键组合解析测试</summary>
public class HotKeyServiceTests
{
    [Theory]
    [InlineData("Ctrl+Alt+D", HotKeyService.ModControl | HotKeyService.ModAlt, 0x44)]
    [InlineData("ctrl+alt+d", HotKeyService.ModControl | HotKeyService.ModAlt, 0x44)]
    [InlineData("Ctrl+Shift+F1", HotKeyService.ModControl | HotKeyService.ModShift, 0x70)]
    [InlineData("Ctrl+F12", HotKeyService.ModControl, 0x7B)]
    [InlineData("Alt+F5", HotKeyService.ModAlt, 0x74)]
    [InlineData("Win+Space", HotKeyService.ModWin, 0x20)]
    [InlineData("Ctrl+Alt+Shift+Win+P", HotKeyService.ModControl | HotKeyService.ModAlt | HotKeyService.ModShift | HotKeyService.ModWin, 0x50)]
    [InlineData("Ctrl+0", HotKeyService.ModControl, 0x30)]
    [InlineData("Ctrl+9", HotKeyService.ModControl, 0x39)]
    [InlineData("Shift+A", HotKeyService.ModShift, 0x41)]
    [InlineData("Ctrl+-", HotKeyService.ModControl, 0xBD)]
    [InlineData("Ctrl+=", HotKeyService.ModControl, 0xBB)]
    [InlineData("Ctrl+[", HotKeyService.ModControl, 0xDB)]
    [InlineData("Ctrl+]", HotKeyService.ModControl, 0xDD)]
    [InlineData("Ctrl+\\", HotKeyService.ModControl, 0xDC)]
    [InlineData("Ctrl+;", HotKeyService.ModControl, 0xBA)]
    [InlineData("Ctrl+'", HotKeyService.ModControl, 0xDE)]
    [InlineData("Ctrl+,", HotKeyService.ModControl, 0xBC)]
    [InlineData("Ctrl+.", HotKeyService.ModControl, 0xBE)]
    [InlineData("Ctrl+/", HotKeyService.ModControl, 0xBF)]
    [InlineData("Ctrl+`", HotKeyService.ModControl, 0xC0)]
    [InlineData("Ctrl+Enter", HotKeyService.ModControl, 0x0D)]
    [InlineData("Ctrl+Tab", HotKeyService.ModControl, 0x09)]
    [InlineData("Ctrl+Esc", HotKeyService.ModControl, 0x1B)]
    [InlineData("Ctrl+Back", HotKeyService.ModControl, 0x08)]
    [InlineData("Ctrl+Del", HotKeyService.ModControl, 0x2E)]
    [InlineData("Ctrl+Ins", HotKeyService.ModControl, 0x2D)]
    [InlineData("Ctrl+Up", HotKeyService.ModControl, 0x26)]
    [InlineData("Ctrl+Down", HotKeyService.ModControl, 0x28)]
    [InlineData("Ctrl+Left", HotKeyService.ModControl, 0x25)]
    [InlineData("Ctrl+Right", HotKeyService.ModControl, 0x27)]
    [InlineData("Ctrl+Home", HotKeyService.ModControl, 0x24)]
    [InlineData("Ctrl+End", HotKeyService.ModControl, 0x23)]
    [InlineData("Ctrl+PgUp", HotKeyService.ModControl, 0x21)]
    [InlineData("Ctrl+PgDn", HotKeyService.ModControl, 0x22)]
    [InlineData("Ctrl+PrtSc", HotKeyService.ModControl, 0x2C)]
    [InlineData("Ctrl+Pause", HotKeyService.ModControl, 0x13)]
    public void TryParse_ValidCombos(string combo, uint expectedMods, uint expectedVk)
    {
        Assert.True(HotKeyService.TryParse(combo, out var mods, out var vk, out var error), error);
        Assert.Equal(expectedMods, mods);
        Assert.Equal(expectedVk, vk);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("D")]                    // 无修饰键
    [InlineData("Ctrl")]                 // 无主键
    [InlineData("Ctrl+Alt+D+E")]         // 两个主键
    [InlineData("Ctrl+Ctrl+D")]          // 修饰键重复
    [InlineData("Ctrl+Qux")]             // 未知主键
    [InlineData("Ctrl+ф")]               // 非 ASCII 主键
    [InlineData("Foo+Bar")]              // 全部无法识别
    [InlineData("Ctrl+Alt+Shift+Win")]   // 只有修饰键
    public void TryParse_InvalidCombos_ReturnsFalse(string? combo)
    {
        Assert.False(HotKeyService.TryParse(combo!, out _, out _, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error), "失败时应返回中文错误信息");
    }
}
