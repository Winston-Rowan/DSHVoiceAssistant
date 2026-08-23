using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DivaVoiceAssistant.Models;
using DivaVoiceAssistant.Utils;

namespace DivaVoiceAssistant.Services;

/// <summary>
/// ⭐ DSH 指令执行服务（核心）：
/// 调用百炼 compatible-mode /chat/completions 接口，注入"DSH 系统角色"提示词，
/// 使大模型扮演 DSH 执行引擎——负责全部指令理解与决策，返回结构化 JSON 指令，
/// 本地只做语音采集、展示与动作落地。
/// </summary>
public sealed class DSHCommandService : IDSHCommandService
{
    /// <summary>DSH 系统角色提示词（即需求文档中的"关键提示词"）</summary>
    private const string SystemPrompt = """
你是Diva，一个Windows桌面语音助手的执行引擎。你的任务是将用户的自然语言指令转换为可执行的格式化命令。

【指令转换规则】
1. 打开应用/软件 → action: "open_app"（target 只用于系统自带程序或已知可执行文件名，如 notepad.exe、calc.exe；不要猜不存在的文件名）
2. 搜索信息 → action: "web_search"
3. 系统操作(关机/重启/锁屏/睡眠) → action: "system_command"
4. 纯对话回复 → action: "text_reply"
5. 媒体控制(播放/暂停/下一首/音量) → action: "control_media"
6. 打开网页 → action: "open_url"
7. 文件操作 → action: "file_operation"（params.operation: open/reveal/delete）
8. 自定义脚本 → action: "custom_script"
9. 打开游戏 → action: "open_game"（target: 游戏名称，同时给出中文名和英文名，用 | 分隔，如 "战神5|God of War Ragnarök"；本地会自动从开始菜单/Steam/Epic 查找）

【输出格式】
只输出一个JSON对象，不要输出任何多余文字、解释或Markdown代码块。字段：
- action: 字符串，上述动作类型
- target: 字符串，动作的目标对象
- params: 可选对象，附加参数（键值对，值必须是字符串）
- response: 字符串，给用户的语音回复内容（简洁友好，10~30字）

【示例】
用户: "打开浏览器" → {"action":"open_app","target":"chrome.exe","response":"正在为您打开浏览器"}
用户: "帮我搜索红烧肉的做法" → {"action":"web_search","target":"红烧肉的做法","response":"正在为您搜索红烧肉的做法"}
用户: "关闭电脑" → {"action":"system_command","target":"shutdown","response":"正在准备关机..."}
用户: "今天心情不好" → {"action":"text_reply","target":"","response":"主人别难过，我陪您聊聊天吧"}
用户: "下一首" → {"action":"control_media","target":"next","response":"好的，切到下一首"}
用户: "打开百度首页" → {"action":"open_url","target":"https://www.baidu.com","response":"正在为您打开网页"}
用户: "打开战神5" → {"action":"open_game","target":"战神5|God of War Ragnarök","response":"正在为您启动战神5"}
用户: "帮我打开微信" → {"action":"open_game","target":"微信|WeChat","response":"正在为您打开微信"}

【注意事项】
- 保持回复简洁友好，response 字段会被语音朗读
- 对于不确定的指令，用 text_reply 请用户再说一次
- open_app 的 target 只给已知的、确定存在于系统 PATH 的程序名或完整路径，不要编造文件名；
  不确定安装位置的软件/游戏一律用 open_game（本地会智能查找）
- open_game 的 target 用 | 分隔多个可能的名称（中文名|英文名）
""";

    private static readonly JsonSerializerOptions RequestOptions = new();
    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _http;
    private readonly DivaConfig _config;

    public DSHCommandService(DivaConfig config)
    {
        _config = config;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(Math.Max(10, config.ApiTimeoutSeconds)) };
    }

    public async Task<DSHResponse> ExecuteAsync(string userCommand, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userCommand)) return DSHResponse.Failure("指令文本为空");

        var payload = new DSHChatRequest
        {
            Model = _config.DSHModel,
            Temperature = 0.3,
            MaxTokens = 2000, // 推理模型（deepseek-v4-flash）需要给思考过程留足 token
            Messages =
            {
                new DSHChatMessage { Role = "system", Content = SystemPrompt },
                new DSHChatMessage { Role = "user", Content = userCommand }
            }
        };
        var body = JsonSerializer.Serialize(payload, RequestOptions);

        // 简单重试：网络异常或 5xx 时重试一次
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, _config.ApiHost.TrimEnd('/') + "/chat/completions");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _config.ApiKey);
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");

                using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
                var responseText = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    if (attempt == 0 && (int)response.StatusCode >= 500)
                    {
                        await Task.Delay(1000, cancellationToken);
                        continue;
                    }
                    return DSHResponse.Failure($"DSH 调用失败 HTTP {(int)response.StatusCode}: {JsonUtils.Truncate(responseText)}", responseText);
                }

                var chat = JsonSerializer.Deserialize<DSHChatResponse>(responseText, ReadOptions);
                var content = chat?.Choices?.FirstOrDefault()?.Message?.Content ?? "";
                Logger.Info("DSH 返回: " + JsonUtils.Truncate(content, 400));

                var command = DivaCommandParser.Parse(content);
                Logger.Info("DSH 指令: " + command);
                return command;
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return DSHResponse.Failure("DSH 请求超时，请检查网络");
            }
            catch (Exception ex)
            {
                if (attempt == 0)
                {
                    await Task.Delay(1000, cancellationToken);
                    continue;
                }
                return DSHResponse.Failure("DSH 调用异常: " + ex.Message);
            }
        }

        return DSHResponse.Failure("DSH 调用失败");
    }
}
