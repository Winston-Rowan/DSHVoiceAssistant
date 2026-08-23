using System.Text.Json;
using DivaVoiceAssistant.Models;

namespace DivaVoiceAssistant.Utils;

/// <summary>
/// DSH 返回内容解析器：把模型输出的文本转换为结构化指令 DSHResponse。
/// 兼容以下情况：纯 JSON / Markdown 代码块包裹 / 前后带多余文字 / 未输出 JSON（兜底为文本回复）。
/// </summary>
public static class DivaCommandParser
{
    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

    public static DSHResponse Parse(string content)
    {
        var json = JsonUtils.ExtractJsonObject(content);
        if (json != null)
        {
            try
            {
                var dto = JsonSerializer.Deserialize<DSHCommandDto>(json, ReadOptions);
                if (dto != null && !string.IsNullOrWhiteSpace(dto.Action))
                {
                    return new DSHResponse
                    {
                        Action = dto.Action.Trim().ToLowerInvariant(),
                        Target = dto.Target ?? "",
                        Params = dto.Params,
                        Response = dto.Response ?? "",
                        RawContent = content
                    };
                }
            }
            catch (JsonException)
            {
                // JSON 结构异常时走文本回复兜底
            }
        }

        // 兜底：模型没有输出 JSON 时，把原始内容当作纯文本回复朗读
        var fallback = content.Trim();
        if (fallback.Length > 500) fallback = fallback[..500];
        return new DSHResponse
        {
            Action = "text_reply",
            Target = fallback,
            Response = fallback,
            RawContent = content
        };
    }
}
