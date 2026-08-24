# 设计文档：屏幕边缘 Siri 光晕动效（EdgeGlow）

日期：2026-08-24
状态：已确认，待实现
适用范围：DSHVoiceAssistant（WPF / .NET 8）

## 背景

静默启动（主窗口不显示）时，用户无法直观感知助手的唤起与工作状态。
参考新版 Siri（iOS 18 / Apple Intelligence）的交互范式：**激活时屏幕四周
边缘亮起一圈柔和的彩色流光光晕**，随使用状态变化节奏，结束后消退。
（参考：SlashGear《Here's Why Your iPhone Is Glowing Around The Edges》、
pocket-lint《How to get Siri's new look in iOS 18.3》、CNET《Is Your iPhone
Not Showing Siri's Modern Glow?》）

## 决策记录

| 决策点 | 结论 |
|--------|------|
| 显示时机 | 静默运行（主窗口不可见）时，唤起（唤醒词/快捷键）→ 出现 |
| 持续 | 倾听→处理→播报全程显示，节奏随状态变化 |
| 消失 | 回到待命（对话超时/退出监听）、打开主窗口、静音时 |
| 主窗口可见时 | 永不显示（避免遮挡界面） |
| 设置项 | `EdgeGlowEnabled: bool = true`，设置窗口提供开关 |

## 视觉设计（对应 iOS 18 特征）

| 要素 | 设计 |
|------|------|
| 形态 | 主屏幕四边各一条光带（厚 28px），四角加亮圆角光斑衔接，整圈围绕屏幕外缘 |
| 色彩 | 每条光带线性渐变，调色板循环：粉(#FF9BC7)→紫(#B39DFF)→蓝(#7EC8FF)→青(#8FF0E8)→暖橙(#FFC58F)，低饱和柔光色 |
| 流动 | 四条边渐变**相位错开**（每条延迟 1/4 周期），形成光沿边框"跑"的动感 |
| 光晕 | 每条光带叠加 DropShadowEffect（BlurRadius 24、ShadowDepth 0、颜色随渐变循环） |
| 呼吸 | 整层透明度 0.75↔1.0 往复，周期 2.5s |
| 状态节奏 | 倾听=正常(8s 周期)；处理中=加速(4s)；播报=舒缓(12s) |

## 技术设计

### EdgeGlowWindow（新文件 `Controls/EdgeGlowWindow.cs`，纯代码构建无 XAML）

- 无边框透明悬浮窗：`WindowStyle=None`、`AllowsTransparency=true`、
  `ShowInTaskbar=false`、`Topmost=true`、`ShowActivated=false`、`Focusable=false`
- **点击穿透**：窗口句柄挂 `WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_TOOLWINDOW`
  （通过 HwndSource.AddHook 在 WM_CREATE 后设置），鼠标事件全部穿透到下层
- 尺寸 = 主屏幕全屏 `SystemParameters.PrimaryScreenWidth/Height`，位置 (0,0)
- 内容：Grid 内 4 条光带（Border）+ 4 角光斑（Border，CornerRadius=28，
  与相邻光带重叠衔接）
- 动画：纯 WPF 动画（ColorAnimationUsingKeyFrames 无限循环 + DoubleAnimation
  往复），无 ShaderEffect 依赖；`SetPhase(DSHState)` 按状态重建动画周期
- 生命周期：`Show()` / `Hide()`；`Show` 时先 `Left/Top = 0` 定位
- 性能：光带面积小（四周条带），透明层软件渲染开销可接受

### 可见性逻辑（纯函数，可单测）

```
ShouldShow(state, mainWindowVisible) =
    EdgeGlowEnabled && !mainWindowVisible && state is
        Recording or Transcribing or Thinking or Executing or Speaking
```

- `DSHState` 枚举已有上述状态值（Idle 不显示）
- 静音时状态为 Idle → 自然不显示

### 接线（MainWindow）

- 构造函数创建 `EdgeGlowWindow`，订阅：
  - `_orchestrator.StateChanged` → 计算 ShouldShow → Show/Hide；可见时调用
    `SetPhase(state)` 更新动画节奏
  - 自身 `IsVisibleChanged` → 窗口可见性变化时重算 ShouldShow
- 所有回调经 `Dispatcher` 封送到 UI 线程（StateChanged 可能来自音频/线程池线程）
- 应用退出时随窗口销毁

### 设置窗口

「行为选项」区块新增 CheckBox「静默运行被唤起时显示屏幕边缘光晕（Siri 风格）」，
绑定 `EdgeGlowEnabled`。

## 测试

### 单元测试

`EdgeGlowVisibilityTests`：纯函数覆盖——
- 各忙碌状态（Recording/Transcribing/Thinking/Executing/Speaking）+ 窗口隐藏 → true
- Idle + 窗口隐藏 → false
- 任意状态 + 窗口可见 → false
- 开关关闭 → false

### 手动验证清单

1. 静默启动（--silent）→ 说唤醒词 → 屏幕四周出现彩色流光，随倾听/处理/播报变速
2. 播报结束回倾听→沉默超时回到待命 → 光晕消失
3. 光晕显示期间鼠标操作其他窗口不受影响（点击穿透）
4. 托盘打开主窗口 → 光晕立即消失
5. 正常双击运行（窗口可见）→ 唤起时不显示光晕
6. 设置关闭 EdgeGlowEnabled → 不显示

## 文档同步

- `README.md`：功能特性新增「屏幕边缘 Siri 光晕」；配置表新增 `EdgeGlowEnabled`

## 不做的事（YAGNI）

- 不做多显示器支持（仅主屏幕）
- 不做 ShaderEffect/自定义像素着色器（纯动画已够）
- 不做光晕颜色/粗细的可配置化
