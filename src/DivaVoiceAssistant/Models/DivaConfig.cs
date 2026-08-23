using System.Text.Json.Serialization;

namespace DivaVoiceAssistant.Models;

/// <summary>
/// Diva 应用配置（对应 appsettings.json 的 DivaConfig 节点）。
/// </summary>
public sealed class DivaConfig
{
    /// <summary>百炼工作台 API 密钥（也可通过环境变量 DIVA_API_KEY 覆盖）</summary>
    [JsonPropertyName("ApiKey")]
    public string ApiKey { get; set; } = "";

    /// <summary>百炼 compatible-mode API 地址（不带末尾斜杠）</summary>
    [JsonPropertyName("ApiHost")]
    public string ApiHost { get; set; } = "https://ws-fdenp5x6eqwcud4u.cn-beijing.maas.aliyuncs.com/compatible-mode/v1";

    /// <summary>语音识别模型（百炼工作台可用的 omni 多模态模型，如 qwen3.5-omni-flash；转写走原生多模态网关）</summary>
    [JsonPropertyName("SpeechModel")]
    public string SpeechModel { get; set; } = "qwen3.5-omni-flash";

    /// <summary>DSH 指令模型（与 Diva 同款推理模型，工作台可用模型，如 deepseek-v4-flash）</summary>
    [JsonPropertyName("DSHModel")]
    public string DSHModel { get; set; } = "deepseek-v4-flash";

    /// <summary>识别语言（zh / en 等，留空则自动检测）</summary>
    [JsonPropertyName("Language")]
    public string Language { get; set; } = "zh";

    /// <summary>唤醒词（英文拼写）</summary>
    [JsonPropertyName("WakeWord")]
    public string WakeWord { get; set; } = "diva";

    /// <summary>唤醒词的变体（含常见中文音译），识别文本命中任一即唤醒</summary>
    [JsonPropertyName("WakeWordVariants")]
    public string[] WakeWordVariants { get; set; } = ["diva", "迪瓦", "迪娃", "黛娃", "蒂娃"];

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

    /// <summary>是否启用快捷键 Ctrl+Alt+D 唤醒</summary>
    [JsonPropertyName("HotKeyEnabled")]
    public bool HotKeyEnabled { get; set; } = true;

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

    /// <summary>云端 TTS 音色（qwen3-tts-flash 支持 Cherry/Serena/Ethan/Noah 等）</summary>
    [JsonPropertyName("TtsVoice")]
    public string TtsVoice { get; set; } = "Cherry";

    /// <summary>
    /// 连续对话超时（秒）：唤醒后处理完指令保持倾听（无需再次呼叫），
    /// 沉默超过该时长后自动退出、回到待命等待再次呼叫。0 = 关闭连续对话。
    /// </summary>
    [JsonPropertyName("ConversationTimeoutSeconds")]
    public int ConversationTimeoutSeconds { get; set; } = 30;

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
}
