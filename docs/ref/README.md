# 学习笔记：Apple Intelligence 光效开源实现研究

日期：2026-08-24
来源代码已存档于本目录（docs/ref/）。

## 参考资料

| 来源 | 技术栈 | 核心手法 |
|------|--------|----------|
| [jacobamobin/AppleIntelligenceGlowEffect](https://github.com/jacobamobin/AppleIntelligenceGlowEffect)（IOS.swift / TypeToSiri.swift） | SwiftUI | 角向渐变 + 多层描边递增模糊 + 随机渐变停靠点缓动过渡 |
| [DEV: I Recreated iPhone's AI Edge Glow on Mac](https://dev.to/vector4wang/i-recreated-iphones-apple-intelligence-edge-glow-effect-on-mac-57f5)（dev-article-code*.txt） | macOS Swift + Core Animation | 60fps 定时器驱动 lineDashPhase 虚线跑马灯；速度=周长/时长；dt 钳制防跳变 |
| [Svelte Motion: AI Glow Border](https://motion.svelte.page/examples/ai-glow-border) | Web | conic-gradient 边框流动（参考链接） |
| [V2EX: iPhone AI 边缘流光搬到 Mac](https://global.v2ex.co/t/1221462) | macOS | 同类效果的产品化讨论 |

## 关键学习点

1. **结构**：真 Siri 是**角向（conic）渐变绕屏幕中心**——颜色沿整个边框连续环绕，
   四角自然衔接。WPF 无内置角向画刷，用"四边线性渐变 + 相位错开首尾相接"近似。
2. **发光层次**：多层描边 + 递增模糊叠加（SwiftUI 4 层：宽 6/9/11/15px，模糊 0/4/12/15），
   实现"外硬内软"。WPF 软件渲染模糊质量差 → 改用**分层透明度 + OpacityMask 衰减**近似。
3. **运动**：颜色不只"流动"，还会**随机再生 + easeInOut 平滑过渡**（0.5s 定时 + 1s 缓动），
   产生有机极光感；纯线性循环机械感强。
4. **配色**：低饱和霓虹（#BC82F3 紫、#F5B9EA 粉、#8D9FFF 蓝、#FF6778 红粉、#FFBA71 橙、
   #C686FF 紫）两两相邻渐变循环。
5. **性能**：单定时器驱动所有层、绘制合成（drawingGroup）、隐藏时取消定时器；
   macOS 版强调 dt 钳制防止后台恢复时动画跳变。

## 已应用到本项目的改进（EdgeGlowWindow）

1. **极光颜色呼吸**：每个 GradientStop 增加 SplineColorKeyFrame 缓动循环
   （不规则节奏 0/0.19/0.41/0.68/0.84/1.0，周期 = 流动周期的 1.6 倍，相位细分错开）
   → 光带跑动的同时颜色有机渐变。
2. **三层发光**：亮带 16px + 中晕 48px（衰减 α 0xE6→0）+ 外氛围光 96px（衰减 α 0x38→0）。
3. **四角 3 停靠点径向光斑**（亮核 0xFF → 中晕 0x59@0.55 → 透明），颜色缓动循环。
4. 呼吸脉动、状态变速（8s/4s/12s）、点击穿透保持不变。

## 可继续探索（未做）

- 自绘角向渐变 Brush（Freezable 子类）真正实现 conic gradient
- ShaderEffect（HLSL）实现 GPU 流光（需要 fxc 编译管线）
- 随机停靠点再生成（DispatcherTimer 驱动，仿 SwiftUI 0.5s 随机化）
