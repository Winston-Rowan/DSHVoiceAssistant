using System.Text;

namespace DSHVoiceAssistant.Utils;

/// <summary>
/// 语音结束对话匹配：用户说"没事了/退下/滚吧"等即结束当前对话、回到待命。
/// 匹配规则：文本归一化（去空白与标点）后整句相等，或句子以词条开头（词条长度≥2）。
/// 不做子串匹配，避免"打开记事本"等正常指令误判。
/// </summary>
public static class EndConversationMatcher
{
    private static readonly string[] Phrases =
    [
        "没事了", "没事儿了", "没事啦",
        "退下", "退下吧", "你退下吧", "退下吧你",
        "滚吧", "滚开", "滚蛋",
        "不聊了", "结束对话", "结束吧",
        "再见", "拜拜", "拜拜了",
        "没你事了", "没你的事了", "没别的事了", "没有别的事了",
        "就这样吧", "到此为止", "就到这吧",
        "不用了", "不用了谢谢"
    ];

    public static bool IsEndPhrase(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var normalized = Normalize(text);
        if (normalized.Length == 0) return false;

        foreach (var phrase in Phrases)
        {
            if (normalized == phrase) return true;
            if (phrase.Length >= 2 && normalized.StartsWith(phrase, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    /// <summary>去除空白与标点（保留汉字/字母/数字）</summary>
    public static string Normalize(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c)) continue;
            if (char.IsPunctuation(c) || char.IsSymbol(c)) continue;
            sb.Append(c);
        }
        return sb.ToString();
    }
}
