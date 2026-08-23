namespace DSHVoiceAssistant.Models;

/// <summary>本地指令执行结果</summary>
public sealed record CommandResult(bool Success, string Message)
{
    public static CommandResult Ok(string message) => new(true, message);

    public static CommandResult Fail(string message) => new(false, message);
}
