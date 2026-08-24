using System.Text.Json.Serialization;

namespace DSHVoiceAssistant.Models;

/// <summary>
/// DSH 应用配置（对应 appsettings.json 的 DSHConfig 节点）。
/// </summary>
public sealed class DSHConfig
{
    /// <summary>百炼工作台 API 密钥（也可通过环境变量 DSH_API_KEY 覆盖）</summary>
    [JsonPropertyName("ApiKey")]
    public string ApiKey { get; set; } = "";

    /// <summary>百炼 compatible-mode API 地址（不带末尾斜杠）</summary>
    [JsonPropertyName("ApiHost")]
    public string ApiHost { get; set; } = "https://ws-fdenp5x6eqwcud4u.cn-beijing.maas.aliyuncs.com/compatible-mode/v1";

    /// <summary>语音识别模型（百炼工作台可用的 omni 多模态模型，如 qwen3.5-omni-flash；转写走原生多模态网关）</summary>
    [JsonPropertyName("SpeechModel")]
    public string SpeechModel { get; set; } = "qwen3.5-omni-flash";

    /// <summary>DSH 指令模型（与 DSH 同款推理模型，工作台可用模型，如 deepseek-v4-flash）</summary>
    [JsonPropertyName("DSHModel")]
    public string DSHModel { get; set; } = "deepseek-v4-flash";

    /// <summary>识别语言（zh / en 等，留空则自动检测）</summary>
    [JsonPropertyName("Language")]
    public string Language { get; set; } = "zh";

    /// <summary>唤醒词</summary>
    [JsonPropertyName("WakeWord")]
    public string WakeWord { get; set; } = "老梁";

    /// <summary>唤醒词变体（本地识别引擎的词表），识别文本命中任一即唤醒</summary>
    [JsonPropertyName("WakeWordVariants")]
    public string[] WakeWordVariants { get; set; } = ["老梁", "梁文峰", "梁总", "laoliang"];

    /// <summary>
    /// 助手名称（DSH 系统提示词中的身份，如"梁文峰"）：
    /// 用户问"你叫什么名字"时按此回答；界面/浮层显示也优先用此名称。
    /// 留空则回退用唤醒词作为身份。
    /// </summary>
    [JsonPropertyName("AssistantName")]
    public string AssistantName { get; set; } = "梁文峰";

    /// <summary>
    /// Git 项目目录（语音说"推送到GitHub"时在此目录执行 git 提交推送；工作区受限模式的边界）。
    /// 注入 DSH 系统提示词，可设置中修改。
    /// </summary>
    [JsonPropertyName("GitProjectPath")]
    public string GitProjectPath { get; set; } = "D:\\Projects\\DSHVoiceAssistant";

    /// <summary>
    /// DSH 执行权限模式（与 DSH 代理的三种权限一致）：
    /// full = 完全访问（任意命令/文件/系统操作直接执行，推荐）
    /// workspace = 工作区受限（文件操作与命令执行仅限 GitProjectPath 内）
    /// readonly = 只读（仅查询/打开/搜索/媒体/对话，禁止执行与修改）
    /// 提示词按模式注入权限说明；执行器对 readonly/workspace 有硬性拦截。
    /// </summary>
    [JsonPropertyName("PermissionMode")]
    public string PermissionMode { get; set; } = "full";

    /// <summary>开机自启动（由设置窗口写入注册表）</summary>
    [JsonPropertyName("AutoStart")]
    public bool AutoStart { get; set; }

    /// <summary>
    /// 唤醒方式：
    /// local = 本地识别（Windows 内置识别引擎，免费离线，推荐）
    /// api = 云端识别（消耗 API 额度）
    /// off = 关闭（仅支持快捷键唤醒）
    /// </summary>
    [JsonPropertyName("WakeMode")]
    public string WakeMode { get; set; } = "local";

    /// <summary>是否启用全局快捷键唤醒/倾听</summary>
    [JsonPropertyName("HotKeyEnabled")]
    public bool HotKeyEnabled { get; set; } = true;

    /// <summary>
    /// 全局快捷键组合（格式如 "Ctrl+Alt+D" / "Ctrl+Shift+F1"），
    /// 至少包含一个修饰键（Ctrl/Alt/Shift/Win）。
    /// </summary>
    [JsonPropertyName("HotKeyCombo")]
    public string HotKeyCombo { get; set; } = "Ctrl+Alt+D";

    /// <summary>
    /// 自声过滤：播报回复期间忽略麦克风输入（VAD/唤醒检测），
    /// 防止自己的语音被扬声器→麦克风回路误触发（如打断自己的播报）。
    /// 开启时播报期间无法语音插嘴（可用快捷键打断）；关闭则保留语音插嘴行为。
    /// </summary>
    [JsonPropertyName("SelfVoiceFilter")]
    public bool SelfVoiceFilter { get; set; } = true;

    /// <summary>
    /// 麦克风数字增益（1.0 ~ 8.0，默认 2.0）：低增益麦克风适配，
    /// 在采集源头统一放大，云端识别受益；本地 SAPI 唤醒走原始音频（自带 AGC）；
    /// 1.0 = 不增益。
    /// </summary>
    [JsonPropertyName("MicGain")]
    public double MicGain { get; set; } = 2.0;

    /// <summary>麦克风设备编号（0 为系统默认）</summary>
    [JsonPropertyName("MicDeviceNumber")]
    public int MicDeviceNumber { get; set; }

