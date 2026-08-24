namespace DSHVoiceAssistant.Models;

/// <summary>
/// DSH 返回的动作类型（与 DSH 系统提示词中约定的 action 字段一一对应）。
/// </summary>
public enum CommandAction
{
    /// <summary>打开应用</summary>
    OpenApp,

    /// <summary>打开游戏/任意已安装应用（智能查找：别名/开始菜单/Steam/Epic）</summary>
    OpenGame,

    /// <summary>网络搜索</summary>
    WebSearch,

    /// <summary>系统命令（关机/重启/锁屏/睡眠等）</summary>
    SystemCommand,

    /// <summary>文件操作</summary>
    FileOperation,

    /// <summary>纯文本回复（朗读）</summary>
    TextReply,

    /// <summary>执行自定义脚本（PowerShell）</summary>
    CustomScript,

    /// <summary>媒体控制（播放/暂停/音量等）</summary>
    ControlMedia,

    /// <summary>打开网址</summary>
    OpenUrl,

    /// <summary>闹钟/提醒/倒计时（params: minutes 或 at + message）</summary>
    Reminder,

    /// <summary>全屏截图（保存到图片目录）</summary>
    Screenshot,

    /// <summary>剪贴板操作（params.operation: get / set）</summary>
    Clipboard,

    /// <summary>未知动作</summary>
    Unknown
}

public static class CommandActionExtensions
{
    /// <summary>
    /// 将 DSH 返回的动作字符串（如 "open_app"、"OpenApp"、"open app"）映射为枚举。
    /// 归一化规则：忽略大小写，并移除下划线/连字符/空格。
    /// </summary>
    public static CommandAction FromString(string? action)
    {
        if (string.IsNullOrWhiteSpace(action)) return CommandAction.Unknown;
        var normalized = action.Replace("_", "").Replace("-", "").Replace(" ", "").Trim().ToLowerInvariant();
        return normalized switch
        {
            "openapp" => CommandAction.OpenApp,
            "opengame" => CommandAction.OpenGame,
            "websearch" => CommandAction.WebSearch,
            "systemcommand" => CommandAction.SystemCommand,
            "fileoperation" => CommandAction.FileOperation,
            "textreply" => CommandAction.TextReply,
            "customscript" => CommandAction.CustomScript,
            "controlmedia" => CommandAction.ControlMedia,
            "openurl" => CommandAction.OpenUrl,
            "reminder" or "timer" or "alarm" or "remind" => CommandAction.Reminder,
            "screenshot" or "screencapture" => CommandAction.Screenshot,
            "clipboard" => CommandAction.Clipboard,
            _ => CommandAction.Unknown
        };
    }
}
