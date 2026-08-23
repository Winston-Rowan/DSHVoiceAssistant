using System.Text.RegularExpressions;

namespace DivaVoiceAssistant.Utils;

/// <summary>JSON 工具：从模型输出中稳健地提取 JSON 对象。</summary>
public static partial class JsonUtils
{
    /// <summary>
    /// 从文本中提取第一个完整的 JSON 对象（支持 Markdown 代码块包裹、前后多余文字）。
    /// 提取失败返回 null。
    /// </summary>
    public static string? ExtractJsonObject(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var t = text.Trim();

        // 去掉 ```json ... ``` 代码块围栏
        t = FenceStartRegex().Replace(t, "");
        t = FenceEndRegex().Replace(t, "");

        var start = t.IndexOf('{');
        if (start < 0) return null;

        // 括号配对扫描（跳过字符串内部的 { }）
        var depth = 0;
        var inString = false;
        var prev = '\0';
        for (var i = start; i < t.Length; i++)
        {
            var c = t[i];
            if (inString)
            {
                if (c == '"' && prev != '\\') inString = false;
            }
            else
            {
                switch (c)
                {
                    case '"':
                        inString = true;
                        break;
                    case '{':
                        depth++;
                        break;
                    case '}':
                        depth--;
                        if (depth == 0) return t.Substring(start, i - start + 1);
                        break;
                }
            }
            prev = c;
        }
        return null;
    }

    /// <summary>截断长文本（用于日志/界面展示）</summary>
    public static string Truncate(string text, int maxLength = 300)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength) return text;
        return text[..maxLength] + "…";
    }

    [GeneratedRegex(@"^```(?:json)?\s*", RegexOptions.IgnoreCase)]
    private static partial Regex FenceStartRegex();

    [GeneratedRegex(@"```\s*$")]
    private static partial Regex FenceEndRegex();
}
