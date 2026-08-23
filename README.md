# 🎤 Diva 语音助手（DSH 驱动版）

一个 Windows 桌面语音助手（C# / WPF / .NET 8），类似 Siri 的使用体验：
对着麦克风说 **"Diva"** 唤醒，然后说出指令（如"打开记事本""搜索深圳天气""下一首"），
助手会**朗读回复并执行动作**。

> **执行引擎说明**：本应用的所有指令理解与决策均由大模型完成——应用通过
> 阿里云百炼工作台 OpenAI 兼容接口调用 chat/completions，并注入 **DSH 系统角色提示词**
> （见 [docs/API文档.md](docs/API文档.md)），使大模型扮演"DSH 执行引擎"：
> 将自然语言转换为结构化 JSON 指令 `{action, target, params, response}`，
> 由本地执行器落地为 Windows 真实操作。本应用本身只负责：语音采集、唤醒词检测、
> 结果展示与本地动作执行。
>
> **注意**：DSH（DeepSeek Harness）是运行 Agent 会话的指令执行框架，本身不提供
> 对外 HTTP API；因此"DSH 作为执行引擎"的落地方式是上述"大模型 + 系统提示词"方案，
> 与您的架构意图完全一致：**一切指令决策在云端，本地只做语音与动作**。

## ✨ 功能特性

- 🎙️ **本地语音唤醒**：说 "Diva"（支持中文音译"迪瓦/迪娃"等变体）即可唤醒——
  使用 Windows 内置识别引擎**完全本地、免费、离线**，不消耗任何 API 算力
- 🗂️ **软件归档引擎**：启动时自动扫描桌面（含子文件夹）/开始菜单快捷方式、注册表卸载项、
  App Paths、Steam/Epic 游戏库，生成软件清单（`Config\软件清单.md` 可查看）；
  "打开游戏/软件"通过清单智能查找（支持"战神5"→"战神：诸神黄昏"这类简称匹配），
  找不到时可手动"重新扫描已装软件"或在设置中添加别名
- 💬 **连续对话**：唤醒一次即可连续下达多条指令（无需再次呼叫），
  可随时**插嘴打断** Diva 的播报；沉默 30 秒（可配置）后自动待命等待再次呼叫
- ⌨️ **快捷键唤醒**：`Ctrl+Alt+D` 全局快捷键
- 🧠 **DSH 指令引擎**：大模型将自然语言转换为结构化指令，支持 8 类动作
  （打开应用 / 网络搜索 / 系统命令 / 文件操作 / 文本回复 / 自定义脚本 / 媒体控制 / 打开网址）
- 🎧 **云端语音识别（仅指令）**：唤醒后的一次指令识别走百炼 omni 多模态模型，
  每轮对话仅消耗一次识别额度
- 🔊 **自然语音回复**：默认云端神经语音（qwen3-tts-flash，听感接近豆包，可换音色），
  离线/失败自动回退 Windows 本地语音
- 📊 **波形可视化**：实时音量波形 + 状态指示灯（绿=待命/黄=倾听/蓝=处理/紫=回复/红=出错）
- 🗔 **系统托盘**：状态颜色随状态变化，右键菜单控制显示/监听/退出
- 📝 **命令历史**：界面展示最近指令与 Diva 回复
- ⚙️ **设置窗口**：API 配置、麦克风设备、VAD 阈值、语速音量、开机自启等
- 🛡️ **健壮性**：全局异常兜底、日志落盘、断网提示不崩溃、单实例保护

## 🧱 技术架构

```
用户语音 → Diva应用(NAudio麦克风采集)
        ├─ 本地唤醒引擎(SAPI词表识别) → 命中唤醒词（免费/离线/即时）
        └─ 指令录音 → 云端语音识别(qwen3.5-omni-flash多模态网关) → 文本指令
        → DSH引擎(chat/completions + 系统提示词) → 结构化JSON指令
        → 本地执行器(打开应用/搜索/系统命令/媒体键…) → 动作落地
        → TTS语音反馈 + 界面展示
```

## 📦 环境要求

