namespace DSHVoiceAssistant.Utils;

/// <summary>
/// 提醒/闹钟参数解析（纯函数，便于单元测试）：
/// params.minutes = "N"（N 分钟后，1~1440）；或 params.at = "HH:mm"（今天该时刻，已过则明天）；
/// params.message = 提醒内容（可选，缺省用通用文案）。
/// </summary>
public static class ReminderParser
{
    /// <summary>解析出距触发的时间间隔；参数缺失/非法返回 null</summary>
    public static TimeSpan? ParseDelay(Dictionary<string, string>? parameters)
    {
        if (parameters == null) return null;

        // minutes=N（N 分钟后）
        if (parameters.TryGetValue("minutes", out var minutesStr)
            && double.TryParse(minutesStr, out var minutes)
            && minutes > 0 && minutes <= 24 * 60)
        {
            return TimeSpan.FromMinutes(minutes);
        }

        // at=HH:mm（今天该时刻；已过则顺延到明天）
        if (parameters.TryGetValue("at", out var atStr)
            && TimeSpan.TryParse(atStr, out var at))
        {
            var now = DateTime.Now;
            var target = now.Date + at;
            if (target <= now) target = target.AddDays(1);
            return target - now;
        }

        return null;
    }

    /// <summary>提醒内容（message 参数）；缺省返回 null</summary>
    public static string? GetMessage(Dictionary<string, string>? parameters)
    {
        if (parameters != null
            && parameters.TryGetValue("message", out var m)
            && !string.IsNullOrWhiteSpace(m))
        {
            return m.Trim();
        }
        return null;
    }
}
