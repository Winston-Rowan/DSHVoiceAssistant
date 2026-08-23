using DivaVoiceAssistant.Models;

namespace DivaVoiceAssistant.Services;

/// <summary>
/// DSH 指令执行服务接口（⭐核心）：
/// 将用户的自然语言指令发送给大模型（携带 DSH 系统角色提示词），
/// 由大模型完成全部指令理解与决策，返回结构化可执行指令。
/// </summary>
public interface IDSHCommandService
{
    /// <summary>执行指令：自然语言 → DSHResponse 结构化指令。</summary>
    Task<DSHResponse> ExecuteAsync(string userCommand, CancellationToken cancellationToken = default);
}