| 项目 | 要求 |
|------|------|
| 系统 | Windows 10 / 11 x64 |
| SDK | .NET 8 SDK（含 Windows Desktop 工作负载）或 Visual Studio 2022 |
| 麦克风 | 可用录音设备 |
| 网络 | 可访问阿里云百炼 API |
| 依赖 | NuGet 包：NAudio、System.Speech、Microsoft.Extensions.DependencyInjection |

## 🚀 快速开始

### 1. 构建

方式 A（命令行）：

```bat
build.bat
```

方式 B（Visual Studio 2022）：打开 `DivaVoiceAssistant.sln` → 生成解决方案。

方式 C（dotnet CLI）：

```bat
dotnet restore DivaVoiceAssistant.sln
dotnet build DivaVoiceAssistant.sln -c Release
dotnet publish src\DivaVoiceAssistant\DivaVoiceAssistant.csproj -c Release -o publish\win-x64
```

产物：`publish\win-x64\DivaVoiceAssistant.exe`

### 2. 配置

编辑程序目录下 `Config\appsettings.json`（首次运行自动生成），重点确认：

```json
"ApiKey": "sk-ws-...",          // 百炼工作台密钥（也可用环境变量 DIVA_API_KEY 覆盖）
"ApiHost": "https://...maas.aliyuncs.com/compatible-mode/v1",
"SpeechModel": "qwen3.5-omni-flash", // 指令语音识别模型（工作台可用 omni 模型）
"DSHModel": "deepseek-v4-flash", // 指令引擎模型（与 DSH 同款，需为工作台可用模型）
"WakeMode": "local"             // 唤醒方式：local=本地识别(免费) / api=云端识别 / off=仅快捷键
```

> ⚠️ 请保管好 API 密钥；**`appsettings.json` 已加入 .gitignore 不会提交**，
> 仓库中提供 `src\DivaVoiceAssistant\Config\appsettings.example.json` 作为配置模板：
> 首次使用请复制为 `appsettings.json` 并填入密钥（或设置环境变量 `DIVA_API_KEY`）。
> 应用首次运行（配置文件缺失）时也会自动生成默认配置。

### 3. 使用

1. 启动应用 → 状态"待命中"（🟢）
2. 说 **"Diva"**（或按 **Ctrl+Alt+D**）→ 提示音 + 状态"倾听中"（🟡）
3. 说出指令，如"打开记事本"、"搜索深圳天气"、"下一首"
4. Diva 执行动作并语音回复

## 📁 项目结构

```
DivaVoiceAssistant/
├── DivaVoiceAssistant.sln
├── build.bat                        # 一键构建
├── src/
│   ├── DivaVoiceAssistant/          # WPF 主项目
│   │   ├── App.xaml(.cs)            # 入口：DI装配/异常兜底/单实例
│   │   ├── MainWindow.xaml(.cs)     # 主界面
│   │   ├── SettingsWindow.xaml(.cs) # 设置窗口
│   │   ├── Services/                # 音频/唤醒/识别/DSH/执行/TTS/编排/托盘/热键
│   │   ├── Models/                  # 配置/状态/DSH请求响应模型
│   │   ├── ViewModels/              # MainViewModel / RelayCommand
│   │   ├── Converters/              # 状态→颜色/文字 转换器
│   │   ├── Controls/                # 波形可视化控件
│   │   ├── Utils/                   # 日志/VAD/JSON解析/WAV封装/媒体键/自启动
│   │   └── Config/appsettings.json
│   └── DivaVoiceAssistant.Tests/    # xUnit 单元测试
└── docs/                            # API文档/部署指南/架构设计/故障排除
```

## 📚 文档

- [API 集成文档](docs/API文档.md) —— 百炼接口 + DSH 系统提示词 + 指令协议
- [部署指南](docs/部署指南.md) —— 编译/发布/开机自启/卸载
- [架构设计](docs/架构设计.md) —— 模块划分/状态机/线程模型/扩展点
- [故障排除](docs/故障排除.md) —— 常见问题与解决办法

## 🔬 测试

```bat
dotnet test DivaVoiceAssistant.sln
```

覆盖：DSH 返回解析（JSON/fenced/兜底）、唤醒词匹配、动作映射、搜索 URL 构造、系统命令参数。

## 📄 License

[MIT](LICENSE)
