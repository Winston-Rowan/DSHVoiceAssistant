using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace DSHVoiceAssistant.Utils;

/// <summary>
/// 应用/游戏智能查找器：解决"DSH 只知道名称、不知道安装路径"的问题。
///
/// 查找顺序：用户别名表 → 开始菜单快捷方式 → Steam 游戏库 → Epic 游戏库。
/// target 支持用 | 分隔多个候选名称（如 "战神5|God of War Ragnarök"），逐个匹配。
/// 匹配算法：归一化（去空格标点/变音符号、小写）后做相等/包含/编辑距离匹配。
/// </summary>
public static partial class AppFinder
{
    /// <summary>
    /// 按名称查找并启动应用/游戏。
    /// </summary>
    /// <returns>成功时的描述消息；未找到返回 null。</returns>
    public static string? TryLaunch(string query, IReadOnlyDictionary<string, string>? aliases)
    {
        var queries = (query ?? "")
            .Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (queries.Length == 0) return null;

        // 1. 用户别名表（最可靠，如 "战神5=steam://rungameid/2322010"）
        if (aliases != null)
        {
            foreach (var (name, value) in aliases)
            {
                if (queries.Any(q => IsNameMatchSmart(name, q)))
                {
                    var message = Launch(value);
                    if (message != null) return $"已通过别名「{name}」启动";
                    break;
                }
            }
        }

        // 2. 软件清单（AppInventory 归档：桌面/开始菜单快捷方式、注册表卸载项、Steam/Epic 等）
        try
        {
            foreach (var entry in AppInventory.GetEntries())
            {
                if (queries.Any(q => IsNameMatchSmart(entry.Name, q)))
                {
                    var message = Launch(entry.Launch);
                    if (message != null) return $"已启动「{entry.Name}」（{entry.Source}）";
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warn("查询软件清单失败: " + ex.Message);
        }

        // 3. 开始菜单/桌面快捷方式实时兜底（含子文件夹，覆盖归档未生成的情形）
        var shortcut = FindShortcut(queries);
        if (shortcut != null && Launch(shortcut) != null)
            return "已通过开始菜单/桌面快捷方式启动";

        // 4. Steam 游戏库（appmanifest 解析 appid → steam://rungameid/）
        var steam = FindSteamGame(queries);
        if (steam != null && Launch(steam.Value.LaunchUri) != null)
            return $"已启动 Steam 游戏「{steam.Value.Name}」";

        // 5. Epic 游戏库（Manifests/*.item 解析安装位置）
        var epicExe = FindEpicGame(queries);
        if (epicExe != null && Launch(epicExe) != null)
            return "已通过 Epic 游戏库启动";

        return null;
    }

    // ---------- 各查找来源 ----------

    private static string? FindShortcut(string[] queries)
    {
        // 开始菜单 Programs（两个位置）+ 桌面（用户/公共，含子文件夹，如"游戏"目录）
        var dirs = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs"),
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)
        };
        foreach (var dir in dirs)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;
            foreach (var lnk in Directory.EnumerateFiles(dir, "*.lnk", SearchOption.AllDirectories))
            {
                var name = Path.GetFileNameWithoutExtension(lnk);
                if (queries.Any(q => IsNameMatchSmart(name, q)))
                    return lnk;
            }
        }
        return null;
    }

    private static (string LaunchUri, string Name)? FindSteamGame(string[] queries)
    {
        var steamPath = Registry.GetValue(
            @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string
            ?? Registry.GetValue(
            @"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam", "InstallPath", null) as string;
        if (string.IsNullOrWhiteSpace(steamPath)) return null;

        var appsDir = Path.Combine(steamPath, "steamapps");
        if (!Directory.Exists(appsDir)) return null;

        foreach (var acf in Directory.EnumerateFiles(appsDir, "appmanifest_*.acf"))
        {
            try
            {
                var fields = ParseAcf(File.ReadAllText(acf));
                var name = fields.GetValueOrDefault("name", "");
                var appid = fields.GetValueOrDefault("appid", "");
                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(appid)) continue;
                if (queries.Any(q => IsNameMatchSmart(name, q)))
                    return ($"steam://rungameid/{appid}", name);
            }
            catch (Exception ex)
            {
                Logger.Warn("解析 Steam 清单失败: " + acf + " - " + ex.Message);
            }
        }
        return null;
    }

    private static string? FindEpicGame(string[] queries)
    {
        var manifestsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Epic", "EpicGamesLauncher", "Data", "Manifests");
        if (!Directory.Exists(manifestsDir)) return null;

        foreach (var item in Directory.EnumerateFiles(manifestsDir, "*.item"))
        {
            try
            {
                var info = ParseEpicItem(File.ReadAllText(item));
                if (info == null || string.IsNullOrEmpty(info.Value.LaunchExecutable)) continue;
                if (!queries.Any(q => IsNameMatchSmart(info.Value.DisplayName, q))) continue;
                return Path.Combine(info.Value.InstallLocation, info.Value.LaunchExecutable);
            }
            catch (Exception ex)
            {
                Logger.Warn("解析 Epic 清单失败: " + item + " - " + ex.Message);
            }
        }
        return null;
    }

    private static string? Launch(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            Logger.Info("AppFinder 启动: " + target);
            return target;
        }
        catch (Exception ex)
        {
            Logger.Warn("AppFinder 启动失败: " + target + " - " + ex.Message);
            return null;
        }
    }

    // ---------- 纯函数（供单元测试） ----------

    /// <summary>归一化：小写、去空白/标点/符号/变音符号。如 "God of War Ragnarök" → "godofwarragnarok"</summary>
    public static string Normalize(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var folded = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(folded.Length);
        foreach (var ch in folded)
        {
            if (char.IsWhiteSpace(ch) || char.IsPunctuation(ch) || char.IsSymbol(ch)) continue;
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(char.ToLowerInvariant(ch));
        }
        return sb.ToString();
    }

    /// <summary>名称匹配：归一化后相等、互相包含或编辑距离足够近。</summary>
    public static bool IsNameMatch(string candidate, string query)
    {
        var c = Normalize(candidate);
        var q = Normalize(query);
        if (c.Length == 0 || q.Length == 0) return false;
        if (c == q) return true;
        if (c.Contains(q, StringComparison.Ordinal) || q.Contains(c, StringComparison.Ordinal)) return true;
        return Levenshtein(c, q) <= Math.Max(1, q.Length / 3);
    }

    /// <summary>
    /// 智能名称匹配（open_game/清单查找使用）：
    /// ① 常规匹配 → ② 去除候选名中的平台/版本噪音后匹配 → ③ 前缀+短序号启发式
    /// （如查询"战神5"可匹配"战神：诸神黄昏_WWW.XDGAME.COM"：前缀"战神"+序号"5"）。
    /// </summary>
    public static bool IsNameMatchSmart(string candidate, string query)
    {
        if (IsNameMatch(candidate, query)) return true;

        var clean = CleanNameNoise(candidate);
        if (!clean.Equals(candidate, StringComparison.Ordinal) && IsNameMatch(clean, query)) return true;

        var c = Normalize(clean);
        var q = Normalize(query);
        if (c.Length == 0 || q.Length == 0) return false;

        for (var len = Math.Min(4, q.Length); len >= 2; len--)
        {
            if (!c.StartsWith(q[..len], StringComparison.Ordinal)) continue;
            var rest = q[len..];
            if (rest.Length <= 2 && rest.All(char.IsDigit)) return true;
        }
        return false;
    }

    /// <summary>去除名称中的常见平台/版本噪音后缀："战神：诸神黄昏_WWW.XDGAME.COM" → "战神：诸神黄昏"</summary>
    public static string CleanNameNoise(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        var lower = name.ToLowerInvariant();
        foreach (var marker in NoiseMarkers)
        {
            var idx = lower.IndexOf(marker, StringComparison.Ordinal);
            if (idx > 0)
            {
                name = name[..idx];
                lower = name.ToLowerInvariant();
            }
        }
        return name.Trim().TrimEnd('_', '-', ' ', '，', ',');
    }

    private static readonly string[] NoiseMarkers =
    {
        "_www", " www", "www.", ".com", ".cn", "xdgame", "官方", "官网",
        "破解版", "免安装", "绿色版", "_3dm", "3dm", "游侠", "游民", "中文版", "简体中文"
    };

    /// <summary>编辑距离（容忍拼写差异/错别字）</summary>
    public static int Levenshtein(string a, string b)
    {
        var dp = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) dp[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) dp[0, j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            for (var j = 1; j <= b.Length; j++)
            {
                dp[i, j] = Math.Min(
                    Math.Min(dp[i - 1, j] + 1, dp[i, j - 1] + 1),
                    dp[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            }
        }
        return dp[a.Length, b.Length];
    }

    /// <summary>解析 Steam appmanifest_*.acf 文件内容为键值表。</summary>
    public static Dictionary<string, string> ParseAcf(string content)
    {
        var result = new Dictionary<string, string>();
        foreach (Match m in AcfPairRegex().Matches(content))
            result[m.Groups[1].Value] = m.Groups[2].Value;
        return result;
    }

    /// <summary>解析 Epic .item 清单 JSON，提取显示名/启动程序/安装位置。解析失败返回 null。</summary>
    public static (string DisplayName, string LaunchExecutable, string InstallLocation)? ParseEpicItem(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("DisplayName", out var name)) return null;
            if (!root.TryGetProperty("LaunchExecutable", out var exe)) return null;
            if (!root.TryGetProperty("InstallLocation", out var loc)) return null;
            return (name.GetString() ?? "", exe.GetString() ?? "", loc.GetString() ?? "");
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>解析设置窗口的别名行："名称=路径或命令"。</summary>
    public static bool TryParseAliasLine(string line, out string name, out string value)
    {
        name = "";
        value = "";
        var idx = line.IndexOf('=');
        if (idx <= 0) return false;
        name = line[..idx].Trim();
        value = line[(idx + 1)..].Trim();
        return name.Length > 0 && value.Length > 0;
    }

    [GeneratedRegex(@"""(\w+)""\s+""((?:[^""\\]|\\.)*)""")]
    private static partial Regex AcfPairRegex();
}
