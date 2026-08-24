using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DSHVoiceAssistant.Models;
using DSHVoiceAssistant.Utils;

namespace DSHVoiceAssistant.Services;

/// <summary>
/// ⭐ DSH 指令执行服务（核心）：
/// 调用百炼 compatible-mode /chat/completions 接口，注入"DSH 系统角色"提示词，
/// 使大模型扮演 DSH 执行引擎——负责全部指令理解与决策，返回结构化 JSON 指令，
/// 本地只做语音采集、展示与动作落地。
/// </summary>
public sealed class DSHCommandService : IDSHCommandService
{
    /// <summary>
    /// DSH 系统角色提示词模板。
    /// {NAME} 运行时替换为助手身份（配置 AssistantName，如"梁文峰"）；
    /// {WAKE_WORD} 替换为唤醒词（如"老梁"）；{GIT_PATH} 替换为项目目录；
    /// {PERMISSION} 按 PermissionMode（full/workspace/readonly）注入权限说明；
    /// {REPORT_LANG} 按 ReportLanguage 注入强制汇报语言。
    /// </summary>
    private const string SystemPromptTemplate = """
你是{NAME}，一个Windows桌面语音助手的执行引擎。用户用唤醒词"{WAKE_WORD}"（或"梁总"等称呼）呼叫你，但你真正的名字是{NAME}——如果用户问"你叫什么名字"或"你是谁"，回答"我叫{NAME}"；用户说出"{WAKE_WORD}"或直接下达指令时都是在和你说话。
你的任务是将用户的自然语言指令转换为可执行的格式化命令。

{PERMISSION}

【汇报规则】（强制）
- 执行复杂操作（脚本、git、批量文件处理、系统更改、安装软件等）时：**不要在过程中播报中间推理或中间步骤**（禁止"我正在分析…""我先…""准备…"这类内容），只在操作**全部执行结束后**统一汇报结果（成功/失败/结果摘要）
- 简单操作（打开应用、切歌、查询等）可以一句简短确认
- 所有语音回复必须使用指定语言：{REPORT_LANG}，**不得混用其他语言**

【指令转换规则】
1. 打开应用/软件 → action: "open_app"（target 只用于系统自带程序或已知可执行文件名，如 notepad.exe、calc.exe；不要猜不存在的文件名）
2. 搜索信息 → action: "web_search"
3. 系统操作(关机/重启/锁屏/睡眠) → action: "system_command"
4. 纯对话回复 → action: "text_reply"
5. 媒体控制(播放/暂停/下一首/音量) → action: "control_media"
6. 打开网页 → action: "open_url"
7. 文件操作 → action: "file_operation"（params.operation: open/reveal/delete）
8. 自定义命令/脚本 → action: "custom_script"（target: 任意 PowerShell 命令或命令序列——git 操作、文件批处理、软件安装、系统管理等通用执行通道）
9. 打开游戏 → action: "open_game"（target: 游戏名称，同时给出中文名和英文名，用 | 分隔，如 "战神5|God of War Ragnarök"；本地会自动从开始菜单/Steam/Epic 查找）
10. 闹钟/提醒/倒计时 → action: "reminder"（params: minutes="N"（N 分钟后）或 at="HH:mm"（具体时刻）、message="提醒内容"）
11. 全屏截图 → action: "screenshot"
12. 剪贴板 → action: "clipboard"（params.operation: get=读出并播报 / set=写入，set 时 target 为要写入的文本）

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
用户: "推送到GitHub" → {"action":"custom_script","target":"git -C \"{GIT_PATH}\" add -A; git -C \"{GIT_PATH}\" commit -m \"update\"; git -C \"{GIT_PATH}\" push","response":"正在推送代码到 GitHub"}
用户: "看看磁盘空间" → {"action":"custom_script","target":"Get-PSDrive -PSProvider FileSystem | Select-Object Name,Used,Free | Format-Table","response":"正在查询磁盘空间"}
用户: "5分钟后提醒我喝水" → {"action":"reminder","target":"","params":{"minutes":"5","message":"该喝水啦"},"response":"好的，5分钟后提醒您喝水"}
用户: "晚上8点提醒我开会" → {"action":"reminder","target":"","params":{"at":"20:00","message":"该开会了"},"response":"好的，晚上8点提醒您开会"}
用户: "截个图" → {"action":"screenshot","target":"","response":"正在截图"}
用户: "把这段话复制到剪贴板" → {"action":"clipboard","target":"这段话","params":{"operation":"set"},"response":"已复制到剪贴板"}
用户: "看看剪贴板里有什么" → {"action":"clipboard","target":"","params":{"operation":"get"},"response":"正在读取剪贴板内容"}

【注意事项】
- 保持回复简洁友好，response 字段会被语音朗读
- custom_script 执行后会返回真实输出，把结果摘要写进 response（如推送成功、失败原因）
- 对于不确定的指令，用 text_reply 请用户再说一次
- open_app 的 target 只给已知的、确定存在于系统 PATH 的程序名或完整路径，不要编造文件名；
  不确定安装位置的软件/游戏一律用 open_game（本地会智能查找）
- open_game 的 target 用 | 分隔多个可能的名称（中文名|英文名）
""";

