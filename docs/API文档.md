# API 集成文档

DSH 语音助手调用阿里云百炼工作台的接口如下：

| 用途 | 端点 | 模型 |
|------|------|------|
| 指令语音识别（ASR） | `POST {原生Host}/api/v1/services/aigc/multimodal-generation/generation` | omni 多模态模型（默认 `qwen3.5-omni-flash`） |
| DSH 指令引擎（LLM） | `POST {ApiHost}/chat/completions` | deepseek 系列 / qwen 系列（默认 `deepseek-v4-flash`，与 DSH 同款模型） |

`ApiHost` 默认值（compatible-mode 基址）：

```
https://ws-fdenp5x6eqwcud4u.cn-beijing.maas.aliyuncs.com/compatible-mode/v1
```

> ⚠️ **重要事实（实测确认）**：百炼 compatible-mode **不提供**
> OpenAI 风格的 `/audio/transcriptions` 文件转写路由（任何模型均返回 404）。
> 语音转写必须走百炼**原生多模态网关**：`https://{host}/api/v1/services/aigc/multimodal-generation/generation`
> （即从 ApiHost 去掉 `/compatible-mode/v1` 后缀）。

认证：所有请求携带 `Authorization: Bearer <ApiKey>`。
密钥可在 `Config\appsettings.json` 配置，也可通过环境变量 `DSH_API_KEY` 覆盖。

> 模型名必须以你的百炼工作台实际开通的模型为准。若调用报 404/InvalidParameter，
> 请在百炼工作台"模型广场"确认可用的语音识别模型与对话模型名称
> （`GET {ApiHost}/models` 可列出全部可用模型）。

---

## 一、指令语音识别（原生多模态网关）

```
POST {原生Host}/api/v1/services/aigc/multimodal-generation/generation
Content-Type: application/json
Authorization: Bearer <ApiKey>
```

请求体（WAV 音频以 base64 data URI 放入 user 消息）：

```json
{
  "model": "qwen3.5-omni-flash",
  "input": {
    "messages": [
      {
        "role": "system",
        "content": [ { "text": "请把用户音频中的语音内容转写为文字，只输出转写结果，不要任何解释或多余内容。（语音为中文）" } ]
      },
      {
        "role": "user",
        "content": [ { "audio": "data:audio/wav;base64,<base64音频>" } ]
      }
    ]
  },
  "parameters": { "result_format": "message", "max_tokens": 500 }
}
```

音频规格：WAV / 16kHz / 16bit / 单声道（应用内录音即此格式）。

成功响应：

```json
{
  "output": {
    "choices": [
      { "message": { "role": "assistant", "content": [ { "text": "打开记事本" } ] } }
    ]
  }
}
```

应用内从 `output.choices[0].message.content[]` 中取第一个 `text` 字段作为识别结果。

应用内的超时时间为 90 秒（可配置），超时/网络异常会返回友好错误提示，不影响程序运行。

> 说明：唤醒词检测默认走**本地 SAPI 引擎**（不调用本接口，免费离线）；
> 仅唤醒后的一句指令识别调用本接口，每轮对话消耗一次识别额度。
> 若在设置中把唤醒方式改为"云端识别"，则唤醒检测也走本接口。

---

## 二、DSH 指令引擎（核心）

```
POST {ApiHost}/chat/completions
Content-Type: application/json
Authorization: Bearer <ApiKey>
```

请求体：

```json
{
  "model": "deepseek-v4-flash",
  "messages": [
    { "role": "system", "content": "<DSH 系统角色提示词，见下>" },
    { "role": "user", "content": "打开记事本" }
  ],
  "temperature": 0.3,
  "max_tokens": 500
}
```

响应体（OpenAI 兼容）：

```json
{
  "choices": [
    { "message": { "role": "assistant", "content": "{\"action\":\"open_app\",\"target\":\"notepad.exe\",\"response\":\"好的，正在为您打开记事本\"}" } }
  ]
}
```

### DSH 指令协议

大模型必须返回一个 JSON 对象（允许被 Markdown 代码块包裹），字段：

| 字段 | 类型 | 说明 |
|------|------|------|
| `action` | string | 动作类型（见下表） |
| `target` | string | 动作目标 |
| `params` | object? | 附加参数（值为字符串） |
| `response` | string | 给用户的语音回复（10~30 字） |

| action | target 示例 | 本地执行效果 |
|--------|-------------|--------------|
| `open_app` | `notepad.exe` / `C:\path\app.exe` | 打开程序（ShellExecute） |
| `web_search` | `深圳天气` | 用配置的搜索引擎（百度/Bing/Google）打开搜索 |
| `system_command` | `shutdown` / `restart` / `lock` / `sleep` / `hibernate` / `logoff` | 关机/重启/锁屏/睡眠等 |
| `text_reply` | （空） | 仅朗读 response |
| `control_media` | `play` / `pause` / `next` / `prev` / `volume_up` / `volume_down` / `mute` | 模拟多媒体键（全局生效） |
| `open_url` | `https://www.baidu.com` | 默认浏览器打开 |
| `file_operation` | 文件/目录路径 | `params.operation`: `open` / `reveal` / `delete`（delete 必须带 `confirm=true`） |
| `custom_script` | PowerShell 命令文本 | 执行 PowerShell（超时 60 秒；**全局权限通道**：git 操作、脚本、系统管理等） |

