using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DSHVoiceAssistant.Services;

/// <summary>
/// 低级键盘钩子（WH_KEYBOARD_LL）：监听 ESC 键。
/// 仅在回调判定"对话进行中"时消费该按键并触发结束对话；其余情况完全透传
/// （不注册全局热键，避免把系统里所有应用的 ESC 抢走）。
/// 必须在 UI 线程安装（依赖 WPF 消息循环派发钩子回调）。
/// </summary>
public sealed class KeyboardHookService : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int VkEscape = 0x1B;

    private readonly Func<bool> _inConversation;
    private readonly Action _onEscapePressed;
    private readonly HookProc _proc;
    private IntPtr _hook;

    public KeyboardHookService(Func<bool> inConversation, Action onEscapePressed)
    {
        _inConversation = inConversation ?? throw new ArgumentNullException(nameof(inConversation));
        _onEscapePressed = onEscapePressed ?? throw new ArgumentNullException(nameof(onEscapePressed));
        _proc = HookCallback;
    }

    public bool Install()
    {
        if (_hook != IntPtr.Zero) return true;
        using var curProcess = Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule;
        _hook = SetWindowsHookEx(WhKeyboardLl, _proc, GetModuleHandle(curModule?.ModuleName), 0);
        return _hook != IntPtr.Zero;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam.ToInt32() == WmKeyDown)
        {
            var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            if (info.vkCode == VkEscape && _inConversation())
            {
                _onEscapePressed();
                return new IntPtr(1); // 消费 ESC：仅结束对话，不传给其他应用
            }
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}
