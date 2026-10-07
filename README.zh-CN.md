# Papergraph

**用图组织论证与论文写作。**

[English](README.md) · [简体中文](README.zh-CN.md)

Papergraph 是一款用于论文写作的本地桌面应用。通过命题及其关系组织论证，将观点、证据和笔记放在可查看、可编辑的图中，在整体结构与具体段落之间切换。

目前提供 Windows 正式版和 macOS 预览版。通过可选的本地 Agent 接口，外部 AI Agent 可以在版本检查的约束下读取、修改文档中的具体内容。

## 下载

| 平台 | 芯片架构 | 发布版本 | 下载 |
| --- | --- | --- | --- |
| Windows | x64 | 正式版 · v0.14.3 | [Windows 正式版](https://github.com/wangyuer1-lang/papergraph/releases/latest) |
| Windows | x64 | 预览版 · v0.15.0-preview.7 | [Windows 预览版](https://github.com/wangyuer1-lang/papergraph/releases/download/v0.15.0-preview.7/Papergraph-0.15.0-preview.7-windows-x64.zip) |
| macOS | Apple Silicon（M 系列） | 预览版 · v0.15.0-preview.7 | [macOS M 系列版](https://github.com/wangyuer1-lang/papergraph/releases/download/v0.15.0-preview.7/Papergraph-0.15.0-preview.7-macos-arm64.zip) |
| macOS | Intel | 预览版 · v0.15.0-preview.7 | [macOS Intel 版](https://github.com/wangyuer1-lang/papergraph/releases/download/v0.15.0-preview.7/Papergraph-0.15.0-preview.7-macos-x64.zip) |

**Windows：**解压到可写文件夹，运行 `papergraph.exe`。**macOS：**要求 macOS 14 或以上；解压后将 `Papergraph.app` 拖入“应用程序”。所有下载包均已包含 .NET 运行时，无需另行安装。

macOS 预览版使用临时签名（ad-hoc），尚未经过 Apple 公证；Intel 版本尚未进行实机验证。首次打开方法见 [macOS 使用指南](docs/macos.md#download-and-install)，本次更新见[发布说明](https://github.com/wangyuer1-lang/papergraph/releases/tag/v0.15.0-preview.7)。

![Papergraph 工作流程：编写命题、连接论证、检查证据并与 Agent 协作](docs/quickstart.svg)

## 核心概念

| 对象 | 用途 |
| --- | --- |
| 命题 | 表达观点、问题或段落，支持短标题及独立的证据、来源笔记。节点大小随命题长度变化。 |
| 关系 | 带方向和语义符号的连线，可分别填写标题与笔记。 |
| 方框 | 用矩形区域组织主题或章节，可交叠、记录内容并连接其他对象。 |
| 圆环 | 将相互连接的论证组织为一组，保留内部内容的可见性，也可作为整体连接其他对象。 |

双击方框或圆环可进入内部视图，完成编辑后返回全图。

## 主要功能

- **图形编辑：**连接命题，组织方框与圆环，跨图复制内容，排列相互连接的对象，并用颜色标记待审阅内容。
- **实时全文：**在画布下方查看随编辑更新的正文。有向正文关系（Body）决定阅读顺序；引用关系（Reference）与孤立节点不计入正文。顺序检查提示分叉歧义和循环，点击段落可定位原命题。
- **字数统计：**统计当前正文页或选中文本的词数与字符数，支持中文、日文及混合语言文本。
- **四页笔记：**将辅助材料与正文分开保存。第一页保留给作者，Agent 使用第二至第四页。
- **图谱管理：**在侧栏按分类管理图谱，支持自动保存、备份文件及 Markdown 导出。
- **界面设置：**支持浅色、深色主题，以及英文、简体中文和日文。在 **View → Language（视图 → 语言）** 中切换，文档内容保持原样；详见[语言支持说明](docs/localization.md)。

## 快速开始

1. 点击 **Graphs（图谱）** 旁的 **＋** 新建图谱，在顶部输入标题。
2. 双击画布空白处添加命题，选中后在右侧编辑正文与笔记。
3. 将连接柄拖向另一个对象；选中连线后设置含义、方向与笔记。
4. 用方框或圆环组织相关命题，通过 **Fit all / F** 查看当前视图中的全部内容。
5. 打开 **Full text（实时全文）** 审阅正文，点击段落返回对应命题。

| 操作 | Windows | macOS |
| --- | --- | --- |
| 框选 | 在空白处按住右键拖动 | 在 **Select（选择，V）** 模式下拖动空白处 |
| 平移 | 在空白处按住左键拖动 | 双指滑动，或使用 **Pan（平移，H）** |
| 缩放 | 鼠标滚轮 | 双指捏合、Command＋滚动，或 **− / ＋** 按钮 |
| 复制／粘贴 | Ctrl+C / Ctrl+V | Command+C / Command+V |
| 更多操作 | 右键菜单 | **Actions（操作）**、**···**、辅助点按或 Control＋点按 |

完整操作与正文顺序规则见 [Windows 使用指南](docs/usage.md)和 [macOS 使用指南](docs/macos.md#using-the-preview)（英文）。

## Agent 集成

本地接口允许外部 Agent 读取包含未保存编辑的实时文档，并对具体对象进行操作。`snapshot`、`search` 与 `fullText` 也可读取未激活的图谱，无需切换用户当前视图。

`addNodes` 和 `editGraph` 支持命题、关系、方框及圆环的编辑；其他接口用于管理页面和分类。新建图谱及完整副本默认在后台打开，复制时保留正文、笔记、位置、颜色与连接。

写入需提供目标文档、最新修订号与稳定的请求 UUID。图形编辑批次保存成功后才返回结果，整批操作可一步撤销；过期写入会被拒绝，持久化创建记录可防止重试时重复建页。笔记第一页始终保留给作者。页面创建、切换与画布撤销分别管理。

Windows 与 macOS 均通过仅限当前用户访问的本地命名管道通信。请求格式及命令行示例见 [Agent 接口指南](Agent%20guide.md)。

Papergraph 未内置 AI 模型、自动事实核验或 Zotero 检索。外部 Agent 需自行核实来源；根据其配置，文档内容可能发送至对应服务提供方。来源追踪、修改提议和人工审阅属于后续规划。

## 数据存储

图谱以可读的 `.papergraph` JSON 文件保存，同时兼容既有 `.yujian` 文档。每次保存保留上一版本的 `.bak` 备份，支持 Markdown 导出；撤销历史保留于当前会话。

| 平台 | 默认图谱目录 |
| --- | --- |
| Windows | `papergraph.exe` 旁的 `Data` 文件夹 |
| macOS | `~/Library/Application Support/Papergraph` |

升级时请保留图谱目录，尤其是 Windows 程序旁的 `Data` 文件夹。发布包不包含个人图谱或资料库状态。也可通过 `--data-dir` 指定其他目录，例如：

```powershell
.\papergraph.exe --data-dir "D:\My Papers\Graphs"
```

## 文档

| 指南 | 内容 |
| --- | --- |
| [Windows 使用指南](docs/usage.md) | 写作、导航、关系与正文阅读顺序 |
| [macOS 使用指南](docs/macos.md) | 安装、Mac 操作、兼容性与构建说明 |
| [Agent 接口指南](Agent%20guide.md) | 实时文档访问、版本检查与自动化 |
| [语言支持说明](docs/localization.md) | 界面翻译范围与翻译资源 |
| [发布说明](https://github.com/wangyuer1-lang/papergraph/releases) | 已发布版本与历史下载 |

上述详细指南目前以英文为主。

## 构建与测试

Windows 版使用 C# 与 WPF，桌面运行和测试需要 Windows 及 **.NET 10 SDK**。macOS 版使用 Avalonia，并共享图谱代码；详见 [macOS 构建说明](docs/macos.md#run-and-build)。

Windows 测试和便携版打包命令见[英文构建说明](README.md#build-and-test)。测试覆盖持久化、图形几何、选择与分组、视图控制、主题、正文顺序与字数、笔记、导航及 Agent 操作。请始终使用独立测试目录。

## 参与贡献

欢迎提交 Issue 或 Pull Request，说明希望改善的写作问题。缺陷报告请使用模拟内容，避免包含私人论文或凭据；提交代码前运行对应平台的测试，并保留既有文档内容。

## 支持项目

如果 Papergraph 对你的工作有所帮助，欢迎[赞助项目](https://github.com/sponsors/wangyuer1-lang)，支持后续维护、写作体验改进与文档完善。

## 许可证与致谢

Papergraph 采用 [MIT 许可证](LICENSE)。图布局以 C# 实现，设计参考见[布局参考资料](Layout%20references.md)，未捆绑所参考的 JavaScript 布局库。便携版包含 Microsoft .NET 运行时及相应许可证声明。
