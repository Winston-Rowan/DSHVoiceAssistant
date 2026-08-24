using System.Diagnostics;
using System.IO;
using DSHVoiceAssistant.Models;
using DSHVoiceAssistant.Utils;

namespace DSHVoiceAssistant.Services;

/// <summary>
/// 本地指令执行器：负责将 DSH 返回的结构化指令落地为 Windows 真实操作。
/// 支持：打开应用/网址、网络搜索、系统命令、文件操作、媒体控制、PowerShell 脚本。
/// </summary>
public sealed class CommandProcessor : ICommandProcessor
{
    private readonly DSHConfig _config;

    public CommandProcessor(DSHConfig config) => _config = config;

    public async Task<CommandResult> ExecuteAsync(DSHResponse command, CancellationToken cancellationToken = default)
    {
        if (command == null) return CommandResult.Fail("指令为空");

        try
        {
            switch (CommandActionExtensions.FromString(command.Action))
            {
                case CommandAction.OpenApp:
                    return OpenApp(command.Target);

                case CommandAction.OpenGame:
                    return OpenGame(command.Target);

                case CommandAction.OpenUrl:
                    return OpenUrl(command.Target);

                case CommandAction.WebSearch:
                    return OpenSearch(command.Target);

                case CommandAction.SystemCommand:
                    return SystemAction(command.Target);

                case CommandAction.ControlMedia:
                    return MediaKeyHelper.Send(command.Target)
                        ? CommandResult.Ok($"媒体控制：{command.Target}")
                        : CommandResult.Fail($"不支持的媒体操作：{command.Target}");

                case CommandAction.FileOperation:
                    return FileAction(command);

                case CommandAction.CustomScript:
                    // 权限拦截：只读模式禁止一切命令/脚本执行
                    if (IsReadonly)
                        return CommandResult.Fail("只读模式下禁止执行命令/脚本，请切换权限模式");
                    return await RunScriptAsync(command.Target, cancellationToken);

                case CommandAction.Screenshot:
                    return ScreenshotAction();

                case CommandAction.Clipboard:
                    return ClipboardAction(command);

                case CommandAction.TextReply:
                    return CommandResult.Ok(string.IsNullOrWhiteSpace(command.Response) ? command.Target : command.Response);

                default:
                    return CommandResult.Fail($"未知动作类型：{command.Action}");
            }
        }
        catch (Exception ex)
        {
            Logger.Error("指令执行异常: " + ex);
            return CommandResult.Fail("执行失败：" + ex.Message);
        }
    }

    // ---------- 具体动作 ----------

