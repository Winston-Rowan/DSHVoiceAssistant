namespace DivaVoiceAssistant.Utils;

/// <summary>
/// 唤醒词文本匹配工具。
/// </summary>
public static class WakeWordMatcher
{
    /// <summary>识别文本是否包含任一唤醒词变体（不区分大小写）</summary>
    public static bool ContainsWakeWord(string? text, IEnumerable<string> variants)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        foreach (var variant in variants)
        {
            if (!string.IsNullOrEmpty(variant) && text.Contains(variant, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>
    /// 若唤醒词出现在句首，则将其（及紧跟的标点）从文本中剥离。
    /// 例如 "Diva，打开记事本" → "打开记事本"。
    /// </summary>
    public static string StripLeadingWakeWord(string text, IEnumerable<string> variants)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;

        var trimmed = text.Trim();
        foreach (var variant in variants)
        {
            if (string.IsNullOrEmpty(variant)) continue;

            var index = trimmed.IndexOf(variant, StringComparison.OrdinalIgnoreCase);
            if (index < 0) continue;

            // 只处理唤醒词位于句首的情况（前面只有空白）
            var before = trimmed[..index];
            if (!string.IsNullOrWhiteSpace(before)) continue;

            trimmed = trimmed[(index + variant.Length)..]
                .TrimStart(' ', ',', '，', '.', '。', '!', '！', '?', '？', ':', '：', ';', '；');
            break;
        }
        return trimmed;
    }
}
