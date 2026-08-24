using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using DSHVoiceAssistant.Models;
using MediaColor = System.Windows.Media.Color;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaPoint = System.Windows.Point;

namespace DSHVoiceAssistant.Controls;

/// <summary>
/// 边缘光晕可见性判定（纯函数，便于单元测试）。
/// 规则：开关开启 && 主窗口不可见 && 处于忙碌状态（倾听/识别/思考/执行/播报）时显示。
/// </summary>
public static class EdgeGlowVisibility
{
    public static bool ShouldShow(bool enabled, bool mainWindowVisible, DSHState state) =>
        enabled && !mainWindowVisible &&
        state is DSHState.Recording or DSHState.Transcribing or DSHState.Thinking
            or DSHState.Executing or DSHState.Speaking;
}

/// <summary>
/// 屏幕边缘 Siri 风格光晕：无边框透明置顶悬浮窗。
/// 主屏幕四边各一条彩色流光光带 + 四角加亮光斑，光沿边框"跑"动、整体呼吸脉动，
/// 节奏随助手状态变化（倾听=8s 周期 / 处理=4s / 播报=12s）。
/// 窗口点击穿透（WS_EX_TRANSPARENT），不拦截任何鼠标操作。
/// </summary>
public sealed class EdgeGlowWindow : Window
{
    // Siri 风格柔光调色板（低饱和：粉→紫→蓝→青→暖橙）
    private static readonly MediaColor[] Palette =
    [
        MediaColor.FromRgb(0xFF, 0x9B, 0xC7),
        MediaColor.FromRgb(0xB3, 0x9D, 0xFF),
        MediaColor.FromRgb(0x7E, 0xC8, 0xFF),
        MediaColor.FromRgb(0x8F, 0xF0, 0xE8),
        MediaColor.FromRgb(0xFF, 0xC5, 0x8F)
    ];

    private const double Thickness = 28;
    private const double CornerSize = 40;
    private const int WmExStyle = -20;
    private const long WsExTransparent = 0x20;
    private const long WsExToolWindow = 0x80;
    private const long WsExNoActivate = 0x08000000;

    private readonly Canvas _canvas;
    private readonly Border[] _strips = new Border[4];  // 0=上 1=右 2=下 3=左
    private readonly Border[] _corners = new Border[4]; // 0=左上 1=右上 2=右下 3=左下

    private bool _shown;
    private DSHState _phase = DSHState.Recording;

    public EdgeGlowWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = MediaBrushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = false;
        Focusable = false;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;

