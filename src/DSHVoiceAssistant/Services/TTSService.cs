using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Speech.Synthesis;
using System.Text;
using System.Text.Json;
using DSHVoiceAssistant.Models;
using DSHVoiceAssistant.Utils;
using NAudio.Wave;

namespace DSHVoiceAssistant.Services;

/// <summary>
/// TTS 语音合成：
/// - 默认（TtsMode=cloud）走百炼云端神经语音（qwen3-tts-flash 等，自然音色，听感接近豆包），
///   合成成功后下载 WAV 用 NAudio 播放；失败/离线时自动回退本地 SAPI 语音。
/// - 也可配置为 local 仅用 Windows 本地语音。
///
/// 云端 TTS 调用（已实测验证）：
///   POST {原生Host}/api/v1/services/aigc/multimodal-generation/generation
///   {"model":"qwen3-tts-flash","input":{"text":"..."},"parameters":{"voice":"Cherry","response_format":{"format":"wav"}}}
///   响应 output.audio.url → GET 下载 WAV。
/// </summary>
public sealed class TTSService : ITTSService, IDisposable
{
    private readonly DSHConfig _config;
    private readonly HttpClient _http;
    private readonly SpeechSynthesizer _localSynth = new();
    private readonly object _gate = new();

    private WaveOutEvent? _waveOut;
    private int _generation;
    private bool _speaking;

    public TTSService(DSHConfig config)
    {
        _config = config;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(Math.Max(10, config.ApiTimeoutSeconds)) };

