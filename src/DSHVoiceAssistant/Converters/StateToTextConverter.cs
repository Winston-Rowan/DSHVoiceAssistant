using System.Globalization;
using System.Windows.Data;
using DSHVoiceAssistant.Models;

namespace DSHVoiceAssistant.Converters;

/// <summary>状态 → 中文状态文字转换器。</summary>
public sealed class StateToTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is DSHState state ? state switch
        {
            DSHState.Idle => "待命中",
            DSHState.WakeChecking => "检测唤醒词…",
            DSHState.Recording => "倾听中…",
            DSHState.Transcribing => "语音识别中…",
            DSHState.Thinking => "DSH 思考中…",
            DSHState.Executing => "执行中…",
            DSHState.Speaking => "回复中…",
            DSHState.Error => "出错了",
            _ => "未知状态"
        } : "未知状态";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
