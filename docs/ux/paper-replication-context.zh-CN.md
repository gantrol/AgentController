# Codex Micro：Paper HTML 复刻上下文

更新时间：2026-07-22

## 一句话目标

`CodexMicro.Desktop` 的主控制面板要**复刻 Paper 导出的 JSX／Tailwind 设计**，不是在它的基础上重新设计一套“相似的水晶风格”。布局、比例、层级、颜色、透明度、圆角、阴影和图标以 Paper 导出值为基准；虚拟版本只补充必要的交互状态，不改变静态外观语言。

## 参考资料优先级

| 优先级 | 资料 | 用途 | 能否覆盖 Paper 视觉值 |
| --- | --- | --- | --- |
| P0 | [Paper 设计稿](https://app.paper.design/file/01KY44WJ5EWGYGSF0GAB0GEQTB/1-0) | 视觉目标与人工验收基准 | 是，最高优先级 |
| P0 | [Paper 原始导出](paper-openai-micro-export.jsx) | DOM 层级、Tailwind 任意值、渐变、阴影、透明度和 SVG 路径的实现事实来源 | 是，最高优先级 |
| P1 | 用户提供的 Paper 截图 | 同尺寸截图对照，检查整体观感和层级 | 只用于验证 P0 |
| P1 | [Codex Micro 官方说明](https://learn.chatgpt.com/docs/features/codex-micro) | 按键与摇杆的动作语义 | 否，不决定材质和布局 |
| P2 | [独立 HTML/CSS 参考页](codex-micro-ui-reference.html) | 本地浏览器预览和次要对照；当前为本机未跟踪文件 | 否，它不是 Paper 原始导出 |
| P3 | [虚拟水晶交互草案](codex-micro-virtual-interaction-concept.zh-CN.md) | 记录虚拟版交互、状态和技术约束 | 否，只能补充交互 |
| P4 | [GPT Image 2 光影探索](codex-micro-virtual-crystal-concept.png) | 光线柔化、水晶气氛参考 | 否，不能作为组件、布局或图标稿 |

如果资料冲突，按上表从上到下处理。尤其不能用独立 HTML、GPT Image 或当前 XAML 的既有样式反向覆盖 Paper 导出参数。

## HTML／导出文件具体在哪里

### 1. 真正用于复刻的 Paper 原始导出

- 仓库中的持久副本：[docs/ux/paper-openai-micro-export.jsx](paper-openai-micro-export.jsx)
- 会话中收到的原始粘贴附件：`C:\Users\gantrol\.codex\attachments\cfeb619b-3b7e-4736-86db-ebb72be05901\pasted-text.txt`
- 导出文件头记录的 Paper 页面：`https://app.paper.design/file/01KY44WJ5EWGYGSF0GAB0GEQTB/1-0/1-0`

它使用 `.jsx` 后缀，是因为 Paper 导出的是 React JSX：结构本质上是 HTML DOM，样式写在 Tailwind class 和少量内联 `style` 中。它不是双击即可独立运行的静态 HTML，但它比截图更适合作为复刻规范，因为所有关键数值与 SVG path 都保留下来了。

### 2. 可直接在浏览器打开的独立 HTML

- [docs/ux/codex-micro-ui-reference.html](codex-micro-ui-reference.html)
- 它的本地索引说明在 `docs/ux/README.md`。

这个页面是另一份 HTML/CSS 参考实现，不是 Paper 导出的原件。目前两者都是本机未跟踪文件；保留用于预览和比较，但不能作为视觉参数的来源。若它与 `paper-openai-micro-export.jsx` 不一致，以 JSX 为准。

## Paper 导出行号地图

以下行号对应 [paper-openai-micro-export.jsx](paper-openai-micro-export.jsx)：

| 行号 | Paper 结构 | 需要保留的重点 |
| --- | --- | --- |
| 8–12 | 外层水晶壳、绿色外发光、内层机身 | 330 × 330 基准、内外圆角、边框、渐变、五组阴影与发光层级 |
| 13–25 | 两侧铭文与底部文字 | 位置、旋转方向、字号、字距与透明度 |
| 26 | 4 × 4 控件网格 | `4.8px` 间距和单元格比例 |
| 27–31 | 左上白色旋钮 | 双层圆盘、对角高光切面、内外阴影 |
| 32–107 | 六个 Agent 状态键 | 方形玻璃键帽、外发光、圆形状态光场、玻璃罩、加号；各状态使用导出的原始色值和透明度 |
| 56–62 | 右上摇杆 | 单元格 70% 的黑色圆帽、34%／27% 高光中心、四条凹槽 |
| 108–140 | Fast、Approve、Decline、Fork | 方形实体键帽、内凹圆面、精确的 24 × 24 SVG path |
| 141–149 | 左下旋钮与三盏指示灯 | 72% 底座、58% 黑色圆帽、三灯尺寸和间距 |
| 150–158 | 双格宽语音键 | 双格布局、键帽与内凹面、精确的麦克风 SVG path |
| 159–164 | Codex 键 | 与命令键相同的材质以及 Codex 标识位置 |

## Paper 到 WPF 的落点

| Paper 内容 | 当前 WPF 文件 | 职责 |
| --- | --- | --- |
| 整机外壳、铭文、4 × 4 网格、旋钮、摇杆、语音光层 | [MainWindow.xaml](../../virtual-micro/src/CodexMicro.Desktop/MainWindow.xaml) | 布局、图层顺序和局部几何 |
| Agent／命令键帽材质与交互模板 | [App.xaml](../../virtual-micro/src/CodexMicro.Desktop/App.xaml) | Brush、Shadow、Border、ControlTemplate 和交互触发器 |
| Fast／Approve／Decline／Fork／Mic／Codex 图标 | [KeycapIcon.cs](../../virtual-micro/src/CodexMicro.Desktop/Controls/KeycapIcon.cs) | 将 Paper 的 24 × 24 SVG path 等比绘制到 WPF |
| Agent 状态色、语音动态、摇杆反馈 | [MainWindow.xaml.cs](../../virtual-micro/src/CodexMicro.Desktop/MainWindow.xaml.cs) | 只控制状态与动画，不另造静态材质 |
| 视觉结构回归 | [WindowDesignTests.cs](../../virtual-micro/tests/CodexMicro.Desktop.Tests/WindowDesignTests.cs) | 比例、图层、动画边界和可交互区域的自动检查 |

窗口标题栏、应用图标、默认置顶和 HID／驱动链路不属于 Paper 的 330 × 330 画布；它们可以独立实现，但不得挤压、重排或替换主控制面板。

## 复刻规则

1. 以 Paper 的 `330 × 330` 根节点为归一化坐标。WPF 放大时统一按比例换算，不能凭观感逐项“调得差不多”。
2. 保留 DOM 的图层顺序。外发光、键帽、状态光场、内凹玻璃罩和图标不能合并成一个普通渐变圆。
3. 原样记录并换算任意值：`rgba`／八位十六进制颜色、opacity、圆角、inset、blur、shadow offset 和 SVG viewBox。
4. WPF 没有完全等价的 `backdrop-filter` 或 OKLab 插值时，用分层半透明 Brush 与 Effect 逼近；这种技术差异必须写在实现注释或测试说明中，不能顺手重做造型。
5. 不增加 Paper 中不存在的环、徽标、装饰图层或“智能”符号。Agent 状态应是 Paper 的圆形光场，不是另外套一圈状态环。
6. 按用户要求，当前任务状态不做循环闪烁；实现方式是稳定显示 Paper 某一状态的静态帧，而不是改变该状态的几何结构。
7. 语音声波属于虚拟交互增强：空闲时不得破坏 Paper 的语音键基线；录音／处理时才叠加，并且不能改变命中区或 HID 路径。
8. 视觉重构期间冻结按键、摇杆、Micro HID 和桥接协议行为，避免把样式问题与输入链路问题混在一起。

## 当前实现与 Paper 的已知差异

- 当前 Agent 键的彩色状态环是临时设计，不存在于 Paper 导出中，不能作为最终方案。
- 当前 `PaperAgentKeyBaseBrush` 使用了自定义渐变；Paper 键帽基底实际为 `#DCE2DFA3`、`backdrop-filter: blur(1px)` 和四层指定阴影，需要按导出层级重新落地。
- 命令键的主要 SVG path 已从 Paper／Codex 的 24 × 24 路径换算，方向正确；仍需与最终键帽尺寸一起做同尺度截图检查。
- 摇杆的黑色圆帽、径向高光和凹槽已接近 Paper 结构，但要以 70% 尺寸和导出阴影参数重新验收。
- 语音声波是允许的动态增强，不应被误认为 Paper 原始静态层。

因此，当前版本只能算“行为可用、部分元素已对齐”，不能称为 Paper 视觉复刻完成。

## 下一轮实现与验收顺序

1. 冻结 HID、按钮动作和摇杆事件，只处理视觉层。
2. 先复刻外壳与 4 × 4 布局，再复刻旋钮和摇杆。
3. 按 JSX 的图层逐层重建 Agent 键，删除临时状态环；仅取消时间轴闪烁。
4. 复刻命令键和语音键的键帽材质，保留已经核对过的 SVG path。
5. 在同一 `330 × 330` 视口分别截图 Paper 与 WPF，做半透明叠图对照，而不是只靠肉眼单独观看。
6. 验收空闲、蓝、绿、琥珀、红、未分配以及语音按下／松开状态；同时跑输入与 HID 回归，确认视觉改动没有改变动作路径。
