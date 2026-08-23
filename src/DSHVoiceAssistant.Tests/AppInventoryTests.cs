using System.IO;
using DSHVoiceAssistant.Utils;
using Xunit;

namespace DSHVoiceAssistant.Tests;

/// <summary>AppInventory（软件归档引擎）纯函数测试</summary>
public class AppInventoryTests
{
    [Theory]
    [InlineData("C:\\Program Files\\Test\\app.exe,0", "C:\\Program Files\\Test\\app.exe")]
    [InlineData("\"C:\\a b\\x.exe\"", "C:\\a b\\x.exe")]
    [InlineData("C:\\plain\\y.exe", "C:\\plain\\y.exe")]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("   ", null)]
    public void CleanIconPath_StripsCommaSuffixAndQuotes(string? input, string? expected)
    {
        Assert.Equal(expected, AppInventory.CleanIconPath(input));
    }

    [Fact]
    public void FindMainExe_DirNameMatch_Wins()
    {
        using var dir = new TempDir();
        File.WriteAllBytes(Path.Combine(dir.Path, "GodOfWar.exe"), new byte[100]);
        File.WriteAllBytes(Path.Combine(dir.Path, "unins000.exe"), new byte[5000]);
        File.WriteAllBytes(Path.Combine(dir.Path, "vcruntime140.dll.exe"), new byte[3000]);

        var hit = AppInventory.FindMainExe(dir.Path, "God of War Ragnarök");
        Assert.NotNull(hit);
        Assert.EndsWith("GodOfWar.exe", hit);
    }

    [Fact]
    public void FindMainExe_DisplayNameMatch_Works()
    {
        using var dir = new TempDir();
        File.WriteAllBytes(Path.Combine(dir.Path, "WeChat.exe"), new byte[100]);

        var hit = AppInventory.FindMainExe(dir.Path, "微信 WeChat");
        Assert.NotNull(hit);
        Assert.EndsWith("WeChat.exe", hit);
    }

    [Fact]
    public void FindMainExe_FallbackLargestNonInstaller()
    {
        using var dir = new TempDir();
        File.WriteAllBytes(Path.Combine(dir.Path, "setup.exe"), new byte[100]);
        File.WriteAllBytes(Path.Combine(dir.Path, "mainapp.exe"), new byte[900]);
        File.WriteAllBytes(Path.Combine(dir.Path, "helper.exe"), new byte[100]);

        var hit = AppInventory.FindMainExe(dir.Path, "某软件");
        Assert.NotNull(hit);
        Assert.EndsWith("mainapp.exe", hit);
    }

    [Fact]
    public void FindMainExe_NoExe_ReturnsNull()
    {
        using var dir = new TempDir();
        File.WriteAllText(Path.Combine(dir.Path, "readme.txt"), "x");
        Assert.Null(AppInventory.FindMainExe(dir.Path, "测试"));
    }

    [Fact]
    public void BuildMarkdown_ContainsEntries()
    {
        var md = AppInventory.BuildMarkdown([
            new AppInventory.Entry("战神5", "D:\\Games\\GoW\\GoW.exe", "桌面快捷方式"),
            new AppInventory.Entry("WeChat", "C:\\WeChat\\WeChat.exe", "注册表卸载项")
        ]);
        Assert.Contains("战神5", md);
        Assert.Contains("D:\\Games\\GoW\\GoW.exe", md);
        Assert.Contains("WeChat", md);
        Assert.Contains("注册表卸载项", md);
    }

    /// <summary>测试用临时目录（自动清理）</summary>
    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dsh-test-" + Guid.NewGuid().ToString("N"));

        public TempDir() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { /* 忽略 */ }
        }
    }
}
