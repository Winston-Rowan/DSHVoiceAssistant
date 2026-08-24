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
    /// 解析快捷键组合字符串（如 "Win+F2" / "Ctrl+Alt+D" / "Win+Space"）。
    /// 规则：至少一个修饰键（Ctrl/Alt/Shift/Win）+ 恰好一个主键。
    /// </summary>
    /// <param name="combo">组合字符串（大小写不敏感，'+' 分隔）</param>
    /// <param name="modifiers">解析出的修饰键位组合</param>
    /// <param name="vk">解析出的虚拟键码</param>
    /// <param name="error">解析失败时的中文错误信息</param>
    /// <returns>是否解析成功</returns>
    public static bool TryParse(string combo, out uint modifiers, out uint vk, out string error)
    {
        modifiers = 0;
        vk = 0;
        error = "";

        if (string.IsNullOrWhiteSpace(combo))
        {
            error = "快捷键不能为空";
            return false;
        }

        var parts = combo.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
        {
            error = "格式应为“修饰键+主键”，如 Win+F2";
            return false;
        }

        string? mainKey = null;
        foreach (var part in parts)
        {
            var mod = part.ToUpperInvariant() switch
            {
                "CTRL" or "CONTROL" => ModControl,
                "ALT" => ModAlt,
                "SHIFT" => ModShift,
                "WIN" or "WINDOWS" or "CMD" or "META" => ModWin,
                _ => 0u
            };
            if (mod != 0)
            {
                if ((modifiers & mod) != 0)
                {
                    error = "修饰键重复：" + part;
                    return false;
                }
                modifiers |= mod;
            }
            else
            {
                if (mainKey != null)
                {
                    error = "主键只能有一个：" + part;
                    return false;
                }
                mainKey = part;
            }
        }

        if (modifiers == 0)
        {
            error = "快捷键至少需要一个修饰键（Ctrl/Alt/Shift/Win）";
            return false;
        }
        if (mainKey == null || !TryGetVirtualKey(mainKey, out vk))
        {
            error = "无法识别的主键：" + (mainKey ?? "");
            return false;
        }
        return true;
    }

    /// <summary>把主键名称（如 D / F5 / Space / -）映射为虚拟键码</summary>
    private static bool TryGetVirtualKey(string key, out uint vk)
    {
        vk = 0;
        var k = key.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(k)) return false;

        // 单个字符：字母 / 数字 / 常用符号
        if (k.Length == 1)
        {
            var c = k[0];
            if (c is >= 'A' and <= 'Z') { vk = (uint)c; return true; }
            if (c is >= '0' and <= '9') { vk = (uint)c; return true; }
            switch (c)
            {
                case ' ': vk = 0x20; return true;   // Space
                case '-': vk = 0xBD; return true;
                case '=': vk = 0xBB; return true;
                case '[': vk = 0xDB; return true;
                case ']': vk = 0xDD; return true;
                case '\\': vk = 0xDC; return true;
                case ';': vk = 0xBA; return true;
                case '\'': vk = 0xDE; return true;
                case ',': vk = 0xBC; return true;
                case '.': vk = 0xBE; return true;
                case '/': vk = 0xBF; return true;
                case '`': vk = 0xC0; return true;
            }
            return false;
        }

        // 功能键 F1-F12
        if (k.Length >= 2 && k[0] == 'F' && int.TryParse(k[1..], out var fn) && fn is >= 1 and <= 12)
        {
            vk = (uint)(0x70 + fn - 1); // F1=0x70 … F12=0x7B
            return true;
        }

        vk = k switch
        {
            "SPACE" => 0x20,
            "ENTER" => 0x0D,
            "TAB" => 0x09,
            "ESC" or "ESCAPE" => 0x1B,
            "BACK" or "BACKSPACE" => 0x08,
            "DEL" or "DELETE" => 0x2E,
            "INS" or "INSERT" => 0x2D,
            "UP" => 0x26,
            "DOWN" => 0x28,
            "LEFT" => 0x25,
            "RIGHT" => 0x27,
            "HOME" => 0x24,
            "END" => 0x23,
            "PGUP" or "PAGEUP" => 0x21,
            "PGDN" or "PAGEDOWN" => 0x22,
            "PAUSE" => 0x13,
            "PRTSC" or "PRINTSCREEN" => 0x2C,
            _ => 0
        };
        return vk != 0;
    }

    /// <summary>
    /// 注册全局快捷键（默认 Win+F2）。
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
