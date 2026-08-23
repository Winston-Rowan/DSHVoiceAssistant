using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using DivaVoiceAssistant.Models;
using DivaVoiceAssistant.Utils;

namespace DivaVoiceAssistant.Services;

/// <summary>
/// 系统托盘图标服务。图标颜色随状态变化：绿=待命，黄=倾听，蓝=处理中，紫=回复中，红=出错，灰=静音。
/// 所有公开方法线程安全（内部封送到创建线程）。
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private readonly SynchronizationContext? _uiContext;
    private readonly NotifyIcon _notify;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _listenItem;

    /// <summary>请求显示主窗口</summary>
    public event Action? ShowRequested;

    /// <summary>请求立即对话（快捷键唤醒）</summary>
    public event Action? ForceActivateRequested;

    /// <summary>请求切换监听（静音/取消静音）</summary>
    public event Action? ToggleListeningRequested;

    /// <summary>请求退出应用</summary>
    public event Action? ExitRequested;

    public TrayIconService()
    {
        _uiContext = SynchronizationContext.Current;

        _notify = new NotifyIcon
        {
            Text = "Diva 语音助手",
            Visible = true,
            Icon = CreateIcon(GetColor(DivaState.Idle, muted: false))
        };

        _menu = new ContextMenuStrip();
        _menu.Items.Add("显示主窗口", null, (_, _) => ShowRequested?.Invoke());
        _menu.Items.Add("立即对话 (Ctrl+Alt+D)", null, (_, _) => ForceActivateRequested?.Invoke());

        _listenItem = new ToolStripMenuItem("启用监听") { Checked = true };
        _listenItem.Click += (_, _) => ToggleListeningRequested?.Invoke();
        _menu.Items.Add(_listenItem);

        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("退出", null, (_, _) => ExitRequested?.Invoke());

        _notify.ContextMenuStrip = _menu;
        _notify.DoubleClick += (_, _) => ShowRequested?.Invoke();
    }

    /// <summary>更新托盘图标状态与提示文字</summary>
    public void UpdateState(DivaState state, bool muted)
    {
        RunOnUi(() =>
        {
            _notify.Icon = CreateIcon(GetColor(state, muted));
            _notify.Text = "Diva 语音助手 - " + GetText(state) + (muted ? "（已静音）" : "");
            _listenItem.Checked = !muted;
        });
    }

    public void Dispose()
    {
        RunOnUi(() =>
        {
            _notify.Visible = false;
            _notify.Dispose();
            _menu.Dispose();
        });
    }

    // ---------- 内部 ----------

    private void RunOnUi(Action action)
    {
        if (_uiContext != null && _uiContext != SynchronizationContext.Current)
        {
            _uiContext.Post(_ => action(), null);
        }
        else
        {
            action();
        }
    }

    private static string GetText(DivaState state) => state switch
    {
        DivaState.Idle => "待命中",
        DivaState.WakeChecking => "检测中",
        DivaState.Recording => "倾听中",
        DivaState.Transcribing => "识别中",
        DivaState.Thinking => "DSH 思考中",
        DivaState.Executing => "执行中",
        DivaState.Speaking => "回复中",
        DivaState.Error => "出错了",
        _ => "未知"
    };

    private static Color GetColor(DivaState state, bool muted)
    {
        if (muted) return Color.Gray;
        return state switch
        {
            DivaState.Idle => Color.FromArgb(39, 201, 63),      // 绿
            DivaState.WakeChecking => Color.FromArgb(46, 124, 246), // 蓝
            DivaState.Recording => Color.FromArgb(255, 179, 0), // 黄
            DivaState.Transcribing or DivaState.Thinking => Color.FromArgb(46, 124, 246), // 蓝
            DivaState.Executing => Color.FromArgb(255, 109, 0), // 橙
            DivaState.Speaking => Color.FromArgb(156, 39, 176), // 紫
            DivaState.Error => Color.FromArgb(229, 57, 53),     // 红
            _ => Color.FromArgb(154, 165, 177)
        };
    }

    private static Icon CreateIcon(Color color)
    {
        using var bitmap = new Bitmap(16, 16);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var fill = new SolidBrush(color);
            g.FillEllipse(fill, 1, 1, 14, 14);
            using var border = new Pen(Color.White, 1.5f);
            g.DrawEllipse(border, 2.5f, 2.5f, 11, 11);
        }

        var hIcon = bitmap.GetHicon();
        try
        {
            using var temp = Icon.FromHandle(hIcon);
            return (Icon)temp.Clone();
        }
        finally
        {
            DestroyIcon(hIcon);
        }
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