        // 本地语音初始化（本地模式/云端回退时使用）
        try
        {
            var zhVoice = _localSynth.GetInstalledVoices().FirstOrDefault(v =>
                v.VoiceInfo?.Culture?.Name?.StartsWith("zh", StringComparison.OrdinalIgnoreCase) == true ||
                v.VoiceInfo?.Name?.Contains("Huihui", StringComparison.OrdinalIgnoreCase) == true ||
                v.VoiceInfo?.Name?.Contains("Xiaoxiao", StringComparison.OrdinalIgnoreCase) == true ||
                v.VoiceInfo?.Name?.Contains("Kangkang", StringComparison.OrdinalIgnoreCase) == true);
            if (zhVoice?.VoiceInfo != null) _localSynth.SelectVoice(zhVoice.VoiceInfo.Name);
            _localSynth.Rate = Math.Clamp(config.TtsRate, -10, 10);
            _localSynth.Volume = Math.Clamp(config.TtsVolume, 0, 100);
        }
        catch (Exception ex)
        {
            Logger.Warn("本地 TTS 初始化失败（可能缺少语音包）: " + ex.Message);
        }
    }

    public bool IsSpeaking => _speaking;

    public Task SpeakAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text)) return Task.CompletedTask;
        var content = text.Length > 800 ? text[..800] : text;

        return _config.TtsMode == "local"
            ? SpeakLocalAsync(content)
            : SpeakCloudWithFallbackAsync(content, cancellationToken);
    }

    public void Stop()
    {
        Interlocked.Increment(ref _generation);
        lock (_gate)
        {
            try { _waveOut?.Stop(); } catch { /* 忽略 */ }
            try { _localSynth.SpeakAsyncCancelAll(); } catch { /* 忽略 */ }
        }
    }

    // ---------- 云端 TTS ----------

    private async Task SpeakCloudWithFallbackAsync(string text, CancellationToken ct)
    {
        try
        {
            await SpeakCloudAsync(text, ct);
        }
        catch (Exception ex)
        {
            Logger.Warn("云端 TTS 失败，回退本地语音: " + ex.Message);
            await SpeakLocalAsync(text);
        }
    }

    private async Task SpeakCloudAsync(string text, CancellationToken ct)
    {
        var nativeHost = new Uri(_config.ApiHost).GetLeftPart(UriPartial.Authority);
        var endpoint = nativeHost + "/api/v1/services/aigc/multimodal-generation/generation";

        var payload = new
        {
            model = _config.TtsModel,
            input = new { text },
            parameters = new { voice = _config.TtsVoice, response_format = new { format = "wav" } }
        };
        var body = JsonSerializer.Serialize(payload);

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _config.ApiKey);
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"TTS 合成失败 HTTP {(int)response.StatusCode}: {JsonUtils.Truncate(json)}");

        var url = ExtractAudioUrl(json)
                  ?? throw new InvalidOperationException("TTS 合成响应缺少音频地址");
        Logger.Info("云端 TTS 合成完成，下载音频: " + JsonUtils.Truncate(url, 80));

        var wav = await _http.GetByteArrayAsync(url, ct);
        if (wav == null || wav.Length < 100)
            throw new InvalidOperationException("TTS 音频数据为空");
        Logger.Info($"云端 TTS 音频 {wav.Length} 字节，开始播放（音色 {_config.TtsVoice}）");

        await PlayWavAsync(wav);
    }

    /// <summary>用 NAudio 播放 WAV（可被 Stop 打断）</summary>
    private Task PlayWavAsync(byte[] wav)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var gen = Interlocked.Increment(ref _generation);

        var ms = new MemoryStream(wav);
        var reader = new WaveFileReader(ms);
        var output = new WaveOutEvent();

        output.PlaybackStopped += (_, e) =>
        {
            var current = Volatile.Read(ref _generation);
            lock (_gate)
            {
                if (ReferenceEquals(_waveOut, output)) _waveOut = null;
                _speaking = false;
            }
            try { reader.Dispose(); } catch { /* 忽略 */ }
            try { ms.Dispose(); } catch { /* 忽略 */ }
            try { output.Dispose(); } catch { /* 忽略 */ }

            if (gen == current)
            {
                if (e.Exception != null) tcs.TrySetException(e.Exception);
                else tcs.TrySetResult(true);
            }
            else
            {
                tcs.TrySetResult(false); // 被 Stop 打断
            }
        };

        lock (_gate)
        {
            try { _waveOut?.Stop(); } catch { /* 忽略 */ } // 打断上一段
            _waveOut = output;
            _speaking = true;
            output.Init(reader);
            output.Play();
        }
        return tcs.Task;
    }

    /// <summary>从 TTS 合成响应中提取音频下载地址（output.audio.url）</summary>
    public static string? ExtractAudioUrl(string responseJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseJson);
            if (!doc.RootElement.TryGetProperty("output", out var output)) return null;
            if (!output.TryGetProperty("audio", out var audio)) return null;
            if (audio.TryGetProperty("url", out var urlElement))
            {
                var url = urlElement.GetString();
                if (!string.IsNullOrWhiteSpace(url)) return url;
            }
            // 某些响应可能内嵌 base64 音频（data 字段，形如 data:audio/wav;base64,...）
            if (audio.TryGetProperty("data", out var dataElement))
            {
                var data = dataElement.GetString();
                if (!string.IsNullOrWhiteSpace(data) && data.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                    return data;
            }
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // ---------- 本地 SAPI ----------

    private Task SpeakLocalAsync(string text)
    {
        var gen = Interlocked.Increment(ref _generation);
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnSpeakCompleted(object? sender, SpeakCompletedEventArgs e)
        {
            if (gen != Volatile.Read(ref _generation)) return; // 旧语音的取消事件
            _localSynth.SpeakCompleted -= OnSpeakCompleted;
            lock (_gate) _speaking = false;
            if (e.Error != null) tcs.TrySetException(e.Error);
            else tcs.TrySetResult(!e.Cancelled);
        }

        _localSynth.SpeakCompleted += OnSpeakCompleted;
        lock (_gate)
        {
            _speaking = true;
            try
            {
                _localSynth.SpeakAsyncCancelAll();
                _localSynth.SpeakAsync(text);
            }
            catch (Exception ex)
            {
                _localSynth.SpeakCompleted -= OnSpeakCompleted;
                lock (_gate) _speaking = false;
                tcs.TrySetException(ex);
            }
        }
        return tcs.Task;
    }

    public void Dispose()
    {
        try { Stop(); } catch { /* 忽略 */ }
        try { _localSynth.Dispose(); } catch { /* 忽略 */ }
    }
}
