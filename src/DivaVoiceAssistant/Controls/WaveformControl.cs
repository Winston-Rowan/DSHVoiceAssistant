using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Point = System.Windows.Point;
using Pen = System.Windows.Media.Pen;

namespace DivaVoiceAssistant.Controls;

/// <summary>
/// 音量波形可视化控件：由音频线程 PushLevel 推入音量（线程安全），
/// UI 线程定时重绘，渲染最近若干帧的柱状图。
/// </summary>
public sealed class WaveformControl : FrameworkElement
{
    public static readonly DependencyProperty BarColorProperty =
        DependencyProperty.Register(
            nameof(BarColor),
            typeof(Brush),
            typeof(WaveformControl),
            new PropertyMetadata(Brushes.SteelBlue, (d, _) => ((WaveformControl)d).InvalidateVisual()));

    public Brush BarColor
    {
        get => (Brush)GetValue(BarColorProperty);
        set => SetValue(BarColorProperty, value);
    }

    private const int MaxBars = 64;
    private readonly object _gate = new();
    private readonly Queue<float> _levels = new();
    private readonly DispatcherTimer _timer;

    public WaveformControl()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) }; // ~30fps
        _timer.Tick += (_, _) => InvalidateVisual();
        _timer.Start();
        SnapsToDevicePixels = true;
    }

    /// <summary>推送一帧音量（0~1），任何线程可调用。</summary>
    public void PushLevel(float level)
    {
        lock (_gate)
        {
            _levels.Enqueue(Math.Clamp(level, 0f, 1f));
            while (_levels.Count > MaxBars) _levels.Dequeue();
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        float[] snapshot;
        lock (_gate) snapshot = _levels.ToArray();

        var w = ActualWidth;
        var h = ActualHeight;
        if (w <= 4 || h <= 4) return;

        if (snapshot.Length == 0)
        {
            // 无数据时画一条虚线基线
            var dash = new Pen(BarColor, 2) { DashStyle = new DashStyle(new double[] { 4, 4 }, 0) };
            dc.DrawLine(dash, new Point(4, h / 2), new Point(w - 4, h / 2));
            return;
        }

        const double gap = 2;
        var barWidth = Math.Max(1, (w - gap * (snapshot.Length + 1)) / snapshot.Length);
        for (var i = 0; i < snapshot.Length; i++)
        {
            var barHeight = Math.Max(2, snapshot[i] * h * 0.92);
            var x = gap + i * (barWidth + gap);
            dc.DrawRectangle(BarColor, null, new Rect(x, (h - barHeight) / 2, barWidth, barHeight));
        }
    }
}
