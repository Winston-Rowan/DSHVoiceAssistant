namespace DSHVoiceAssistant.Utils;

/// <summary>
/// 中文回复保障：检测文本是否需要翻译成中文（供语音播报前兜底）。
/// 判定：正文（去空白/标点）中非中文汉字的字母类字符占比过高 → 需要翻译。
/// </summary>
public static class ChineseTextGuard
{
    /// <summary>是否需要翻译成中文（正文以非汉字为主时返回 true）</summary>
    public static bool NeedsTranslation(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;

        var hanzi = 0;
        var foreign = 0;
        foreach (var c in text)
        {
            if (c is >= '\u4E00' and <= '\u9FFF' or >= '\u3400' and <= '\u4DBF')
            {
                hanzi++;
            }
            else if (char.IsLetter(c))
            {
                foreign++; // 拉丁字母/假名/谚文等非汉字字母
            }
            // 数字、标点、空白不计入
        }

        if (hanzi + foreign == 0) return false;
        // 非汉字字母 ≥5 个且占比 >30% 才翻译（避免 "D盘剩余空间：30.5 GB" 这类少量英文术语被误判）
        return foreign >= 5 && foreign / (double)(hanzi + foreign) > 0.3;
    }

    /// <summary>检测文本中是否含日文假名（调试/诊断用）</summary>
    public static bool ContainsJapaneseKana(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        foreach (var c in text)
        {
            if (c is >= '\u3040' and <= '\u30FF' or >= '\u31F0' and <= '\u31FF') return true;
        }
        return false;
    }
}
