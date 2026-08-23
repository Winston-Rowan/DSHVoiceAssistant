using System.Globalization;
using System.Windows.Data;
using DivaVoiceAssistant.Models;

namespace DivaVoiceAssistant.Converters;

/// <summary>状态 → 中文状态文字转换器。</summary>
public sealed class StateToTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is DivaState state ? state switch
        {
            DivaState.Idle => "待命中",
            DivaState.WakeChecking => "检测唤醒词…",
            DivaState.Recording => "倾听中…",
            DivaState.Transcribing => "语音识别中…",
            DivaState.Thinking => "DSH 思考中…",
            DivaState.Executing => "执行中…",
            DivaState.Speaking => "回复中…",
            DivaState.Error => "出错了",
            _ => "未知状态"
        } : "未知状态";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
