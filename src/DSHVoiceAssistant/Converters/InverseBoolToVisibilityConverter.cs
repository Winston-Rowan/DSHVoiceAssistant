using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace DSHVoiceAssistant.Converters;

/// <summary>取反的 Bool → Visibility 转换器（true → Collapsed）。</summary>
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
