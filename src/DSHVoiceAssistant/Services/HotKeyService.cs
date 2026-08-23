using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using DSHVoiceAssistant.Utils;

namespace DSHVoiceAssistant.Services;

/// <summary>
/// 全局快捷键服务（基于 RegisterHotKey，应用最小化/后台时依然生效）。
/// </summary>
public static class HotKeyService
{
    public const uint ModAlt = 0x0001;
    public const uint ModControl = 0x0002;
    public const uint ModShift = 0x0004;
    public const uint ModWin = 0x0008;
    private const int WmHotKey = 0x0312;
    private const int HotKeyId = 0xD1A1;

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    /// <summary>
    /// 注册全局快捷键（默认 Ctrl+Alt+D）。
    /// </summary>
    /// <param name="window">宿主窗口（WPF）</param>
    /// <param name="modifiers">修饰键组合（ModAlt | ModControl）</param>
    /// <param name="virtualKey">虚拟键码（'D' = 0x44）</param>
    /// <param name="callback">触发回调</param>
    /// <returns>释放句柄（应用退出时调用 Dispose）</returns>
    public static IDisposable Register(Window window, uint modifiers, uint virtualKey, Action callback)
    {
        var helper = new WindowInteropHelper(window);
        var hwnd = helper.EnsureHandle();
        var source = HwndSource.FromHwnd(hwnd);

        if (!RegisterHotKey(hwnd, HotKeyId, modifiers, virtualKey))
        {
            Logger.Warn("注册全局快捷键失败（可能被其他程序占用）");
            return new HotKeyRegistration(null, null);
        }

        HwndSourceHook hook = (IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled) =>
        {
            if (msg == WmHotKey && w.ToInt32() == HotKeyId)
            {
                handled = true;
                callback();
            }
            return IntPtr.Zero;
        };
        source?.AddHook(hook);

        return new HotKeyRegistration(hwnd, hook);
    }

    private sealed class HotKeyRegistration : IDisposable
    {
        private readonly IntPtr _hwnd;
        private readonly HwndSourceHook? _hook;

        public HotKeyRegistration(IntPtr? hwnd, HwndSourceHook? hook)
        {
            _hwnd = hwnd ?? IntPtr.Zero;
            _hook = hook;
        }

        public void Dispose()
        {
            if (_hwnd != IntPtr.Zero)
            {
                try { UnregisterHotKey(_hwnd, HotKeyId); } catch { /* 忽略 */ }
            }
        }
    }
}
