using DSHVoiceAssistant.Models;

namespace DSHVoiceAssistant.Services;

/// <summary>本地指令执行器接口：把 DSH 返回的结构化指令落地为真实操作。</summary>
public interface ICommandProcessor
{
    /// <summary>执行一条结构化指令，返回执行结果。</summary>
    Task<CommandResult> ExecuteAsync(DSHResponse command, CancellationToken cancellationToken = default);
}