解析策略（`Utils/DSHCommandParser.cs`）：

1. 提取首个完整 JSON 对象（自动剥离 Markdown 围栏与前后杂文）
2. 缺少 `action` 或解析失败 → 兜底为 `text_reply`，朗读原始内容
3. 未知 action → 本地执行器返回友好错误并朗读

### DSH 系统角色提示词（内置于 DSHCommandService.cs）

```
你是{NAME}，一个Windows桌面语音助手的执行引擎。……（完整模板见 DSHCommandService.cs）

【权限说明】
- 全局完整权限：git 提交/推送、任意命令与脚本、文件读写/删除、安装软件、系统设置、
  网络请求等一律直接执行，不拒绝、不建议用户手动操作
- 删除等不可逆操作：用户明确要求即可（file_operation 的 delete 需 params.confirm="true"）
- 常用项目目录 {GIT_PATH}（配置 GitProjectPath）：说"推送到GitHub"时在此执行 git 提交推送
- 命令执行后把真实结果写进 response 播报

【指令转换规则】
1. 打开应用/软件 → action: "open_app"
2. 搜索信息 → action: "web_search"
3. 系统操作(关机/重启/锁屏/睡眠) → action: "system_command"
4. 纯对话回复 → action: "text_reply"
5. 媒体控制(播放/暂停/下一首/音量) → action: "control_media"
6. 打开网页 → action: "open_url"
7. 文件操作 → action: "file_operation"（params.operation: open/reveal/delete）
8. 自定义命令/脚本 → action: "custom_script"（任意 PowerShell，通用执行通道）
9. 打开游戏 → action: "open_game"

【输出格式】
只输出一个JSON对象，不要输出任何多余文字、解释或Markdown代码块。字段：
- action: 字符串，上述动作类型
- target: 字符串，动作的目标对象
- params: 可选对象，附加参数（键值对，值必须是字符串）
- response: 字符串，给用户的语音回复内容（简洁友好，10~30字）

【示例】…（打开浏览器/搜索/关机/闲聊/下一首/打开网页）

【注意事项】
- 保持回复简洁友好，response 字段会被语音朗读
- 对于不确定的指令，用 text_reply 请用户再说一次
- 程序路径使用Windows默认路径即可（如 notepad.exe、calc.exe、chrome.exe）
- open_app 的 target 只给程序名或路径，不要带引号
```

如需调整 二狗 的能力边界（新增动作、更换回复风格），直接修改
`DSHCommandService.SystemPrompt` 即可——这就是"DSH 引擎"的规则面。

---

## 三、语音合成（自然音色 TTS）

应用默认用百炼云端神经语音回复（听感接近豆包，可配置音色）：

```
POST {原生Host}/api/v1/services/aigc/multimodal-generation/generation
Content-Type: application/json
Authorization: Bearer <ApiKey>
```

请求体（注意：TTS 走的是 `input.text`，与转写的 `messages` 结构不同）：

```json
{
  "model": "qwen3-tts-flash",
  "input": { "text": "好的，正在为您打开记事本" },
  "parameters": { "voice": "Cherry", "response_format": { "format": "wav" } }
}
```

| 参数 | 说明 |
|------|------|
| `model` | 默认 `qwen3-tts-flash`（工作台可用 TTS 模型） |
| `parameters.voice` | 音色：Cherry / Serena / Ethan / Noah 等（可配置 `TtsVoice`） |
| `parameters.response_format` | 音频格式字典，`{"format":"wav"}` |

响应：`output.audio.url`（临时音频下载地址，GET 获取 WAV），或 `output.audio.data`
（内嵌 base64）。应用下载后以 NAudio 播放。

> 实测注意：`response_format` 必须是字典（`{"format":"wav"}`），传字符串会报 400；
> 输入必须是 `input.text`（传 `messages` 会报 "invalid text"）。
> TTS 失败/离线时应用自动回退 Windows 本地语音，不影响使用。

---

## 四、错误处理与重试策略

| 场景 | 处理 |
|------|------|
| HTTP 4xx（401 密钥错误 / 404 模型不存在） | 立即返回错误并语音提示，不重试 |
| HTTP 5xx / 网络异常 | 自动重试 1 次（间隔 1 秒） |
| 请求超时（90s） | 返回"请求超时，请检查网络" |
| 麦克风不可用 | 启动时提示错误，程序继续运行（仍可用快捷键测试，但无音频输入） |
| 识别内容为空 | 静默回到待命状态 |
