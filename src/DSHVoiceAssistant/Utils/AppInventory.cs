using System.IO;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace DSHVoiceAssistant.Utils;

/// <summary>
/// 本地软件归档引擎：扫描 Windows 原生与第三方程序的安装位置与启动快捷方式，
/// 生成软件清单（JSON 供 AppFinder 快速查找 + Markdown 供用户查看核对）。
///
/// 扫描来源：
///   1. 桌面快捷方式（用户桌面 + 公共桌面，含子文件夹，如"游戏"文件夹）
///   2. 开始菜单快捷方式（ProgramData + AppData，递归）
///   3. 注册表卸载项（HKLM/HKCU × 32/64 位）→ DisplayName + DisplayIcon / InstallLocation
///   4. App Paths 注册表项 → 已注册的可执行文件
///   5. Steam 游戏库（appmanifest_*.acf → steam://rungameid/）
///   6. Epic 游戏库（Manifests/*.item → 安装位置 + 启动程序）
/// </summary>
public static class AppInventory
{
    /// <summary>清单条目：名称、启动目标（exe 路径 / .lnk 路径 / steam 链接）、来源</summary>
    public sealed record Entry(string Name, string Launch, string Source);

    private static readonly object Gate = new();
    private static List<Entry>? _cache;
    private static DateTime _lastRefresh;

    /// <summary>程序查找用 JSON 清单路径</summary>
    public static string InventoryPath => Path.Combine(AppContext.BaseDirectory, "Config", "software-inventory.json");

    /// <summary>用户可读的 Markdown 清单路径</summary>
    public static string MarkdownPath => Path.Combine(AppContext.BaseDirectory, "Config", "软件清单.md");

    /// <summary>获取软件清单：优先缓存（24 小时内），过期或缺失则全量扫描。</summary>
    public static List<Entry> GetEntries()
    {
        lock (Gate)
        {
            if (_cache != null && DateTime.UtcNow - _lastRefresh < TimeSpan.FromHours(24))
                return _cache;
        }
        return Refresh();
    }

    /// <summary>全量扫描并归档（JSON + MD），返回条目列表。单次约数百毫秒。</summary>
    public static List<Entry> Refresh()
    {
        var entries = new List<Entry>();

        try { entries.AddRange(ScanShortcuts("桌面")); } catch (Exception ex) { Logger.Warn("扫描桌面快捷方式失败: " + ex.Message); }
        try { entries.AddRange(ScanShortcuts("开始菜单")); } catch (Exception ex) { Logger.Warn("扫描开始菜单失败: " + ex.Message); }
        try { entries.AddRange(ScanUninstallRegistry()); } catch (Exception ex) { Logger.Warn("扫描注册表卸载项失败: " + ex.Message); }
        try { entries.AddRange(ScanAppPaths()); } catch (Exception ex) { Logger.Warn("扫描 App Paths 失败: " + ex.Message); }
        try { entries.AddRange(ScanSteam()); } catch (Exception ex) { Logger.Warn("扫描 Steam 失败: " + ex.Message); }
        try { entries.AddRange(ScanEpic()); } catch (Exception ex) { Logger.Warn("扫描 Epic 失败: " + ex.Message); }

        // 去重（名称+启动目标相同）
        var dedup = entries
            .GroupBy(e => e.Name + "|" + e.Launch)
            .Select(g => g.First())
            .OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        lock (Gate)
        {
            _cache = dedup;
            _lastRefresh = DateTime.UtcNow;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(InventoryPath)!);
            var json = JsonSerializer.Serialize(
                new { generatedAt = DateTime.Now, count = dedup.Count, entries = dedup },
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(InventoryPath, json);
            File.WriteAllText(MarkdownPath, BuildMarkdown(dedup));
            Logger.Info($"软件归档完成: {dedup.Count} 个条目 → {InventoryPath}");
        }
        catch (Exception ex)
        {
            Logger.Warn("写入软件清单失败: " + ex.Message);
        }

        return dedup;
    }

    // ---------- 扫描来源 ----------