    /// <summary>
    /// 按权限模式（full/workspace/readonly）生成【权限说明】段落。
    /// 与执行器（CommandProcessor）的硬性拦截保持一致。
    /// </summary>
    private string BuildPermissionSection()
    {
        var workspace = string.IsNullOrWhiteSpace(_config.GitProjectPath) ? "" : _config.GitProjectPath.Trim().TrimEnd('\\');
        return _config.PermissionMode?.Trim().ToLowerInvariant() switch
        {
            "readonly" => """
            【权限说明】（当前模式：只读）
            - 只允许：读取信息、打开应用/网页、搜索、媒体控制、纯对话回复
            - 禁止：执行任何命令/脚本（custom_script）、写入/删除/移动文件、关机/重启/休眠/注销等系统更改操作——即使下方示例中出现也一律不得执行
            - 用户要求执行类操作时，用 text_reply 说明当前为只读模式，建议切换到更高权限
            """,
            "workspace" => $"""
            【权限说明】（当前模式：工作区受限）
            - 允许：打开应用/网页、搜索、媒体控制、纯对话回复
            - 文件操作与命令执行（含 git 提交推送）仅限工作区目录：{workspace}
            - 工作区之外的写入/删除/命令执行：需用户明确要求才可执行，否则用 text_reply 说明限制
            """,
            _ => $"""
            【权限说明】（当前模式：完全访问）
            - 你拥有全局完整权限（与 DSH 代理一致）：用户要求执行的操作全部直接执行，不要拒绝，也不要建议用户手动操作——包括 git 提交/推送、运行任意命令与脚本、文件读写/移动/删除、安装软件、系统设置、网络请求、媒体控制等
            - 删除文件等不可逆操作：用户明确要求即可执行（文件删除时 params 携带 confirm="true"）
            - 常用项目目录：{workspace}（用户说"推送到GitHub/推送代码/提交代码/git提交"时在此目录执行 git 提交并推送）
            - 命令/脚本执行后，把真实结果（成功/失败/输出摘要）写进 response 供语音播报
            """
        };
    }

    /// <summary>按当前配置构建系统提示词：身份（AssistantName）与被呼叫方式（唤醒词）分离</summary>
    private string BuildSystemPrompt()
    {
        var name = string.IsNullOrWhiteSpace(_config.AssistantName)
            ? (string.IsNullOrWhiteSpace(_config.WakeWord) ? "DSH 语音助手" : _config.WakeWord.Trim())
            : _config.AssistantName.Trim();
        var wakeWord = string.IsNullOrWhiteSpace(_config.WakeWord) ? name : _config.WakeWord.Trim();
        var gitPath = string.IsNullOrWhiteSpace(_config.GitProjectPath) ? "" : _config.GitProjectPath.Trim().TrimEnd('\\');
        var reportLang = (_config.ReportLanguage ?? "").Trim().ToLowerInvariant() switch
        {
            "en" or "english" => "English（英文）",
            _ => "中文（普通话）"
        };
        return SystemPromptTemplate
            .Replace("{NAME}", name)
            .Replace("{WAKE_WORD}", wakeWord)
            .Replace("{GIT_PATH}", gitPath)
            .Replace("{PERMISSION}", BuildPermissionSection())
            .Replace("{REPORT_LANG}", reportLang);
    }

    private static readonly JsonSerializerOptions RequestOptions = new();
    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _http;
    private readonly DSHConfig _config;

    // 多轮对话记忆（带长度管理：保留最近 N 轮，超限自动淘汰最旧，防止上下文无限增长）
    private readonly object _historyLock = new();
    private readonly List<DSHChatMessage> _history = [];

    public DSHCommandService(DSHConfig config)
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
            Messages = BuildMessages(BuildSystemPrompt(), _history, userCommand, _config.DshHistoryRounds)
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

                var command = DSHCommandParser.Parse(content);
                Logger.Info("DSH 指令: " + command);

                // 记录本轮对话（用户指令 + DSH 原始回复），供下一轮引用
                if (_config.DshHistoryRounds > 0 && command.Success)
                {
                    lock (_historyLock)
                    {
                        _history.Add(new DSHChatMessage { Role = "user", Content = userCommand });
                        _history.Add(new DSHChatMessage { Role = "assistant", Content = content });
                        TrimHistory(_history, _config.DshHistoryRounds);
                    }
                }
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

    /// <summary>
    /// 组装请求消息：system + 最近对话历史 + 当前指令。
    /// 历史按轮数限制（1 轮 = user+assistant 两条），超出丢弃最旧；同时按字符总量兜底截断。
    /// 纯函数，便于单元测试。
    /// </summary>
    public static List<DSHChatMessage> BuildMessages(
        string systemPrompt, List<DSHChatMessage> history, string userCommand, int maxRounds)
    {
        var messages = new List<DSHChatMessage>
        {
            new() { Role = "system", Content = systemPrompt }
        };

        if (maxRounds > 0 && history.Count > 0)
        {
            var historyCopy = new List<DSHChatMessage>(history);
            TrimHistory(historyCopy, maxRounds);
            messages.AddRange(historyCopy);
        }

        messages.Add(new DSHChatMessage { Role = "user", Content = userCommand });
        return messages;
    }

    /// <summary>裁剪历史：限制轮数（2 条/轮）与总字符量（约 8000 字符，防止超长）</summary>
    private static void TrimHistory(List<DSHChatMessage> history, int maxRounds)
    {
        const int maxTotalChars = 8000;
        while (history.Count > maxRounds * 2)
            history.RemoveAt(0);
        var total = history.Sum(m => m.Content.Length);
        while (history.Count > 2 && total > maxTotalChars)
        {
            total -= history[0].Content.Length;
            history.RemoveAt(0);
        }
    }
}