    /// <summary>VAD 语音活动检测阈值（归一化 RMS，0.005~0.1）</summary>
    [JsonPropertyName("VadThreshold")]
    public double VadThreshold { get; set; } = 0.02;

    /// <summary>判定“开始说话”所需的连续有声帧数（每帧约 100ms）</summary>
    [JsonPropertyName("VadStartFrames")]
    public int VadStartFrames { get; set; } = 3;

    /// <summary>静音多少毫秒后判定一句话结束</summary>
    [JsonPropertyName("SilenceTimeoutMs")]
    public int SilenceTimeoutMs { get; set; } = 1200;

    /// <summary>一句话最短时长（毫秒），过短视为噪音丢弃</summary>
    [JsonPropertyName("MinUtteranceMs")]
    public int MinUtteranceMs { get; set; } = 400;

    /// <summary>一句话最长时长（毫秒），超时强制结束</summary>
    [JsonPropertyName("MaxUtteranceMs")]
    public int MaxUtteranceMs { get; set; } = 15000;

    /// <summary>web_search 使用的搜索引擎：baidu / bing / google</summary>
    [JsonPropertyName("SearchEngine")]
    public string SearchEngine { get; set; } = "baidu";

    /// <summary>API 请求超时（秒）</summary>
    [JsonPropertyName("ApiTimeoutSeconds")]
    public int ApiTimeoutSeconds { get; set; } = 90;

    /// <summary>
    /// TTS 播报方式：
    /// cloud = 云端自然音色（qwen3-tts-flash 等，听感接近豆包，推荐；失败自动回退本地）
    /// local = 仅用 Windows 本地系统语音
    /// </summary>
    [JsonPropertyName("TtsMode")]
    public string TtsMode { get; set; } = "cloud";

    /// <summary>云端 TTS 模型（工作台可用模型，如 qwen3-tts-flash）</summary>
    [JsonPropertyName("TtsModel")]
    public string TtsModel { get; set; } = "qwen3-tts-flash";

    /// <summary>云端 TTS 音色（qwen3-tts-flash：Andre 沉稳磁性男声 / Cherry 小姐姐 / Serena 温柔 / Ethan 阳光男声 等）</summary>
    [JsonPropertyName("TtsVoice")]
    public string TtsVoice { get; set; } = "Andre";

    /// <summary>
    /// 连续对话超时（秒）：唤醒后处理完指令保持倾听（无需再次呼叫），
    /// 沉默超过该时长后自动退出、回到待命等待再次呼叫。0 = 关闭连续对话。
    /// </summary>
    [JsonPropertyName("ConversationTimeoutSeconds")]
    public int ConversationTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// DSH 多轮记忆轮数：向引擎携带最近 N 轮对话（1 轮 = 用户指令 + DSH 回复），
    /// 超限自动淘汰最旧（总量另有 8000 字符兜底），防止上下文无限增长。0 = 关闭记忆（单轮）。
    /// </summary>
    [JsonPropertyName("DshHistoryRounds")]
    public int DshHistoryRounds { get; set; } = 6;

    /// <summary>
    /// 应用别名表（名称 → 路径/命令/steam链接）。
    /// 用于 open_game/open_app 查找兜底，如 "战神5": "steam://rungameid/2322010"。
    /// </summary>
    [JsonPropertyName("AppAliases")]
    public Dictionary<string, string> AppAliases { get; set; } = new();

    /// <summary>TTS 语速（-10 ~ 10，本地模式生效）</summary>
    [JsonPropertyName("TtsRate")]
    public int TtsRate { get; set; }

    /// <summary>TTS 音量（0 ~ 100，本地模式生效）</summary>
    [JsonPropertyName("TtsVolume")]
    public int TtsVolume { get; set; } = 100;

    /// <summary>点击关闭按钮时最小化到系统托盘</summary>
    [JsonPropertyName("MinimizeToTray")]
    public bool MinimizeToTray { get; set; } = true;

    /// <summary>
    /// 屏幕边缘光晕（Siri 风格）：静默运行（主窗口隐藏）时被唤起，
    /// 在主屏幕最外圈显示彩色流光动效，随倾听/处理/播报状态变化节奏；
    /// 回到待命或打开主窗口时消失。
    /// </summary>
    [JsonPropertyName("EdgeGlowEnabled")]
    public bool EdgeGlowEnabled { get; set; } = true;

    /// <summary>
    /// 对话浮层：主窗口隐藏时，在桌面中下部透明显示对话内容
    /// （用户指令 + 助手回复，无背景、文字不透明），回到待命或开窗时消失。
    /// </summary>
    [JsonPropertyName("ConversationOverlayEnabled")]
    public bool ConversationOverlayEnabled { get; set; } = true;

    /// <summary>对话浮层文字字体</summary>
    [JsonPropertyName("OverlayFontFamily")]
    public string OverlayFontFamily { get; set; } = "Microsoft YaHei UI";

    /// <summary>对话浮层文字字号</summary>
    [JsonPropertyName("OverlayFontSize")]
    public double OverlayFontSize { get; set; } = 26;

    /// <summary>对话浮层文字颜色（#RRGGBB）</summary>
    [JsonPropertyName("OverlayTextColor")]
    public string OverlayTextColor { get; set; } = "#FFFFFF";

    /// <summary>对话浮层文字阴影（无背景下的可读性）</summary>
    [JsonPropertyName("OverlayTextShadow")]
    public bool OverlayTextShadow { get; set; } = true;
}
