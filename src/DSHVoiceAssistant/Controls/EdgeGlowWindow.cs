using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
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
/// Siri 唤醒特效（屏幕边缘彩色渐变亮起），**颜色随状态切换**：
///   倾听=青蓝 / 识别·思考=紫 / 执行=橙 / 播报=粉
///   - 四周边框一圈极细光带（4px 亮线 + 18px 柔和辉光），状态色系光谱顺时针环绕流动
///   - 底部中央多层光晕球体（白亮核心 → 状态主色 → 状态辅色晕），随音量缩放脉动
///   - 语音实时响应：说话时整体亮度增强、光球放大；停顿恢复 2.5s 低频呼吸
///   - 无边框透明置顶、点击穿透；节奏随状态（倾听 8s / 处理 4s / 播报 12s）
/// </summary>
public sealed class EdgeGlowWindow : Window
{
    // 状态专属色系（每套 5 色构成整圈连续光谱）
    private static readonly MediaColor[][] Palettes =
    [
        // 倾听（青蓝系）
        [
            MediaColor.FromRgb(0x5E, 0xE0, 0xD9),
            MediaColor.FromRgb(0x4C, 0xC9, 0xF0),
            MediaColor.FromRgb(0x0A, 0x84, 0xFF),
            MediaColor.FromRgb(0x43, 0x61, 0xEE),
            MediaColor.FromRgb(0x7B, 0x61, 0xFF)
        ],
        // 识别/思考（紫系）
        [
            MediaColor.FromRgb(0x7B, 0x61, 0xFF),
            MediaColor.FromRgb(0x9B, 0x5D, 0xE5),
            MediaColor.FromRgb(0xAF, 0x52, 0xDE),
            MediaColor.FromRgb(0xC7, 0x7D, 0xFF),
            MediaColor.FromRgb(0xE0, 0xAA, 0xFF)
        ],
        // 执行（橙系）
        [
            MediaColor.FromRgb(0xFF, 0xB7, 0x03),
            MediaColor.FromRgb(0xFF, 0x9E, 0x00),
            MediaColor.FromRgb(0xFF, 0x85, 0x00),
            MediaColor.FromRgb(0xFF, 0x6D, 0x00),
            MediaColor.FromRgb(0xFF, 0xB3, 0x4D)
        ],
        // 播报（粉系）
        [
            MediaColor.FromRgb(0xFF, 0x5F, 0x87),
            MediaColor.FromRgb(0xFF, 0x7B, 0xA9),
            MediaColor.FromRgb(0xF1, 0x5B, 0xB5),
            MediaColor.FromRgb(0xFF, 0x8F, 0xB1),
            MediaColor.FromRgb(0xFF, 0xB3, 0xC7)
        ]
    ];

    private const double LineThickness = 4;    // 极细亮线
    private const double HaloThickness = 18;   // 柔和辉光（含亮线区域）

    // 底部中央光球：3 层径向光晕（白核 → 状态主色 → 状态辅色）
    private const double BallCoreSize = 88;
    private const double BallMidSize = 140;
    private const double BallOuterSize = 210;

    // 每条边覆盖的色阶跨度：4 条边 × 1.25 = 5 = 色系周期 → 整圈无缝
    private const double HueSpanPerEdge = 1.25;

    private readonly Canvas _canvas;
    private readonly Border[] _lines = new Border[4];  // 细亮线
    private readonly Border[] _halos = new Border[4];  // 柔和辉光
    private readonly Ellipse[] _ball = new Ellipse[3]; // 光球三层
    private readonly ScaleTransform _ballScale = new();

    private MediaColor[] _palette = Palettes[0];
    private bool _shown;
    private DSHState _phase = DSHState.Recording;
    private double _level;

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

