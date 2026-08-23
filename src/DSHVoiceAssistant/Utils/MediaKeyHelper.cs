using System.Runtime.InteropServices;

namespace DSHVoiceAssistant.Utils;

/// <summary>
/// 媒体键控制（通过 keybd_event 模拟多媒体按键，全局生效，无需窗口焦点）。
/// </summary>
public static class MediaKeyHelper
{
    // 虚拟键码
    private const byte VK_MEDIA_NEXT_TRACK = 0xB0;
    private const byte VK_MEDIA_PREV_TRACK = 0xB1;
    private const byte VK_MEDIA_STOP = 0xB2;
    private const byte VK_MEDIA_PLAY_PAUSE = 0xB3;
    private const byte VK_VOLUME_MUTE = 0xAD;
    private const byte VK_VOLUME_DOWN = 0xAE;
    private const byte VK_VOLUME_UP = 0xAF;

    private const uint KEYEVENTF_KEYUP = 0x0002;

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    private static void Tap(byte vk)
    {
        keybd_event(vk, 0, 0, UIntPtr.Zero);
        keybd_event(vk, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    /// <summary>
    /// 执行媒体控制动作。支持：play/pause/play_pause/next/prev/stop/volume_up/volume_down/mute（及中文别名）。
    /// 返回是否识别该动作。
    /// </summary>
    public static bool Send(string? action)
    {
        var normalized = (action ?? "").Trim().ToLowerInvariant();
        switch (normalized)
        {
            case "play":
            case "pause":
            case "play_pause":
            case "播放":
            case "暂停":
                Tap(VK_MEDIA_PLAY_PAUSE);
                return true;

            case "next":
            case "下一首":
                Tap(VK_MEDIA_NEXT_TRACK);
                return true;

            case "prev":
            case "previous":
            case "上一首":
                Tap(VK_MEDIA_PREV_TRACK);
                return true;

            case "stop":
            case "停止":
                Tap(VK_MEDIA_STOP);
                return true;

            case "volume_up":
            case "音量加":
                for (var i = 0; i < 3; i++) Tap(VK_VOLUME_UP); // 一次按 3 档，效果更明显
                return true;

            case "volume_down":
            case "音量减":
                for (var i = 0; i < 3; i++) Tap(VK_VOLUME_DOWN);
                return true;

            case "mute":
            case "静音":
                Tap(VK_VOLUME_MUTE);
                return true;

            default:
                return false;
        }
    }
}
