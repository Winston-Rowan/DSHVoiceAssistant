using System.Text.Json.Serialization;

namespace DivaVoiceAssistant.Models;

/// <summary>DSH 对话消息</summary>
public sealed class DSHChatMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; } = "";

    [JsonPropertyName("content")]
    public string Content { get; set; } = "";
}

/// <summary>
/// DSH 指令请求（OpenAI 兼容的 chat/completions 请求体）。
/// </summary>
public sealed class DSHChatRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; } = "";

    [JsonPropertyName("messages")]
    public List<DSHChatMessage> Messages { get; set; } = [];

    [JsonPropertyName("temperature")]
    public double Temperature { get; set; } = 0.3;

    [JsonPropertyName("max_tokens")]
    public int MaxTokens { get; set; } = 500;
}

/// <summary>DSH 指令响应（OpenAI 兼容的 chat/completions 响应体，仅取需要的字段）</summary>
public sealed class DSHChatResponse
{
    [JsonPropertyName("choices")]
    public List<DSHChoice>? Choices { get; set; }
}

public sealed class DSHChoice
{
    [JsonPropertyName("message")]
    public DSHChatMessage? Message { get; set; }
}
