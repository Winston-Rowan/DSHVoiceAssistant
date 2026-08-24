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
    /// {REPORT_LANG} 按 ReportLanguage 注入强制汇报语言；
    /// {PERSONA} 按 AssistantPersona 注入人格（默认梁文峰，自定义时经 DSH 优化）。
    /// </summary>
    private const string SystemPromptTemplate = """
你是{NAME}，Windows 桌面语音助手执行引擎。用户用唤醒词"{WAKE_WORD}"呼叫你。你的任务：把用户指令转换为可执行 JSON。

【人格】{PERSONA}

{PERMISSION}

【汇报】复杂操作（脚本/git/批量/系统更改/安装软件）执行中禁止播报推理或中间步骤，操作结束后统一汇报结果（成功/失败/摘要）；简单操作可一句确认。所有回复使用{REPORT_LANG}，不得混用。

【动作】
1 open_app：打开程序（target 仅限系统自带或确定存在的 exe，如 notepad.exe；不要编造文件名）
2 web_search：网络搜索
3 system_command：关机/重启/锁屏/睡眠等系统操作
4 text_reply：纯对话回复
5 control_media：媒体控制（play/pause/next/prev/音量）
6 open_url：打开网页
7 file_operation：文件操作（params.operation: open/reveal/delete）
8 custom_script：执行任意 PowerShell 命令（git 推送、批处理、安装软件等通用通道）
9 open_game：打开游戏/软件（target 用 中文名|英文名 分隔，本地智能查找）
10 reminder：闹钟提醒（params: minutes=N 或 at=HH:mm，message=提醒内容）
11 screenshot：全屏截图
12 clipboard：剪贴板（params.operation: get/set，set 时 target=要写入的文本）

【输出】只输出一个 JSON 对象，禁止多余文字、解释或 Markdown。字段：action、target、params（可选）、response（10~30 字，会被语音朗读）。

【示例】
"打开浏览器"→{"action":"open_app","target":"chrome.exe","response":"正在为您打开浏览器"}
"搜索红烧肉做法"→{"action":"web_search","target":"红烧肉的做法","response":"正在为您搜索"}
"关闭电脑"→{"action":"system_command","target":"shutdown","response":"正在准备关机"}
"下一首"→{"action":"control_media","target":"next","response":"好的，切到下一首"}
"打开战神5"→{"action":"open_game","target":"战神5|God of War Ragnarök","response":"正在为您启动战神5"}
"推送到GitHub"→{"action":"custom_script","target":"git -C \"{GIT_PATH}\" add -A; git -C \"{GIT_PATH}\" commit -m \"update\"; git -C \"{GIT_PATH}\" push","response":"正在推送代码"}
"5分钟后提醒我喝水"→{"action":"reminder","target":"","params":{"minutes":"5","message":"该喝水啦"},"response":"好的，5分钟后提醒您"}
"截个图"→{"action":"screenshot","target":"","response":"正在截图"}
"复制这句话"→{"action":"clipboard","target":"这句话","params":{"operation":"set"},"response":"已复制"}

【注意】custom_script 执行后把真实结果写进 response；不确定时用 text_reply 请用户重说；open_app 只给确定存在的程序，软件/游戏一律 open_game。
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
    private string BuildSystemPrompt(string persona)
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
            .Replace("{REPORT_LANG}", reportLang)
            .Replace("{PERSONA}", persona);
    }

    // ---------- 人格：默认梁文峰，自定义时由 DSH 优化后注入 ----------

    private readonly object _personaLock = new();
    private string? _effectivePersona;
    private string? _personaSource;

    /// <summary>
    /// 获取生效人格：空白/等于默认 → 直接用默认人格；自定义 → 调用 DSH 优化一次并缓存
    /// （源文本未变化则不重复调用）。失败时回退用户原文。
    /// </summary>
    private async Task<string> GetEffectivePersonaAsync(CancellationToken cancellationToken)
    {
        var raw = string.IsNullOrWhiteSpace(_config.AssistantPersona)
            ? DSHConfig.DefaultPersona
            : _config.AssistantPersona.Trim();

        lock (_personaLock)
        {
            if (_effectivePersona != null && _personaSource == raw) return _effectivePersona;
        }

        string? optimized = null;
        // 默认人格本身已是精修版本，无需优化
        if (!string.Equals(raw, DSHConfig.DefaultPersona, StringComparison.Ordinal))
        {
            optimized = await OptimizePersonaAsync(raw, cancellationToken);
            if (!string.IsNullOrWhiteSpace(optimized))
            {
                Logger.Info("人格已由 DSH 优化后注入");
            }
            else
            {
                Logger.Warn("人格优化失败，回退用户原文");
            }
        }

        var effective = optimized ?? raw;
        lock (_personaLock)
        {
            _effectivePersona = effective;
            _personaSource = raw;
        }
        return effective;
    }

    /// <summary>调用 DSH 优化人格描述：保留核心人设，语言精炼可执行，直接输出结果</summary>
    private async Task<string?> OptimizePersonaAsync(string persona, CancellationToken cancellationToken)
    {
        try
        {
            var (dshHost, dshKey) = SelectDshEndpoint(_config);
            var payload = new DSHChatRequest
            {
                Model = _config.DSHModel,
                Temperature = 0.7,
                MaxTokens = 500,
                Messages =
                [
                    new() { Role = "system", Content =
                        "你是人格描述优化器。把用户写的人格描述优化成适合注入语音助手系统提示词的版本：" +
                        "保留核心人设与性格，语言精炼、具体、可执行，120 字以内，用中文直接输出优化结果，不要任何解释、前缀或 Markdown。" },
                    new() { Role = "user", Content = persona }
                ]
            };
            using var request = new HttpRequestMessage(HttpMethod.Post, dshHost + "/chat/completions");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", dshKey);
            request.Content = new StringContent(JsonSerializer.Serialize(payload, RequestOptions), Encoding.UTF8, "application/json");
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
            var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode) return null;
            var chat = JsonSerializer.Deserialize<DSHChatResponse>(responseText, ReadOptions);
            var content = chat?.Choices?.FirstOrDefault()?.Message?.Content?.Trim();
            return string.IsNullOrWhiteSpace(content) ? null : content;
        }
        catch (Exception ex)
        {
            Logger.Warn("人格优化调用异常: " + ex.Message);
            return null;
        }
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

    /// <summary>
    /// 选择指令引擎端点：填写了 DSH API 密钥 → 走 DSH API（DeepSeek 官方）；
    /// 否则回退百炼（与语音识别共用密钥）。
    /// </summary>
    public static (string Host, string Key) SelectDshEndpoint(DSHConfig config)
    {
        var dshKey = config.DshApiKey?.Trim() ?? "";
        if (dshKey.Length > 0)
        {
            var dshHost = string.IsNullOrWhiteSpace(config.DshApiHost)
                ? "https://api.deepseek.com/v1"
                : config.DshApiHost.Trim().TrimEnd('/');
            return (dshHost, dshKey);
        }
        return (config.ApiHost.Trim().TrimEnd('/'), config.ApiKey.Trim());
    }

    public async Task<DSHResponse> ExecuteAsync(string userCommand, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userCommand)) return DSHResponse.Failure("指令文本为空");

        var persona = await GetEffectivePersonaAsync(cancellationToken);
        var payload = new DSHChatRequest
        {
            Model = _config.DSHModel,
            Temperature = 0.3,
            MaxTokens = 4000, // 推理模型需要留足思考+输出空间（曾出现思考耗尽导致空回复）
            Messages = BuildMessages(BuildSystemPrompt(persona), _history, userCommand, _config.DshHistoryRounds)
        };
        var body = JsonSerializer.Serialize(payload, RequestOptions);

        // 简单重试：网络异常或 5xx 时重试一次
        var (dshHost, dshKey) = SelectDshEndpoint(_config);
        Logger.Info($"DSH 引擎请求 → {dshHost}/chat/completions（模型 {_config.DSHModel}）");
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, dshHost + "/chat/completions");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", dshKey);
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
                if (string.IsNullOrWhiteSpace(content))
                {
                    // 诊断：模型返回空内容时记录原始响应（含 reasoning_content / finish_reason）
                    Logger.Warn("DSH 返回空内容，原始响应: " + JsonUtils.Truncate(responseText, 800));
                }
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