        _canvas = new Canvas();
        Content = _canvas;
        BuildLayout();
    }

    /// <summary>显示光晕并按状态设置动画节奏（已在显示中则仅更新节奏）</summary>
    public void ShowGlow(DSHState state)
    {
        _phase = state;
        if (_shown)
        {
            ApplyPhase();
            return;
        }

        // 刷新位置/尺寸（应对分辨率变化）
        Width = SystemParameters.PrimaryScreenWidth;
        Height = SystemParameters.PrimaryScreenHeight;
        Left = 0;
        Top = 0;
        ApplyPhase();
        _shown = true;
        Show();
    }

    /// <summary>隐藏光晕并停止动画</summary>
    public void HideGlow()
    {
        if (!_shown) return;
        _shown = false;
        BeginAnimation(OpacityProperty, null);
        Hide();
    }

    // ---------- 布局 ----------

    private void BuildLayout()
    {
        var w = SystemParameters.PrimaryScreenWidth;
        var h = SystemParameters.PrimaryScreenHeight;

        _strips[0] = CreateStrip(horizontal: true, w, Thickness, 0, 0);          // 上
        _strips[1] = CreateStrip(horizontal: false, Thickness, h, w - Thickness, 0); // 右
        _strips[2] = CreateStrip(horizontal: true, w, Thickness, 0, h - Thickness);  // 下
        _strips[3] = CreateStrip(horizontal: false, Thickness, h, 0, 0);         // 左

        _corners[0] = CreateCorner(0, 0);                                     // 左上
        _corners[1] = CreateCorner(w - CornerSize, 0);                        // 右上
        _corners[2] = CreateCorner(w - CornerSize, h - CornerSize);           // 右下
        _corners[3] = CreateCorner(0, h - CornerSize);                        // 左下

        foreach (var s in _strips) _canvas.Children.Add(s);
        foreach (var c in _corners) _canvas.Children.Add(c);
    }

    private static Border CreateStrip(bool horizontal, double width, double height, double left, double top)
    {
        var border = new Border
        {
            Width = width,
            Height = height,
            Opacity = 0.8
        };
        Canvas.SetLeft(border, left);
        Canvas.SetTop(border, top);
        return border;
    }

    private static Border CreateCorner(double left, double top)
    {
        var border = new Border
        {
            Width = CornerSize,
            Height = CornerSize,
            CornerRadius = new CornerRadius(CornerSize / 2),
            Opacity = 0.95
        };
        Canvas.SetLeft(border, left);
        Canvas.SetTop(border, top);
        return border;
    }

    // ---------- 动画 ----------

    /// <summary>按当前状态重建渐变与动画（旧动画对象随旧画刷被回收）</summary>
    private void ApplyPhase()
    {
        var cycle = _phase switch
        {
            DSHState.Recording => 8.0,                       // 倾听：正常流动
            DSHState.Transcribing or DSHState.Thinking or DSHState.Executing => 4.0, // 处理：加速
            DSHState.Speaking => 12.0,                       // 播报：舒缓
            _ => 8.0
        };

        // 四条边相位错开 1/4 周期 → 光沿边框"跑"
        for (var i = 0; i < _strips.Length; i++)
        {
            var horizontal = i is 0 or 2;
            var (brush, shadow) = BuildGlow(cycle, i / 4.0, horizontal);
            _strips[i].Background = brush;
            _strips[i].Effect = shadow;
        }
        for (var i = 0; i < _corners.Length; i++)
        {
            var (brush, shadow) = BuildGlow(cycle, (i + 0.5) / 4.0, horizontal: false);
            _corners[i].Background = brush;
            _corners[i].Effect = shadow;
        }

        // 整体呼吸脉动
        BeginAnimation(OpacityProperty, new DoubleAnimation(0.75, 1.0, TimeSpan.FromSeconds(2.5))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever
        });
    }

    /// <summary>
    /// 构建一条光带：线性渐变（颜色沿调色板循环流动，相位错开形成跑动感）+ 同色柔光阴影。
    /// </summary>
    private static (LinearGradientBrush brush, DropShadowEffect shadow) BuildGlow(
        double cycle, double phase, bool horizontal)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = horizontal ? new MediaPoint(0, 0.5) : new MediaPoint(0.5, 0),
            EndPoint = horizontal ? new MediaPoint(1, 0.5) : new MediaPoint(0.5, 1)
        };

        var n = Palette.Length;
        var stops = new GradientStop[n + 1];
        for (var i = 0; i <= n; i++)
        {
            var stop = new GradientStop(Palette[i % n], i / (double)n);
            stop.BeginAnimation(GradientStop.ColorProperty, BuildColorLoop(cycle, phase));
            stops[i] = stop;
        }
        brush.GradientStops = new GradientStopCollection(stops);

        var shadow = new DropShadowEffect
        {
            BlurRadius = 24,
            ShadowDepth = 0,
            Direction = 0,
            Opacity = 0.85,
            Color = Palette[0]
        };
        shadow.BeginAnimation(DropShadowEffect.ColorProperty, BuildColorLoop(cycle, phase));

        return (brush, shadow);
    }

    /// <summary>沿调色板循环的无限颜色动画（负 BeginTime 实现相位错开）</summary>
    private static ColorAnimationUsingKeyFrames BuildColorLoop(double cycle, double phase)
    {
        var n = Palette.Length;
        var anim = new ColorAnimationUsingKeyFrames
        {
            Duration = TimeSpan.FromSeconds(cycle),
            BeginTime = TimeSpan.FromSeconds(-cycle * phase),
            RepeatBehavior = RepeatBehavior.Forever
        };
        for (var k = 0; k <= n; k++)
        {
            anim.KeyFrames.Add(new LinearColorKeyFrame(
                Palette[k % n], KeyTime.FromTimeSpan(TimeSpan.FromSeconds(cycle * k / n))));
        }
        return anim;
    }

    // ---------- 点击穿透 ----------

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        var exStyle = GetWindowLongPtr(hwnd, WmExStyle).ToInt64();
        exStyle |= WsExTransparent | WsExToolWindow | WsExNoActivate;
        SetWindowLongPtr(hwnd, WmExStyle, new IntPtr(exStyle));
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
}
