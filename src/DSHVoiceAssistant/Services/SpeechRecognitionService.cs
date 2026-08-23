using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DSHVoiceAssistant.Models;
using DSHVoiceAssistant.Utils;

namespace DSHVoiceAssistant.Services;

/// <summary>
/// 语音识别实现。
///
/// ⚠️ 重要：百炼 compatible-mode 并不提供 OpenAI 风格的 /audio/transcriptions 文件转写路由
/// （实测所有模型均返回 404），因此本实现改走百炼【原生多模态网关】：
///   POST {host}/api/v1/services/aigc/multimodal-generation/generation
/// 使用工作台可用的 omni 多模态模型（如 qwen3.5-omni-flash），
/// 将 WAV 音频以 base64 data URI 随消息提交，由模型返回转写文本。
/// 该路径已用真实语音验证通过。
/// </summary>
public sealed class SpeechRecognitionService : ISpeechRecognition
{
    private const string TranscriptionSystemPrompt =
        "请把用户音频中的语音内容转写为文字，只输出转写结果，不要任何解释或多余内容。";

    private readonly HttpClient _http;
    private readonly DSHConfig _config;

    public SpeechRecognitionService(DSHConfig config)
    {
        _config = config;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(Math.Max(10, config.ApiTimeoutSeconds)) };
    }

    public async Task<RecognitionResult> RecognizeAsync(byte[] wavData, CancellationToken cancellationToken = default)
    {
        if (wavData == null || wavData.Length < 100)
            return RecognitionResult.Empty("音频数据为空");

        try
        {
            // 从兼容模式地址推导原生网关地址：取 scheme://host，去掉 /compatible-mode/v1 路径
            var nativeHost = new Uri(_config.ApiHost).GetLeftPart(UriPartial.Authority);
            var endpoint = nativeHost + "/api/v1/services/aigc/multimodal-generation/generation";

            var base64 = Convert.ToBase64String(wavData);
            var languageHint = _config.Language?.StartsWith("zh", StringComparison.OrdinalIgnoreCase) == true
                ? "（语音为中文）"
                : "";

            var payload = new
            {
                model = _config.SpeechModel,
                input = new
                {
                    messages = new object[]
                    {
                        new { role = "system", content = new object[] { new { text = TranscriptionSystemPrompt + languageHint } } },
                        new { role = "user", content = new object[] { new { audio = $"data:audio/wav;base64,{base64}" } } }
                    }
                },
                parameters = new { result_format = "message", max_tokens = 500 }
            };

            var body = JsonSerializer.Serialize(payload);

            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _config.ApiKey);
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
                return RecognitionResult.Fail($"语音识别失败 HTTP {(int)response.StatusCode}: {JsonUtils.Truncate(json)}");

            var text = ExtractTranscribedText(json);
            return string.IsNullOrEmpty(text)
                ? RecognitionResult.Empty("未识别到内容")
                : RecognitionResult.Ok(text);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return RecognitionResult.Fail("语音识别请求超时，请检查网络");
        }
        catch (Exception ex)
        {
            return RecognitionResult.Fail("语音识别异常: " + ex.Message);
        }
    }

    /// <summary>
    /// 从多模态网关响应中提取转写文本。
    /// 响应结构：output.choices[0].message.content[]（text 项）。
    /// </summary>
    public static string? ExtractTranscribedText(string responseJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseJson);
            if (!doc.RootElement.TryGetProperty("output", out var output)) return null;
            if (!output.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0) return null;
            if (!choices[0].TryGetProperty("message", out var message)) return null;
            if (!message.TryGetProperty("content", out var content)) return null;

            foreach (var item in content.EnumerateArray())
            {
                if (item.TryGetProperty("text", out var textElement))
                {
                    var text = textElement.GetString()?.Trim();
                    if (!string.IsNullOrEmpty(text)) return text;
                }
            }
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