    private static List<Entry> ScanShortcuts(string sourceLabel)
    {
        var result = new List<Entry>();
        var dirs = sourceLabel == "桌面"
            ? new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)
            }
            : new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs")
            };

        foreach (var dir in dirs)
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var lnk in Directory.EnumerateFiles(dir, "*.lnk", SearchOption.AllDirectories))
            {
                var name = Path.GetFileNameWithoutExtension(lnk);
                if (string.IsNullOrWhiteSpace(name)) continue;
                // 过滤卸载程序/安装器快捷方式
                if (name.StartsWith("unins", StringComparison.OrdinalIgnoreCase)) continue;
                if (name.StartsWith("uninstall", StringComparison.OrdinalIgnoreCase)) continue;
                if (name.Contains("卸载", StringComparison.Ordinal)) continue;
                result.Add(new Entry(name, lnk, sourceLabel + "快捷方式"));
            }
        }
        return result;
    }

    private static List<Entry> ScanUninstallRegistry()
    {
        var result = new List<Entry>();
        const string uninstallPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
        var roots = new (RegistryKey Hive, string Sub)[]
        {
            (Registry.LocalMachine, uninstallPath),
            (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
            (Registry.CurrentUser, uninstallPath),
            (Registry.CurrentUser, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall")
        };

        foreach (var (hive, sub) in roots)
        {
            using var key = hive.OpenSubKey(sub);
            if (key == null) continue;
            foreach (var subName in key.GetSubKeyNames())
            {
                using var app = key.OpenSubKey(subName);
                if (app == null) continue;
                try
                {
                    var name = app.GetValue("DisplayName") as string;
                    if (string.IsNullOrWhiteSpace(name)) continue;

                    var icon = CleanIconPath(app.GetValue("DisplayIcon") as string);
                    var location = app.GetValue("InstallLocation") as string;

                    string? launch = null;
                    if (!string.IsNullOrWhiteSpace(icon) && File.Exists(icon))
                        launch = icon;
                    else if (!string.IsNullOrWhiteSpace(location) && Directory.Exists(location))
                        launch = FindMainExe(location, name);

                    if (string.IsNullOrWhiteSpace(launch)) continue;
                    result.Add(new Entry(name.Trim(), launch, "注册表卸载项"));
                }
                catch
                {
                    // 单个注册表项异常跳过
                }
            }
        }
        return result;
    }

    private static List<Entry> ScanAppPaths()
    {
        var result = new List<Entry>();
        const string appPaths = @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths";
        var roots = new (RegistryKey Hive, string Sub)[]
        {
            (Registry.LocalMachine, appPaths),
            (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths"),
            (Registry.CurrentUser, appPaths)
        };

        foreach (var (hive, sub) in roots)
        {
            using var key = hive.OpenSubKey(sub);
            if (key == null) continue;
            foreach (var exeName in key.GetSubKeyNames())
            {
                using var app = key.OpenSubKey(exeName);
                var target = app?.GetValue("") as string;
                if (string.IsNullOrWhiteSpace(target)) continue;
                if (!target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
                result.Add(new Entry(Path.GetFileNameWithoutExtension(exeName), target, "App Paths"));
            }
        }
        return result;
    }

    private static List<Entry> ScanSteam()
    {
        var result = new List<Entry>();
        var steamPath = Registry.GetValue(
            @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string
            ?? Registry.GetValue(
            @"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam", "InstallPath", null) as string;
        if (string.IsNullOrWhiteSpace(steamPath)) return result;

        var appsDir = Path.Combine(steamPath, "steamapps");
        if (!Directory.Exists(appsDir)) return result;

        foreach (var acf in Directory.EnumerateFiles(appsDir, "appmanifest_*.acf"))
        {
            try
            {
                var fields = AppFinder.ParseAcf(File.ReadAllText(acf));
                var name = fields.GetValueOrDefault("name", "");
                var appid = fields.GetValueOrDefault("appid", "");
                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(appid)) continue;
                result.Add(new Entry(name, $"steam://rungameid/{appid}", "Steam 游戏库"));
            }
            catch
            {
                // 单个清单解析失败跳过
            }
        }
        return result;
    }

    private static List<Entry> ScanEpic()
    {
        var result = new List<Entry>();
        var manifestsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Epic", "EpicGamesLauncher", "Data", "Manifests");
        if (!Directory.Exists(manifestsDir)) return result;

        foreach (var item in Directory.EnumerateFiles(manifestsDir, "*.item"))
        {
            try
            {
                var info = AppFinder.ParseEpicItem(File.ReadAllText(item));
                if (info == null || string.IsNullOrEmpty(info.Value.LaunchExecutable)) continue;
                var exe = Path.Combine(info.Value.InstallLocation, info.Value.LaunchExecutable);
                result.Add(new Entry(info.Value.DisplayName, exe, "Epic 游戏库"));
            }
            catch
            {
                // 单个清单解析失败跳过
            }
        }
        return result;
    }

    // ---------- 纯函数（供单元测试） ----------

    /// <summary>清理 DisplayIcon 值："C:\a\b.exe,0" / 带引号 → 纯 exe 路径；无效返回 null。</summary>
    public static string? CleanIconPath(string? icon)
    {
        if (string.IsNullOrWhiteSpace(icon)) return null;
        var t = icon.Trim().Trim('"');
        var comma = t.IndexOf(',');
        if (comma > 0) t = t[..comma];
        t = t.Trim();
        return t.Length == 0 ? null : t;
    }

    /// <summary>
    /// 在安装目录中启发式寻找主程序 exe：
    /// ① 与目录同名 → ② 与 DisplayName 名称匹配 → ③ 排除安装器/运行库后体积最大。
    /// </summary>
    public static string? FindMainExe(string installDir, string displayName)
    {
        try
        {
            var exes = Directory.EnumerateFiles(installDir, "*.exe", SearchOption.TopDirectoryOnly).ToList();
            if (exes.Count == 0) return null;

            var dirName = Path.GetFileName(installDir.TrimEnd('\\', '/'));

            // ① 目录同名
            var hit = exes.FirstOrDefault(e =>
                Path.GetFileNameWithoutExtension(e).Equals(dirName, StringComparison.OrdinalIgnoreCase));

            // ② DisplayName 匹配
            if (hit == null)
            {
                var norm = AppFinder.Normalize(displayName);
                hit = exes.FirstOrDefault(e =>
                {
                    var exeNorm = AppFinder.Normalize(Path.GetFileNameWithoutExtension(e));
                    return exeNorm.Length > 0 && (norm.Contains(exeNorm, StringComparison.Ordinal) || exeNorm.Contains(norm, StringComparison.Ordinal));
                });
            }

            // ③ 排除安装器/运行库后取体积最大
            if (hit == null)
            {
                var candidates = exes.Where(e =>
                {
                    var n = Path.GetFileNameWithoutExtension(e).ToLowerInvariant();
                    return !n.StartsWith("unins")
                        && !n.Contains("setup") && !n.Contains("install")
                        && !n.Contains("redist") && !n.Contains("dotnet")
                        && !n.Contains("vcredist") && !n.Contains("vcruntime");
                }).ToList();
                hit = candidates.OrderByDescending(e => new FileInfo(e).Length).FirstOrDefault();
            }
            return hit;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>生成用户可读的 Markdown 清单。</summary>
    public static string BuildMarkdown(List<Entry> entries)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# DSH 软件清单");
        sb.AppendLine();
        sb.AppendLine($"> 自动生成于 {DateTime.Now:yyyy-MM-dd HH:mm:ss}，共 {entries.Count} 个条目。");
        sb.AppendLine();
        sb.AppendLine("| 名称 | 启动方式 | 来源 |");
        sb.AppendLine("|------|----------|------|");
        foreach (var e in entries)
            sb.AppendLine($"| {e.Name.Replace("|", "\\|")} | `{e.Launch.Replace("|", "\\|")}` | {e.Source} |");
        return sb.ToString();
    }
}
