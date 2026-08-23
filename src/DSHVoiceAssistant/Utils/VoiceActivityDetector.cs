namespace DSHVoiceAssistant.Utils;

/// <summary>VAD（语音活动检测）参数</summary>
public sealed class VADSettings
{
    /// <summary>有声判定阈值（归一化 RMS）</summary>
    public double StartThreshold { get; set; } = 0.02;

    /// <summary>连续多少个有声帧后才判定“开始说话”（防误触发）</summary>
    public int StartFrames { get; set; } = 3;

    /// <summary>静音持续多久判定“一句话结束”</summary>
    public TimeSpan EndSilence { get; set; } = TimeSpan.FromMilliseconds(1200);

    /// <summary>一句话最短时长，过短视为噪音</summary>
    public TimeSpan MinSpeech { get; set; } = TimeSpan.FromMilliseconds(400);

    /// <summary>一句话最长时长，超时强制结束</summary>
    public TimeSpan MaxSpeech { get; set; } = TimeSpan.FromMilliseconds(15000);
}

/// <summary>
/// 基于能量（RMS）的轻量级语音活动检测器。
/// 由音频回调线程逐帧 Feed，事件也在该线程触发；调用方自行处理线程调度。
/// </summary>
public sealed class VoiceActivityDetector
{
    private readonly VADSettings _settings;
    private int _voicedFrames;
    private bool _inSpeech;
    private DateTime _speechStart;
    private DateTime _lastVoiced;

    public VoiceActivityDetector(VADSettings settings) => _settings = settings;

    /// <summary>是否处于说话状态</summary>
    public bool IsInSpeech => _inSpeech;

    /// <summary>当前说话片段已持续时长（未在说话时为 0）</summary>
    public TimeSpan CurrentSpeechDuration => _inSpeech ? DateTime.UtcNow - _speechStart : TimeSpan.Zero;

    /// <summary>开始说话（触发一次）</summary>
    public event Action? SpeechStarted;

    /// <summary>一句话结束（满足最小时长才触发）</summary>
    public event Action? SpeechEnded;

    /// <summary>逐帧喂入归一化音量（每帧约 100ms）</summary>
    public void Feed(float rms)
    {
        var now = DateTime.UtcNow;
        var voiced = rms >= _settings.StartThreshold;

        if (!_inSpeech)
        {
            _voicedFrames = voiced ? _voicedFrames + 1 : 0;
            if (_voicedFrames >= _settings.StartFrames)
            {
                _inSpeech = true;
                _speechStart = now;
                _lastVoiced = now;
                SpeechStarted?.Invoke();
            }
        }
        else
        {
            if (voiced)
            {
                _lastVoiced = now;
            }
            else if (now - _lastVoiced >= _settings.EndSilence)
            {
                EndSpeech(now);
            }
            else if (now - _speechStart >= _settings.MaxSpeech)
            {
                EndSpeech(now);
            }
        }
    }

    /// <summary>重置状态（静音/暂停采集时调用）</summary>
    public void Reset()
    {
        _voicedFrames = 0;
        _inSpeech = false;
    }

    private void EndSpeech(DateTime now)
    {
        _inSpeech = false;
        _voicedFrames = 0;
        if (now - _speechStart >= _settings.MinSpeech)
        {
            SpeechEnded?.Invoke();
        }
    }
}
