# SteamShare

[![CI](https://github.com/FrostyTwilight/SteamShare/actions/workflows/ci.yml/badge.svg)](https://github.com/FrostyTwilight/SteamShare/actions/workflows/ci.yml)
[![Version](https://img.shields.io/github/v/release/FrostyTwilight/SteamShare?label=version)](https://github.com/FrostyTwilight/SteamShare/releases)
[![License](https://img.shields.io/badge/license-CC%20BY--NC--SA%204.0-blue)](LICENSE)

> ## 🚫 免费软件 — 禁止售卖
> 本软件基于 [CC BY-NC-SA 4.0](LICENSE) 协议，**100% 免费**，仅供个人非商业用途使用。
> **严禁任何形式的商业使用、转售或盈利。**
> 如果你为这个软件付了钱，说明你被骗了——请立即要求退款。

在 Steam 好友间轻松分享文件 — 永久免费。

[English Documentation](README.en.md)

---

## ⚠️ 免责声明

- **SteamShare 与 Valve Corporation 或 Steam 无任何关联、背书或合作关系。** "Steam" 和 Steam 徽标是 Valve Corporation 的注册商标。所有商标和品牌名称均归其各自所有者所有。本项目不暗示 Valve 的任何认可或关联。
- **Valve 可能对用户和本仓库采取措施。** SteamShare 运行在 **App ID 480（Spacewar）** 的创意工坊基础设施上——这是一个用于开发者测试的沙盒环境。使用本软件可能导致 Valve 对 **你的 Steam 账号**（包括账号限制、物品移除或社区限制）和/或 **本 GitHub 仓库**（删除通知、API 访问限制）采取措施。在好友间小规模合理使用可降低此风险，但超出合理范围的使用请自行承担后果。
- **下载的文件可能包含病毒或恶意软件。** 在打开或执行下载的文件之前，请务必使用适当的安全工具进行扫描。SteamShare 的作者对通过本软件分享的文件造成的任何损害不承担任何责任。
- **本软件按"原样"提供，不提供任何明示或暗示的保证。**

---

## ⚠️ 许可证 — 请仔细阅读

**[CC BY-NC-SA 4.0](LICENSE)** — 署名-非商业性使用-相同方式共享 4.0 国际

> ### 🚫 严禁商业用途
> 本软件**仅供个人非商业用途免费使用**。你**不得**：
> - 出售、转售本软件或对本软件收费
> - 将本软件与任何付费产品或服务捆绑
> - 将本软件用于任何商业目的
> - 以任何形式将从本软件的访问中获利
>
> 任何违反上述条款的行为均构成对许可证的违反。

- ✅ **免费使用**于个人非商业目的
- ✅ 你可以在相同许可条款（CC BY-NC-SA 4.0）下分享和改编本软件
- ❌ **严禁任何形式的商业用途**
- ❌ **严禁以任何形式出售、转售或商业化本软件**

> **如果你为这个软件付了钱，说明你被骗了。请立即要求退款。**

---

## SteamShare 是什么？

SteamShare 是一个跨平台（.NET，Windows & Linux）的文件分享工具，让你与 [Steam](https://steamcommunity.com/workshop/) 好友之间方便地分享文件。你的文件会被存储为 Spacewar 沙盒（App ID 480）下的创意工坊物品，通过安全的分享密钥在好友间进行点对点分享。

### 核心概念

| 概念 | 说明 |
|---|---|
| **文件组** | 你想分享给好友的本地文件夹，对应一个创意工坊物品 |
| **分享密钥** | 以 `sshare+` 为前缀的令牌，发送给好友即可授权访问；可选择 AES-256-GCM 密码保护 |
| **可见性** | 私密（仅自己）→ 仅分享（持有密钥的好友可访问）→ 公开（所有人） |

### 功能

- **上传**文件夹到 Steam 创意工坊，方便与好友分享
- **下载**好友分享的文件组（通过分享密钥，无需订阅创意工坊物品）
- **分享**文件组给好友（支持可选密码保护）
- **管理**文件组：重命名、移动、删除、更改可见性
- **查看**下载量和评分
- **自动恢复**中断的下载和上传任务（可在设置中配置）
- **任务系统**实时跟踪所有操作的进度
- **多语言**支持
- **双界面**：GUI（Avalonia 12 Fluent）和 CLI（Spectre.Console）
- **自动追踪**已拥有和本地存储的文件组

---

## 项目架构

```
SteamShare.sln
└── src/
    ├── SteamShare.Core/       # 核心库：数据模型、服务（SQLite、编排器、任务系统）
    ├── SteamShare.CLI/        # 跨平台命令行界面
    ├── SteamShare.UI/         # 跨平台桌面 GUI（Avalonia 12）
    └── SteamShare.Test/       # 单元测试和集成测试（xUnit + NSubstitute）
```

### 技术栈

| 层级 | 技术 |
|---|---|
| 运行时 | .NET 11 |
| Steam API | Steamworks.NET（独立版本，来自 GitHub Releases） |
| GUI 框架 | Avalonia 12（Fluent 主题） |
| CLI 框架 | Spectre.Console |
| 架构 | MVVM + DI（CommunityToolkit.Mvvm、Microsoft.Extensions.DI） |
| 数据库 | SQLite（Microsoft.Data.Sqlite）— 追踪数据库、待处理任务持久化 |
| 序列化 | System.Text.Json |
| 日志 | Serilog（控制台 + 文件输出） |
| 压缩 | K4os.Compression.LZ4 |
| 任务系统 | 内存任务树，基于并发字典 + 环境上下文 |
| 测试 | xUnit、FluentAssertions、NSubstitute、Coverlet |

---

## 工作原理

### 分享密钥格式

```
sshare+<base64 编码的负载>
```

负载处理流程：
1. JSON 对象：`{ "encrypted": false, "id": <已发布文件ID> }`
2. 使用 **LZ4** 压缩
3. 如果有密码保护：`id` 字段使用 **RFC 2898（PBKDF2）+ AES-256-GCM** 加密，`encrypted` 设为 `true`

### 上传流程

1. 选择你想分享给好友的本地文件夹
2. SteamShare 创建创意工坊物品（App ID 480）并上传文件夹中的文件
3. 元数据（名称、虚拟文件夹路径等）通过 `AddItemKeyValueTag` 存储在物品的创意工坊元数据中
4. 每个物品标记 `steamshare_tag=true` 用于识别
5. 默认可见性为私密
6. 上传以任务形式实时跟踪进度；中断的上传在下次启动时自动恢复

### 下载流程

1. 获取好友发给你的分享密钥
2. SteamShare 解析密钥，必要时解密
3. 创意工坊物品下载到 Steam 临时目录
4. 文件移动到你的本地存储（无需订阅创意工坊物品）
5. SQLite 追踪数据库记录下载信息以便管理
6. 下载以任务形式实时跟踪进度；中断的下载在下次启动时自动恢复

### 任务系统与自动恢复

所有上传和下载操作均通过**任务编排系统**（`UploadOrchestrator` / `DownloadOrchestrator`）运行。每个操作以任务形式跟踪，实时显示进度、状态和错误报告。待处理任务持久化存储在 SQLite 数据库中。应用启动时可自动恢复任何中断的下载或上传任务——此行为可在设置中配置（"启动时自动重启未完成任务"）。

---

## 快速开始

### 支持平台

| 平台 | 状态 |
|---|---|
| **Windows**（10/11，x64） | 支持 |
| **Linux**（x64） | 支持 |
| **macOS** | 不支持 |

SteamShare 需要 Steam 客户端。macOS 版本的 Steamworks 原生库与项目不兼容，因此暂不支持。

### 前置要求

- [.NET 11 SDK](https://dotnet.microsoft.com/download/dotnet/11.0)
- **Steam 客户端**已安装并运行（已登录）

### 安装

#### Windows

1. 安装 [.NET 11 SDK](https://dotnet.microsoft.com/download/dotnet/11.0)。
2. 安装 [Steam 客户端](https://store.steampowered.com/about/) 并登录。
3. 从 [Releases](https://github.com/FrostyTwilight/SteamShare/releases) 下载最新的 `combined-sc-win-x64.zip`（自包含，无需安装 .NET）或按需选择其他[变体](#release-产物)。
4. 将压缩包解压到你选择的文件夹。
5. 运行 `SteamShare.UI.exe`（GUI）或 `SteamShare.CLI.exe`（CLI）。

从源码构建：

```powershell
git clone https://github.com/FrostyTwilight/SteamShare.git
cd SteamShare
dotnet restore
dotnet build
```

#### Linux

1. 使用你发行版的包管理器或 Microsoft 安装脚本安装 [.NET 11 SDK](https://dotnet.microsoft.com/download/dotnet/11.0)。
2. 安装 [Steam 客户端](https://store.steampowered.com/about/) 并登录。
3. 从 [Releases](https://github.com/FrostyTwilight/SteamShare/releases) 下载最新的 `combined-sc-linux-x64.zip`（自包含，无需安装 .NET）或按需选择其他[变体](#release-产物)。
4. 将压缩包解压到你选择的文件夹。
5. 运行 `./SteamShare.UI`（GUI）或 `./SteamShare.CLI`（CLI）。

从源码构建：

```bash
git clone https://github.com/FrostyTwilight/SteamShare.git
cd SteamShare
dotnet restore
dotnet build
```

### 运行 GUI

```bash
dotnet run --project src/SteamShare.UI
```

### 运行 CLI

```bash
dotnet run --project src/SteamShare.CLI -- <命令> [选项]
```

可用 CLI 命令：`upload`、`download`、`share`、`list`、`delete`、`rename`、`visibility`

### 运行测试

```bash
dotnet test
```

测试在 CI 环境中使用虚拟 Steam API，在本地使用真实的 Steam API。

---

## Release 产物

每个 [Release](https://github.com/FrostyTwilight/SteamShare/releases) 提供 **6 种变体 × 2 个平台 = 12 个包**：

### 变体

| 变体 | 自包含 | 框架依赖 |
|---|---|---|
| **CLI** | `cli-sc-{rid}.zip` | `cli-fd-{rid}.zip` |
| **GUI** | `gui-sc-{rid}.zip` | `gui-fd-{rid}.zip` |
| **GUI + CLI** | `combined-sc-{rid}.zip` | `combined-fd-{rid}.zip` |

其中 `{rid}` 为 `win-x64` 或 `linux-x64`。

### 自包含 vs 框架依赖

| | 自包含（`-sc-`） | 框架依赖（`-fd-`） |
|---|---|---|
| **运行时** | ❌ 无需安装 — .NET 已内嵌 | ✅ 需要安装 .NET 11 |
| **文件大小** | 约 10–50 MB（取决于变体） | 约 3–40 MB（取决于变体） |
| **单文件** | ✅ 是（单个 `.exe`） | ✅ 是（单个 `.exe`） |

> **注意**：GUI+CLI 合并包（`combined-*`）以目录形式发布——**不是**单文件。

### 包内容

每个包包含：

| 文件 | 说明 |
|---|---|
| `SteamShare.UI.exe`（Linux 无扩展名） | GUI 应用 |
| `SteamShare.CLI.exe`（Linux 无扩展名） | CLI 工具（仅 CLI 和 Combined 变体） |
| `steam_api64.dll` / `libsteam_api.so` | Steamworks 原生库 |
| `steam_appid.txt` | App ID 占位符（480） |
| `README.md` | 中文文档 |
| `README.en.md` | 英文文档 |
| `LICENSE` | CC BY-NC-SA 4.0 |

---

## 配置

SteamShare 使用 **SQLite 数据库**（`steamshare.db`）存储在用户的应用数据目录中。记录内容包括：

- 拥有的文件组 ID 及其本地存储路径
- 下载/上传进度状态（待处理任务）
- 用户偏好（语言、主题、自动恢复行为）

---

## 用户须知

SteamShare 基于 Steam 现有基础设施实现好友间文件分享。使用即表示你已知悉：

1. **Valve 可能对你的 Steam 账号采取措施**，如果他们检测到异常使用。
2. Spacewar 创意工坊沙盒（App ID 480）不保证持续可用。
3. 分享密钥是访问文件的唯一凭证，请妥善保管，不要泄露给不信任的人。
4. 你对上传和下载的文件负有全部责任。
5. 扫描所有下载文件中的恶意软件是你的责任。

---

## 贡献

欢迎在相同的 [CC BY-NC-SA 4.0](LICENSE) 许可下贡献代码。通过贡献，你同意你的贡献将在相同条款下获得许可。

---

## 常见问题

**为什么使用 Steam 创意工坊？**
你和好友都在 Steam 上，无需注册额外账号。通过 Steam 直接传输，免费且方便。

**为什么使用 App ID 480（Spacewar）？**
Spacewar 是 Valve 为 Steamworks 开发者提供的测试沙盒。它拥有可用的创意工坊，但没有公开商店页面，因此分享的文件仅在持有密钥的好友之间可见。

**这违反 Steam 服务条款吗？**
Spacewar 创意工坊用于开发者测试。在好友间适度使用通常不会引起注意，但超出合理范围的使用存在风险。请合理使用。

**Valve 能关掉这个吗？**
理论上可以。Valve 有权限制 App ID 480 的创意工坊访问。合理、小规模地在好友间使用是降低风险的最佳方式。

**为什么不使用 NuGet 安装 Steamworks.NET？**
NuGet 包已过时且无人维护。SteamShare 使用来自 [Steamworks.NET GitHub Releases](https://github.com/rlabrecque/Steamworks.NET/releases) 的最新独立 `.dll` 文件。

---

*Steam 是 Valve Corporation 的注册商标。SteamShare 与 Valve 无任何关联。*
