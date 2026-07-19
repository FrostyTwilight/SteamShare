# AGENTS.md — SteamShare 项目 AI 编码规范

## 语言约定

| 范围 | 语言 |
|---|---|
| 代码注释 | 中文 / English 均可，优先中文 |
| Commit message 正文 | **中文** |
| Commit message 前缀 | **English**（`feat` `fix` `docs` `chore` `refactor` `test` `ci` `style`） |
| Issue / PR 描述 | 中文 |
| 文档（README 等） | 中文为默认，英文同步维护 |

### Commit Message 格式

```
<type>: <中文简述>

- <中文详细说明>
- <中文详细说明>
```

示例：
```
feat: 添加文件分享密码保护功能

- 使用 PBKDF2 + AES-256-GCM 加密分享密钥
- 密码强度检测与提示
- 更新 CLI 的 share 命令参数
```

## 技术栈

| 层级 | 技术 |
|---|---|
| 运行时 | .NET 11 |
| GUI | Avalonia 12 + CommunityToolkit.Mvvm |
| CLI | Spectre.Console |
| Steam API | Steamworks.NET（独立 DLL，非 NuGet） |
| 数据库 | SQLite（Microsoft.Data.Sqlite） |
| 日志 | Serilog |
| 压缩 | K4os.Compression.LZ4 |
| 测试 | xUnit + FluentAssertions + NSubstitute |

## 代码风格

- 遵循项目现有 `.editorconfig` 配置
- 使用 C# 12 特性（primary constructor、collection expressions 等）
- 命名：PascalCase（类/方法/属性）、camelCase（局部变量/参数）、`_camelCase`（私有字段）
- `var` 仅在类型明显时使用
- 异步方法以 `Async` 结尾
- 日志使用 `Log.ForContext<T>()` 模式
- 禁止 `#pragma warning disable` 随意禁用警告、禁止不安全的强制类型转换

## 项目结构

```
src/
├── SteamShare.Core/    # 核心库：模型、服务、任务系统
├── SteamShare.CLI/     # 命令行工具
├── SteamShare.UI/      # 桌面 GUI
└── SteamShare.Test/    # 测试
```

添加新功能时：
- 核心逻辑放 `SteamShare.Core`
- CLI 入口放 `SteamShare.CLI`
- GUI 入口放 `SteamShare.UI`
- 必须有对应测试

## 测试要求

- 公共 API 必须有单元测试
- 测试文件命名：`<被测类名>Test.cs`
- Mock 使用 NSubstitute
- CI 环境使用虚拟 Steam API（`--ci` 标志）