    /// <summary>全屏截图：保存到图片目录，返回文件路径</summary>
    private static CommandResult ScreenshotAction()
    {
        try
        {
            var bounds = System.Windows.Forms.Screen.PrimaryScreen?.Bounds
                         ?? new System.Drawing.Rectangle(0, 0, 1920, 1080);
            using var bitmap = new System.Drawing.Bitmap(bounds.Width, bounds.Height);
            using (var g = System.Drawing.Graphics.FromImage(bitmap))
            {
                g.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size);
            }

            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "DSH截图");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"截图_{DateTime.Now:yyyyMMdd_HHmmss}.png");
            bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            Logger.Info("screenshot: " + path);
            return CommandResult.Ok("已保存截图：" + path);
        }
        catch (Exception ex)
        {
            Logger.Error("截图失败: " + ex.Message);
            return CommandResult.Fail("截图失败：" + ex.Message);
        }
    }

    /// <summary>剪贴板操作：get=读出文本（截断播报）；set=写入 target 文本（需 STA 线程）</summary>
    private static CommandResult ClipboardAction(DSHResponse command)
    {
        var operation = command.Params != null && command.Params.TryGetValue("operation", out var op)
            ? op.ToLowerInvariant()
            : "get";
        try
        {
            if (operation == "set")
            {
                var text = command.Target ?? "";
                if (string.IsNullOrEmpty(text)) return CommandResult.Fail("未指定要写入剪贴板的文本");
                RunSta(() => System.Windows.Clipboard.SetText(text));
                Logger.Info("clipboard: set " + JsonUtils.Truncate(text, 60));
                return CommandResult.Ok("已复制到剪贴板");
            }

            string? content = null;
            RunSta(() => content = System.Windows.Clipboard.GetText());
            if (string.IsNullOrWhiteSpace(content)) return CommandResult.Fail("剪贴板中没有文本");
            var snippet = content.Length > 80 ? content[..80] + "…" : content;
            Logger.Info("clipboard: get " + JsonUtils.Truncate(snippet, 80));
            return CommandResult.Ok("剪贴板内容：" + snippet);
        }
        catch (Exception ex)
        {
            Logger.Error("剪贴板操作失败: " + ex.Message);
            return CommandResult.Fail("剪贴板操作失败：" + ex.Message);
        }
    }

    /// <summary>在独立 STA 线程上执行（剪贴板等 COM 组件要求）</summary>
    private static void RunSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error != null) throw error;
    }

    private CommandResult OpenApp(string? target)
    {
        if (string.IsNullOrWhiteSpace(target)) return CommandResult.Fail("未指定要打开的程序");
        try
        {
            Process.Start(new ProcessStartInfo(target.Trim()) { UseShellExecute = true });
            Logger.Info("open_app: " + target.Trim());
            return CommandResult.Ok("已启动 " + target.Trim());
        }
        catch (Exception ex)
        {
            // 直接启动失败（程序不在 PATH、或 DSH 猜了不存在的文件名）→ 智能查找兜底
            Logger.Warn("直接启动失败，尝试智能查找: " + target + " - " + ex.Message);
            return OpenGame(target);
        }
    }

    /// <summary>打开游戏/任意已安装应用：从别名表、开始菜单、Steam、Epic 智能查找</summary>
    private CommandResult OpenGame(string? target)
    {
        if (string.IsNullOrWhiteSpace(target)) return CommandResult.Fail("未指定要启动的游戏");

        var message = AppFinder.TryLaunch(target, _config.AppAliases);
        if (message != null)
        {
            Logger.Info("open_game: " + target + " → " + message);
            return CommandResult.Ok(message);
        }
        return CommandResult.Fail($"没有找到「{target.Split('|')[0]}」，请确认已安装，或在设置中添加应用别名");
    }

    private static CommandResult OpenUrl(string? target)
    {
        if (string.IsNullOrWhiteSpace(target)) return CommandResult.Fail("未指定网址");
        var url = target.Trim();
        if (!url.Contains("://")) url = "https://" + url;
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        Logger.Info("open_url: " + url);
        return CommandResult.Ok("已打开网页");
    }

    private CommandResult OpenSearch(string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return CommandResult.Fail("没有搜索关键词");
        var url = BuildSearchUrl(query, _config.SearchEngine);
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        Logger.Info("web_search: " + url);
        return CommandResult.Ok("正在打开搜索结果");
    }

    private CommandResult SystemAction(string? target)
    {
        var t = (target ?? "").Trim().ToLowerInvariant();

        // 权限拦截：只读模式禁止关机/重启/休眠/注销等系统更改操作（锁屏/睡眠保留）
        if (IsReadonly && t is "shutdown" or "restart" or "reboot" or "hibernate" or "logoff"
            or "关机" or "重启" or "休眠" or "注销")
            return CommandResult.Fail("只读模式下禁止关机/重启等系统操作，请切换权限模式");

        if (t is "lock" or "锁屏")
        {
            Process.Start(new ProcessStartInfo("rundll32.exe", "user32.dll,LockWorkStation") { UseShellExecute = false, CreateNoWindow = true });
            return CommandResult.Ok("已锁屏");
        }
        if (t is "sleep" or "睡眠")
        {
            Process.Start(new ProcessStartInfo("rundll32.exe", "powrprof.dll,SetSuspendState 0,1,0") { UseShellExecute = false, CreateNoWindow = true });
            return CommandResult.Ok("正在进入睡眠");
        }

        var args = BuildSystemCommandArgs(t);
        if (string.IsNullOrEmpty(args)) return CommandResult.Fail($"不支持的系统操作：{target}");

        Process.Start(new ProcessStartInfo("shutdown.exe", args) { UseShellExecute = false, CreateNoWindow = true });
        Logger.Warn("system_command: " + t);
        return CommandResult.Ok($"系统操作已执行：{t}");
    }

    private CommandResult FileAction(DSHResponse command)
    {
        var operation = command.Params != null && command.Params.TryGetValue("operation", out var op)
            ? op.ToLowerInvariant()
            : "open";
        var path = command.Target?.Trim() ?? "";
        if (string.IsNullOrEmpty(path)) return CommandResult.Fail("未指定文件路径");

        // 权限拦截：只读模式禁止删除；工作区模式仅限工作区内（与提示词权限说明一致）
        if (IsReadonly && operation == "delete")
            return CommandResult.Fail("只读模式下禁止删除文件，请切换权限模式");
        if (IsWorkspace && !IsWithinWorkspace(path, _config.GitProjectPath))
            return CommandResult.Fail($"工作区受限模式下只能操作工作区（{_config.GitProjectPath}）内的文件");

        switch (operation)
        {
            case "open":
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                return CommandResult.Ok("已打开 " + Path.GetFileName(path.TrimEnd('\\', '/')));

            case "reveal":
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{Path.GetFullPath(path)}\"") { UseShellExecute = false, CreateNoWindow = true });
                return CommandResult.Ok("已在资源管理器中定位");

            case "delete":
                // 安全闸：删除操作必须由 DSH 显式携带 confirm=true，防止误删
                var confirmed = command.Params != null && command.Params.TryGetValue("confirm", out var c) && c == "true";
                if (!confirmed) return CommandResult.Fail("删除操作需要确认参数（风险操作已阻止）");

                if (File.Exists(path)) File.Delete(path);
                else if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
                else return CommandResult.Fail("文件不存在：" + path);

                Logger.Warn("已删除: " + path);
                return CommandResult.Ok("已删除 " + Path.GetFileName(path.TrimEnd('\\', '/')));

            default:
                return CommandResult.Fail($"不支持的文件操作：{operation}");
        }
    }

    private static async Task<CommandResult> RunScriptAsync(string? script, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(script)) return CommandResult.Fail("脚本内容为空");
        Logger.Warn("执行自定义脚本: " + script);

        var psi = new ProcessStartInfo(
            "powershell.exe",
            $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{script.Replace("\"", "\\\"")}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var process = Process.Start(psi);
        if (process == null) return CommandResult.Fail("无法启动 PowerShell");

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(60));

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* 忽略 */ }
            return CommandResult.Fail("脚本执行超时（60秒）");
        }

        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = await process.StandardError.ReadToEndAsync(cancellationToken);
        var message = (output + error).Trim();
        return CommandResult.Ok(string.IsNullOrEmpty(message) ? "脚本已执行" : JsonUtils.Truncate(message, 200));
    }

    // ---------- 纯函数（供单元测试） ----------

    /// <summary>当前是否为只读模式（执行器硬性拦截用）</summary>
    private bool IsReadonly =>
        _config.PermissionMode?.Trim().Equals("readonly", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>当前是否为工作区受限模式（执行器硬性拦截用）</summary>
    private bool IsWorkspace =>
        _config.PermissionMode?.Trim().Equals("workspace", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>路径是否位于工作区内（含工作区本身；大小写不敏感，防前缀误判）</summary>
    public static bool IsWithinWorkspace(string path, string? workspace)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(workspace)) return false;
        try
        {
            var full = Path.GetFullPath(path).TrimEnd('\\', '/');
            var root = Path.GetFullPath(workspace).TrimEnd('\\', '/');
            return full.Equals(root, StringComparison.OrdinalIgnoreCase)
                || full.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>构造搜索引擎搜索 URL（baidu / bing / google）</summary>
    public static string BuildSearchUrl(string query, string engine)
    {
        var encoded = Uri.EscapeDataString(query);
        return engine.Trim().ToLowerInvariant() switch
        {
            "bing" => "https://www.bing.com/search?q=" + encoded,
            "google" => "https://www.google.com/search?q=" + encoded,
            _ => "https://www.baidu.com/s?wd=" + encoded
        };
    }

    /// <summary>构造 shutdown.exe 参数（shutdown/restart/hibernate/logoff；锁屏与睡眠走 rundll32，不在此列）</summary>
    public static string BuildSystemCommandArgs(string target)
    {
        return target.Trim().ToLowerInvariant() switch
        {
            "shutdown" or "关机" => "/s /t 5",
            "restart" or "reboot" or "重启" => "/r /t 5",
            "hibernate" or "休眠" => "/h",
            "logoff" or "注销" => "/l",
            _ => ""
        };
    }
}
