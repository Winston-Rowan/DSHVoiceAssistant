using DSHVoiceAssistant.Utils;
using Xunit;

namespace DSHVoiceAssistant.Tests;

/// <summary>AppFinder（应用/游戏智能查找）纯函数测试</summary>
public class AppFinderTests
{
    [Theory]
    [InlineData("God of War Ragnarök", "God of War Ragnarök", true)]
    [InlineData("God of War Ragnarök", "god of war ragnarok", true)]   // 大小写
    [InlineData("God of War Ragnarök", "godofwarragnarok", true)]      // 变音符号+空格归一化
    [InlineData("战神5", "战神5", true)]
    [InlineData("战神5", "战神", true)]                                 // 包含匹配
    [InlineData("God of War", "战神5", false)]                          // 中英文无法互配（靠 | 多候选/别名）
    [InlineData("WeChat", "微信", false)]
    [InlineData("steam", "steam", true)]
    [InlineData("", "战神5", false)]
    public void IsNameMatch_Works(string candidate, string query, bool expected)
    {
        Assert.Equal(expected, AppFinder.IsNameMatch(candidate, query));
    }

    [Theory]
    [InlineData("God of War Ragnarök", "godofwarragnarok")]
    [InlineData("战神5，打开", "战神5打开")]
    public void Normalize_StripsSpacesPunctAndDiacritics(string input, string expected)
    {
        Assert.Equal(expected, AppFinder.Normalize(input));
    }

    [Fact]
    public void Levenshtein_Basic()
    {
        Assert.Equal(3, AppFinder.Levenshtein("kitten", "sitting"));
        Assert.Equal(0, AppFinder.Levenshtein("same", "same"));
    }

    [Theory]
    // 关键场景：XDGAME 平台版战神5（桌面"游戏"文件夹快捷方式名）
    [InlineData("战神：诸神黄昏_WWW.XDGAME.COM", "战神5", true)]
    [InlineData("战神：诸神黄昏", "战神5", true)]
    [InlineData("战神：诸神黄昏_WWW.XDGAME.COM", "战神", true)]
    [InlineData("God of War Ragnarök", "god of war", true)]
    [InlineData("WeChat", "微信", false)] // 跨语言仍无法匹配，靠 DSH 多候选/别名
    [InlineData("微信", "微信", true)]
    public void IsNameMatchSmart_Works(string candidate, string query, bool expected)
    {
        Assert.Equal(expected, AppFinder.IsNameMatchSmart(candidate, query));
    }

    [Theory]
    [InlineData("战神：诸神黄昏_WWW.XDGAME.COM", "战神：诸神黄昏")]
    [InlineData("微信官网", "微信")]
    [InlineData("某软件 官方", "某软件")]
    [InlineData("干净名称", "干净名称")]
    public void CleanNameNoise_StripsPlatformMarkers(string input, string expected)
    {
        Assert.Equal(expected, AppFinder.CleanNameNoise(input));
    }

    [Fact]
    public void ParseAcf_ExtractsAppIdAndName()
    {
        const string acf = """
            "AppState"
            {
            	"appid"		"2322010"
            	"name"		"God of War Ragnarök"
            	"installdir"		"God of War Ragnarök"
            }
            """;
        var fields = AppFinder.ParseAcf(acf);
        Assert.Equal("2322010", fields["appid"]);
        Assert.Equal("God of War Ragnarök", fields["name"]);
    }

    [Fact]
    public void ParseEpicItem_ExtractsLaunchInfo()
    {
        const string json = """
            {"DisplayName":"God of War Ragnarök","LaunchExecutable":"GoW.exe","InstallLocation":"E:\\Games\\GodOfWar"}
            """;
        var info = AppFinder.ParseEpicItem(json);
        Assert.NotNull(info);
        Assert.Equal("God of War Ragnarök", info.Value.DisplayName);
        Assert.Equal("GoW.exe", info.Value.LaunchExecutable);
        Assert.Equal(@"E:\Games\GodOfWar", info.Value.InstallLocation);
    }

    [Fact]
    public void ParseEpicItem_MissingFields_ReturnsNull()
    {
        Assert.Null(AppFinder.ParseEpicItem("{\"DisplayName\":\"x\"}"));
        Assert.Null(AppFinder.ParseEpicItem("not json"));
    }

    [Theory]
    [InlineData("战神5=steam://rungameid/2322010", "战神5", "steam://rungameid/2322010", true)]
    [InlineData("微信 = C:\\Program Files\\WeChat\\WeChat.exe", "微信", "C:\\Program Files\\WeChat\\WeChat.exe", true)]
    [InlineData("没有等号", "", "", false)]
    [InlineData("=空名字", "", "", false)]
    [InlineData("", "", "", false)]
    public void TryParseAliasLine_Works(string line, string expectedName, string expectedValue, bool expected)
    {
        Assert.Equal(expected, AppFinder.TryParseAliasLine(line, out var name, out var value));
        Assert.Equal(expectedName, name);
        Assert.Equal(expectedValue, value);
    }
}
