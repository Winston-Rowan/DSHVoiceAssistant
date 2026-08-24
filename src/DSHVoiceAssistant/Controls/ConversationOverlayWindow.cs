using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using DSHVoiceAssistant.Models;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaColor = System.Windows.Media.Color;
using MediaHorizontalAlignment = System.Windows.HorizontalAlignment;

namespace DSHVoiceAssistant.Controls;

/// <summary>
/// 对话浮层可见性判定（纯函数，便于单元测试）。
/// 规则：开关开启 && 主窗口不可见 && 有内容可展示的忙碌状态（识别/思考/执行/播报）。
/// </summary>
public static class ConversationOverlayVisibility
{
    public static bool ShouldShow(bool enabled, bool mainWindowVisible, DSHState state) =>
        enabled && !mainWindowVisible &&
        state is DSHState.Transcribing or DSHState.Thinking or DSHState.Executing or DSHState.Speaking;
}

/// <summary>
/// 桌面中下部对话浮层：无背景（全透明）、文字不透明（带柔和投影保证可读性），
/// 点击穿透不挡操作。显示最近一句用户指令与助手回复，随对话结束（回到待命/开窗）消失。
/// </summary>
public sealed class ConversationOverlayWindow : Window
{
    private const double OverlayHeight = 220;
    private static readonly MediaColor ShadowColor = MediaColor.FromRgb(0x00, 0x00, 0x00);

    private readonly TextBlock _userText;
    private readonly TextBlock _replyText;
    private bool _shown;

    public ConversationOverlayWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = null; // 无背景：全透明
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = false;
        Focusable = false;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;

        Width = SystemParameters.PrimaryScreenWidth;
        Height = OverlayHeight;
        Left = 0;
        Top = SystemParameters.PrimaryScreenHeight * 0.66; // 桌面中下部

        _userText = CreateLine(26, FontWeights.SemiBold);
        _replyText = CreateLine(24, FontWeights.Normal);

        var panel = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = MediaHorizontalAlignment.Center
        };
        panel.Children.Add(_userText);
        panel.Children.Add(_replyText);
        Content = panel;
    }

    private static TextBlock CreateLine(double fontSize, FontWeight weight) => new()
    {
        FontSize = fontSize,
        FontWeight = weight,
        Foreground = MediaBrushes.White, // 文字不透明
        TextAlignment = TextAlignment.Center,
        TextWrapping = TextWrapping.Wrap,
        MaxWidth = SystemParameters.PrimaryScreenWidth * 0.8,
        Margin = new Thickness(40, 6, 40, 6),
        // 柔和投影：无背景下的可读性（不是背景，不影响透明）
        Effect = new DropShadowEffect
        {
            BlurRadius = 6,
            ShadowDepth = 1.5,
            Direction = 270,
            Opacity = 0.9,
            Color = ShadowColor
        }
    };

    /// <summary>显示对话（已在显示时仅更新文字）；带 250ms 淡入</summary>
    public void ShowOverlay(string userText, string replyText)
    {
        _userText.Text = userText;
        _replyText.Text = replyText;
        _replyText.Visibility = string.IsNullOrWhiteSpace(replyText)
            ? Visibility.Collapsed
            : Visibility.Visible;

        if (_shown) return;
        _shown = true;
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250)));
        Show();
    }

    public void HideOverlay()
    {
        if (!_shown) return;
        _shown = false;
        BeginAnimation(OpacityProperty, null);
        Hide();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        var exStyle = GetWindowLongPtr(hwnd, WmExStyle).ToInt64();
        exStyle |= WsExTransparent | WsExToolWindow | WsExNoActivate;
        SetWindowLongPtr(hwnd, WmExStyle, new IntPtr(exStyle));
    }

    private const int WmExStyle = -20;
    private const long WsExTransparent = 0x20;
    private const long WsExToolWindow = 0x80;
    private const long WsExNoActivate = 0x08000000;

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
}
