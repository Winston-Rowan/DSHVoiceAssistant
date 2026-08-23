using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using DivaVoiceAssistant.Models;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace DivaVoiceAssistant.Converters;

/// <summary>状态 → 颜色转换器（状态指示圆点颜色）。</summary>
public sealed class StatusToColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var hex = value is DivaState state ? state switch
        {
            DivaState.Idle => "#27C93F",                                        // 绿：待命
            DivaState.WakeChecking => "#2E7CF6",                                // 蓝：检测
            DivaState.Recording => "#FFB300",                                   // 黄：倾听
            DivaState.Transcribing or DivaState.Thinking => "#2E7CF6",          // 蓝：处理
            DivaState.Executing => "#FF6D00",                                   // 橙：执行
            DivaState.Speaking => "#9C27B0",                                    // 紫：回复
            DivaState.Error => "#E53935",                                       // 红：出错
            _ => "#9AA5B1"
        } : "#9AA5B1";

        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
