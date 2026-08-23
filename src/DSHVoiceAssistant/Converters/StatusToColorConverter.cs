using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using DSHVoiceAssistant.Models;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace DSHVoiceAssistant.Converters;

/// <summary>状态 → 颜色转换器（状态指示圆点颜色）。</summary>
public sealed class StatusToColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var hex = value is DSHState state ? state switch
        {
            DSHState.Idle => "#27C93F",                                        // 绿：待命
            DSHState.WakeChecking => "#2E7CF6",                                // 蓝：检测
            DSHState.Recording => "#FFB300",                                   // 黄：倾听
            DSHState.Transcribing or DSHState.Thinking => "#2E7CF6",          // 蓝：处理
            DSHState.Executing => "#FF6D00",                                   // 橙：执行
            DSHState.Speaking => "#9C27B0",                                    // 紫：回复
            DSHState.Error => "#E53935",                                       // 红：出错
            _ => "#9AA5B1"
        } : "#9AA5B1";

        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