    /// <summary>显示光晕并按状态设置动画节奏与色系（已在显示中则仅更新）</summary>
    public void ShowGlow(DSHState state)
    {
        _phase = state;
        if (_shown)
        {
            ApplyPhase();
            return;
        }

        Width = SystemParameters.PrimaryScreenWidth;
        Height = SystemParameters.PrimaryScreenHeight;
        Left = 0;
        Top = 0;
        Opacity = 0.85;
        _level = 0;
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

    /// <summary>
    /// 语音实时响应（音频线程调用）：说话时亮度增强、光球放大。
    /// level 为归一化 RMS，静音≈0、正常说话≈0.02~0.15。
    /// </summary>
    public void UpdateLevel(float level)
    {
        if (!_shown) return;
        _level = Math.Clamp(level / 0.12, 0, 1); // 归一化到 0~1
        Opacity = 0.8 + _level * 0.2;            // 亮度：静止 0.8 ↔ 说话 1.0
        _ballScale.ScaleX = _ballScale.ScaleY = 1.0 + _level * 0.4; // 光球：静止 1.0 ↔ 说话 1.4
    }

    /// <summary>状态 → 色系（倾听=青蓝 / 识别·思考=紫 / 执行=橙 / 播报=粉）</summary>
    private static MediaColor[] GetPalette(DSHState state) => state switch
    {
        DSHState.Transcribing or DSHState.Thinking => Palettes[1],
        DSHState.Executing => Palettes[2],
        DSHState.Speaking => Palettes[3],
        _ => Palettes[0]
    };

    // ---------- 布局 ----------

    private void BuildLayout()
    {
        var w = SystemParameters.PrimaryScreenWidth;
        var h = SystemParameters.PrimaryScreenHeight;

        // 每边：先辉光（18px），再细亮线（4px）叠上
        _halos[0] = CreateStrip(horizontal: true, HaloThickness, w, 0, 0);                        // 上
        _lines[0] = CreateStrip(horizontal: true, LineThickness, w, 0, 0);
        _halos[1] = CreateStrip(horizontal: false, HaloThickness, h, w - HaloThickness, 0);       // 右
        _lines[1] = CreateStrip(horizontal: false, LineThickness, h, w - LineThickness, 0);
        _halos[2] = CreateStrip(horizontal: true, HaloThickness, w, 0, h - HaloThickness);        // 下
        _lines[2] = CreateStrip(horizontal: true, LineThickness, w, 0, h - LineThickness);
        _halos[3] = CreateStrip(horizontal: false, HaloThickness, h, 0, 0);                       // 左
        _lines[3] = CreateStrip(horizontal: false, LineThickness, h, 0, 0);

        foreach (var s in _halos) _canvas.Children.Add(s);
        foreach (var s in _lines) _canvas.Children.Add(s);

        // 底部中央光球（3 层径向光晕；颜色在 ApplyPhase 按状态更新）
        _ball[0] = CreateBall(BallCoreSize);
        _ball[1] = CreateBall(BallMidSize);
        _ball[2] = CreateBall(BallOuterSize);
        foreach (var b in _ball)
        {
            b.RenderTransformOrigin = new MediaPoint(0.5, 0.5);
            b.RenderTransform = _ballScale; // 三层共用缩放（随音量脉动）
            Canvas.SetLeft(b, (w - b.Width) / 2);
            Canvas.SetTop(b, h - b.Height * 0.62); // 球心位于屏高约 81% 处
            _canvas.Children.Add(b);
        }
    }

    private static Border CreateStrip(bool horizontal, double thickness, double length, double left, double top)
    {
        var border = new Border
        {
            Width = horizontal ? length : thickness,
            Height = horizontal ? thickness : length,
            Opacity = 1.0
        };
        Canvas.SetLeft(border, left);
        Canvas.SetTop(border, top);
        return border;
    }

    private static Ellipse CreateBall(double size)
    {
        return new Ellipse
        {
            Width = size,
            Height = size,
            Fill = MediaBrushes.Transparent, // ApplyPhase 按状态色系填充
            Opacity = 1.0
        };
    }

    // ---------- 动画 ----------

    private void ApplyPhase()
    {
        _palette = GetPalette(_phase);

        var cycle = _phase switch
        {
            DSHState.Recording => 8.0,
            DSHState.Transcribing or DSHState.Thinking or DSHState.Executing => 4.0,
            DSHState.Speaking => 12.0,
            _ => 8.0
        };

        for (var i = 0; i < 4; i++)
        {
            var (flow, lineMask, haloMask) = BuildEdge(cycle, i / 4.0, i);
            _lines[i].Background = flow;
            _lines[i].OpacityMask = lineMask;
            _halos[i].Background = flow;
            _halos[i].OpacityMask = haloMask;
        }

        // 光球颜色随状态色系：白核 + 主色中晕 + 辅色外晕
        _ball[0].Fill = BuildRadial(MediaColor.FromArgb(0xFF, 0xFF, 0xFF, 0xFF));
        _ball[1].Fill = BuildRadial(WithAlpha(_palette[0], 0xCC));
        _ball[2].Fill = BuildRadial(WithAlpha(_palette[2], 0x99));

        // 低频呼吸（静止时）：细线/辉光/四角/光球一起明暗循环，周期 2.5s
        var breath = new DoubleAnimation(0.65, 1.0, TimeSpan.FromSeconds(2.5))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever
        };
        foreach (var s in _lines) s.BeginAnimation(OpacityProperty, breath);
        foreach (var s in _halos) s.BeginAnimation(OpacityProperty, breath);
        foreach (var b in _ball) b.BeginAnimation(OpacityProperty, breath);
    }

    private static RadialGradientBrush BuildRadial(MediaColor glowColor) => new()
    {
        GradientOrigin = new MediaPoint(0.5, 0.5),
        Center = new MediaPoint(0.5, 0.5),
        RadiusX = 0.5,
        RadiusY = 0.5,
        GradientStops = new GradientStopCollection(
        [
            new GradientStop(glowColor, 0),
            new GradientStop(WithAlpha(glowColor, 0x00), 1)
        ])
    };

