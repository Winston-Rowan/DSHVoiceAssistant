using System.Text.Json.Serialization;

namespace DSHVoiceAssistant.Models;

/// <summary>DSH 返回的结构化指令（供本地执行引擎使用）</summary>
public sealed class DSHResponse
{
    /// <summary>动作类型：open_app / web_search / system_command / file_operation / text_reply / custom_script / control_media / open_url</summary>
    public string Action { get; set; } = "text_reply";

    /// <summary>动作目标对象</summary>
    public string Target { get; set; } = "";

    /// <summary>附加参数（键值对）</summary>
    public Dictionary<string, string>? Params { get; set; }

    /// <summary>给用户的语音回复内容（TTS 朗读）</summary>
    public string Response { get; set; } = "";

    /// <summary>DSH 返回的原始内容（调试用）</summary>
    public string RawContent { get; set; } = "";

    /// <summary>解析/调用是否成功</summary>
    public bool Success { get; set; } = true;

    /// <summary>失败原因</summary>
    public string ErrorMessage { get; set; } = "";

    public static DSHResponse Failure(string message, string raw = "") =>
        new() { Success = false, ErrorMessage = message, RawContent = raw };

    public override string ToString() => $"action={Action}, target={Target}, response={Response}";
}

/// <summary>DSH 返回的 JSON 指令对象（action/target/params/response）</summary>
public sealed class DSHCommandDto
{
    [JsonPropertyName("action")]
    public string? Action { get; set; }

    [JsonPropertyName("target")]
    public string? Target { get; set; }

    [JsonPropertyName("params")]
    public Dictionary<string, string>? Params { get; set; }

    [JsonPropertyName("response")]
    public string? Response { get; set; }
}
