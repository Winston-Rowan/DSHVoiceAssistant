using DSHVoiceAssistant.Services;
using Xunit;

namespace DSHVoiceAssistant.Tests;

/// <summary>权限模式：工作区路径判定测试</summary>
public class PermissionGatingTests
{
    private const string Workspace = @"D:\Projects\DSHVoiceAssistant";

    [Theory]
    [InlineData(@"D:\Projects\DSHVoiceAssistant")]
    [InlineData(@"D:\Projects\DSHVoiceAssistant\")]
    [InlineData(@"D:\Projects\DSHVoiceAssistant\src\DSHVoiceAssistant\Program.cs")]
    [InlineData(@"D:\projects\dshvoiceassistant\README.md")] // 大小写不敏感
    public void IsWithinWorkspace_Inside_ReturnsTrue(string path)
    {
        Assert.True(CommandProcessor.IsWithinWorkspace(path, Workspace));
    }

    [Theory]
    [InlineData(@"D:\Projects\Other\file.txt")]
    [InlineData(@"D:\Projects\DSHVoiceAssistantX\file.txt")] // 前缀陷阱
    [InlineData(@"C:\Windows\System32")]
    [InlineData(@"")]
    [InlineData(null)]
    [InlineData(@"D:\Projects\DSHVoiceAssistant\..\..\Other\file.txt")] // 越界
    public void IsWithinWorkspace_Outside_ReturnsFalse(string? path)
    {
        Assert.False(CommandProcessor.IsWithinWorkspace(path!, Workspace));
    }
}