    private static MediaColor WithAlpha(MediaColor c, byte alpha) =>
        MediaColor.FromArgb(alpha, c.R, c.G, c.B);

    /// <summary>
    /// 构建一条边：沿边方向的连续光谱渐变（offset 平移动画 = 顺时针跑动），
    /// 细亮线用平滑衰减掩膜（外硬内柔），辉光用更宽的平滑衰减（无层间边界）。
    /// </summary>
    private (LinearGradientBrush flow, LinearGradientBrush lineMask, LinearGradientBrush haloMask) BuildEdge(double cycle, double phase, int edge)
    {
        // 沿边流动方向（上→右→下→左 = 顺时针，首尾相接）
        var (sx, sy, ex, ey) = edge switch
        {
            0 => (0.0, 0.0, 1.0, 0.0),
            1 => (0.0, 0.0, 0.0, 1.0),
            2 => (1.0, 0.0, 0.0, 0.0),
            _ => (0.0, 1.0, 0.0, 0.0)
        };

        // 整圈连续光谱：本条边从 start 色阶开始，覆盖 HueSpanPerEdge 个色阶
        var start = HueSpanPerEdge * edge;
        var flow = new LinearGradientBrush
        {
            StartPoint = new MediaPoint(sx, sy),
            EndPoint = new MediaPoint(ex, ey)
        };
        const int stops = 6;
        var gradientStops = new GradientStop[stops + 1];
        for (var k = 0; k <= stops; k++)
        {
            var stop = new GradientStop(ColorAt(start + HueSpanPerEdge * k / stops), k / (double)stops);
            stop.BeginAnimation(GradientStop.OffsetProperty, new DoubleAnimation(
                k / (double)stops, k / (double)stops + 1.0, TimeSpan.FromSeconds(cycle))
            {
                BeginTime = TimeSpan.FromSeconds(-cycle * phase),
                RepeatBehavior = RepeatBehavior.Forever
            });
            gradientStops[k] = stop;
        }
        flow.GradientStops = new GradientStopCollection(gradientStops);

        // 跨宽度方向：屏幕边框侧 → 内侧（四条边统一：边框在本地坐标 0 侧）
        var (msx, msy, mex, mey) = edge switch
        {
            0 => (0.0, 0.0, 0.0, 1.0), // 上：边框=本地 y0
            1 => (0.0, 0.0, 1.0, 0.0), // 右：边框=本地 x0
            2 => (0.0, 1.0, 0.0, 0.0), // 下：边框=本地 y1
            _ => (0.0, 0.0, 1.0, 0.0)  // 左：边框=本地 x0（与右同理）
        };

        // 细亮线掩膜：边缘处最亮，向内平滑衰减（4px 内）
        var lineMask = new LinearGradientBrush
        {
            StartPoint = new MediaPoint(msx, msy),
            EndPoint = new MediaPoint(mex, mey),
            GradientStops = new GradientStopCollection(
            [
                new GradientStop(MediaColor.FromArgb(0xFF, 0xFF, 0xFF, 0xFF), 0),
                new GradientStop(MediaColor.FromArgb(0x9E, 0xFF, 0xFF, 0xFF), 0.45),
                new GradientStop(MediaColor.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 1)
            ])
        };

        // 辉光掩膜：多停靠点曲线平滑衰减（无层间边界）
        var haloMask = new LinearGradientBrush
        {
            StartPoint = new MediaPoint(msx, msy),
            EndPoint = new MediaPoint(mex, mey),
            GradientStops = new GradientStopCollection(
            [
                new GradientStop(MediaColor.FromArgb(0xB4, 0xFF, 0xFF, 0xFF), 0),
                new GradientStop(MediaColor.FromArgb(0x7D, 0xFF, 0xFF, 0xFF), 0.22),
                new GradientStop(MediaColor.FromArgb(0x47, 0xFF, 0xFF, 0xFF), 0.5),
                new GradientStop(MediaColor.FromArgb(0x1A, 0xFF, 0xFF, 0xFF), 0.8),
                new GradientStop(MediaColor.FromArgb(0x00, 0xFF, 0xFF, 0xFF), 1)
            ])
        };
        return (flow, lineMask, haloMask);
    }

    /// <summary>状态色系连续色采样：t 以"色阶"为单位（5 色周期环绕），相邻色线性插值</summary>
    private MediaColor ColorAt(double t)
    {
        var n = _palette.Length;
        var idx = t % n;
        if (idx < 0) idx += n;
        var i = (int)Math.Floor(idx);
        var frac = idx - i;
        var a = _palette[i % n];
        var b = _palette[(i + 1) % n];
        return MediaColor.FromRgb(
            (byte)(a.R + (b.R - a.R) * frac),
            (byte)(a.G + (b.G - a.G) * frac),
            (byte)(a.B + (b.B - a.B) * frac));
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

    private const int WmExStyle = -20;
    private const long WsExTransparent = 0x20;
    private const long WsExToolWindow = 0x80;
    private const long WsExNoActivate = 0x08000000;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
}
