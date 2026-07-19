# SteamShare

[![CI](https://github.com/FrostyTwilight/SteamShare/actions/workflows/ci.yml/badge.svg)](https://github.com/FrostyTwilight/SteamShare/actions/workflows/ci.yml)
[![Version](https://img.shields.io/github/v/release/FrostyTwilight/SteamShare?label=version)](https://github.com/FrostyTwilight/SteamShare/releases)
[![License](https://img.shields.io/badge/license-CC%20BY--NC--SA%204.0-blue)](LICENSE)

> ## 🚫 FREE SOFTWARE — NOT FOR SALE
> This software is **100% free** for personal, non-commercial use under [CC BY-NC-SA 4.0](LICENSE).
> **Commercial use, resale, or monetization in any form is strictly prohibited.**
> If you paid money for this software, you have been scammed — demand a refund immediately.

Share files with your Steam friends — for free, forever.

[中文文档](README.md)

---

## ⚠️ Disclaimers

- **SteamShare is NOT affiliated with, endorsed by, or connected to Valve Corporation or Steam in any way.** "Steam" and the Steam logo are registered trademarks of Valve Corporation. All trademarks and brand names belong to their respective owners. This project does not imply any endorsement by or association with Valve.
- **Valve may take action against users and this repository.** SteamShare operates on the Steam Workshop infrastructure of **App ID 480 (Spacewar)** — a developer sandbox for testing. Use of this software may result in Valve taking action against **your Steam account** (including account restrictions, item removal, or community restrictions) and/or **this GitHub repository** (takedown requests, API access restrictions). Moderate use among a small circle of friends reduces this risk, but excessive use is at your own discretion.
- **Downloaded files may contain viruses or malware.** Always scan downloaded files with appropriate security tools before opening or executing them. The authors of SteamShare bear no responsibility for any damage caused by files shared through this software.
- **This software is provided "AS IS", without warranty of any kind, express or implied.**

---

## ⚠️ License — READ CAREFULLY

**[CC BY-NC-SA 4.0](LICENSE)** — Creative Commons Attribution-NonCommercial-ShareAlike 4.0 International

> ### 🚫 COMMERCIAL USE IS STRICTLY PROHIBITED
> This software is **free for personal, non-commercial use only**. You MAY NOT:
> - Sell, resell, or charge money for this software
> - Bundle this software with any paid product or service
> - Use this software for any commercial purpose whatsoever
> - Monetize access to this software in any form
> 
> Any violation of these terms is a breach of the license.

- ✅ **Free to use** for personal, non-commercial purposes
- ✅ You may share and adapt the software under the same license terms (CC BY-NC-SA 4.0)
- ❌ **Commercial use is strictly prohibited**
- ❌ **Selling, reselling, or monetizing this software in any form is strictly prohibited**

> **If you paid money for this software, you have been scammed. Demand a refund immediately.**

---

## What is SteamShare?

SteamShare is a cross-platform (.NET, Windows & Linux) file-sharing tool that lets you easily share files with your [Steam](https://steamcommunity.com/workshop/) friends. Your files are stored as Workshop items under the Spacewar sandbox (App ID 480), and you share them directly with friends via secure share keys.

### Core Concepts

| Concept | Description |
|---|---|
| **File Group** | A local folder you want to share with friends, mapped to one Workshop item |
| **Share Key** | A `sshare+`-prefixed token — send it to friends to grant access; optionally password-protected with AES-256-GCM |
| **Visibility** | Private (just you) → Unlisted (friends with the key) → Public (everyone) |

### Features

- **Upload** folders to the Steam Workshop so friends can grab them
- **Download** file groups shared by friends via share keys (no Workshop subscription required)
- **Share** file groups with friends, with optional password protection
- **Manage** file groups: rename, move, delete, change visibility
- **View** download counts and ratings
- **Auto-restart** interrupted downloads and uploads on app startup (configurable)
- **Task system** with real-time progress tracking for all operations
- **Multi-language** support
- **Two interfaces**: GUI (Avalonia 12 Fluent) and CLI (Spectre.Console)
- **Auto-tracking** of owned and locally stored file groups

---

## Architecture

```
SteamShare.sln
└── src/
    ├── SteamShare.Core/       # Core library: models, services (SQLite, orchestrators, tasks)
    ├── SteamShare.CLI/        # Cross-platform command-line interface
    ├── SteamShare.UI/         # Cross-platform desktop GUI (Avalonia 12)
    └── SteamShare.Test/       # Unit & integration tests (xUnit + NSubstitute)
```

### Tech Stack

| Layer | Technology |
|---|---|
| Runtime | .NET 11 |
| Steam API | Steamworks.NET (standalone, GitHub releases) |
| GUI Framework | Avalonia 12 (Fluent theme) |
| CLI Framework | Spectre.Console |
| Architecture | MVVM + DI (CommunityToolkit.Mvvm, Microsoft.Extensions.DI) |
| Database | SQLite (Microsoft.Data.Sqlite) — tracking database, pending task persistence |
| Serialization | System.Text.Json |
| Logging | Serilog (console + file sinks) |
| Compression | K4os.Compression.LZ4 |
| Task System | In-memory task tree with concurrent dictionary + ambient context |
| Testing | xUnit, FluentAssertions, NSubstitute, Coverlet |

---

## How It Works

### Share Key Format

```
sshare+<base64-encoded-payload>
```

The payload is:
1. A JSON object: `{ "encrypted": false, "id": <published_file_id> }`
2. Compressed with **LZ4**
3. If password-protected: the `id` field is encrypted using **RFC 2898 (PBKDF2) + AES-256-GCM**, and `encrypted` is set to `true`

### Upload Flow

1. Pick a local folder you want to share with friends
2. SteamShare creates a Workshop item (App ID 480) and uploads the files
3. Metadata (name, virtual folder path, etc.) is stored in the item's Workshop metadata via `AddItemKeyValueTag`
4. Each item is tagged with `steamshare_tag=true` for identification
5. Default visibility is Private
6. Uploads are tracked as tasks with real-time progress; interrupted uploads auto-restart on next launch

### Download Flow

1. Get a share key from your friend
2. SteamShare parses the key, decrypting it if necessary
3. The Workshop item is downloaded to a temporary Steam directory
4. The files are moved to your local storage (no Workshop subscription needed)
5. SQLite tracking database records the download for management
6. Downloads are tracked as tasks with real-time progress; interrupted downloads auto-restart on next launch

### Task System & Auto-Restart

All upload and download operations run through the **task orchestrator system** (`UploadOrchestrator` / `DownloadOrchestrator`). Each operation is tracked as a task with real-time progress, status, and error reporting. Pending tasks are persisted in the SQLite database. When the application starts, it can automatically restart any interrupted downloads or uploads from where they left off — this behavior is configurable via Settings (`Auto-restart pending tasks on startup`).

---

## Getting Started

### Supported Platforms

| Platform | Status |
|---|---|
| **Windows** (10/11, x64) | Supported |
| **Linux** (x64) | Supported |
| **macOS** | Not supported |

SteamShare requires the Steam client. The macOS Steamworks native library is incompatible with this project, so macOS is not currently supported.

### Prerequisites

- [.NET 11 SDK](https://dotnet.microsoft.com/download/dotnet/11.0)
- **Steam client** installed and running (logged in)

### Installation

#### Windows

1. Install the [.NET 11 SDK](https://dotnet.microsoft.com/download/dotnet/11.0).
2. Install the [Steam client](https://store.steampowered.com/about/) and log in.
3. Download the latest `combined-sc-win-x64.zip` (self-contained, no .NET required) from [Releases](https://github.com/FrostyTwilight/SteamShare/releases), or pick another [variant](#release-artifacts).
4. Extract the archive to a folder of your choice.
5. Run `SteamShare.UI.exe` (GUI) or `SteamShare.CLI.exe` (CLI).

To build from source:

```powershell
git clone https://github.com/FrostyTwilight/SteamShare.git
cd SteamShare
dotnet restore
dotnet build
```

#### Linux

1. Install the [.NET 11 SDK](https://dotnet.microsoft.com/download/dotnet/11.0) using your distribution's package manager or the Microsoft install script.
2. Install the [Steam client](https://store.steampowered.com/about/) and log in.
3. Download the latest `combined-sc-linux-x64.zip` (self-contained, no .NET required) from [Releases](https://github.com/FrostyTwilight/SteamShare/releases), or pick another [variant](#release-artifacts).
4. Extract the archive to a folder of your choice.
5. Run `./SteamShare.UI` (GUI) or `./SteamShare.CLI` (CLI).

To build from source:

```bash
git clone https://github.com/FrostyTwilight/SteamShare.git
cd SteamShare
dotnet restore
dotnet build
```

### Run GUI

```bash
dotnet run --project src/SteamShare.UI
```

### Run CLI

```bash
dotnet run --project src/SteamShare.CLI -- <command> [options]
```

Available CLI commands: `upload`, `download`, `share`, `list`, `delete`, `rename`, `visibility`

### Run Tests

```bash
dotnet test
```

Tests use a dummy Steam API in CI environments and the real Steam API locally.

---

## Release Artifacts

Each [release](https://github.com/FrostyTwilight/SteamShare/releases) provides **6 variants × 2 platforms = 12 packages**:

### Variants

| Variant | Self-contained | Framework-dependent |
|---|---|---|
| **CLI** | `cli-sc-{rid}.zip` | `cli-fd-{rid}.zip` |
| **GUI** | `gui-sc-{rid}.zip` | `gui-fd-{rid}.zip` |
| **GUI + CLI** | `combined-sc-{rid}.zip` | `combined-fd-{rid}.zip` |

Where `{rid}` is `win-x64` or `linux-x64`.

### Self-contained vs Framework-dependent

| | Self-contained (`-sc-`) | Framework-dependent (`-fd-`) |
|---|---|---|
| **Runtime required** | ❌ None — .NET bundled inside | ✅ .NET 11 must be installed |
| **File size** | ~10–50 MB (varies by variant) | ~3–40 MB (varies by variant) |
| **Single-file** | ✅ Yes (single `.exe`) | ✅ Yes (single `.exe`) |

> **Note**: GUI+CLI combined packages (`combined-*`) are published as a directory — they are **not** single-file.

### Package Contents

Every package includes:

| File | Description |
|---|---|
| `SteamShare.UI.exe` (or no extension on Linux) | GUI application |
| `SteamShare.CLI.exe` (or no extension on Linux) | CLI tool (CLI and Combined variants only) |
| `steam_api64.dll` / `libsteam_api.so` | Steamworks native library |
| `steam_appid.txt` | App ID placeholder (480) |
| `README.md` | Chinese documentation |
| `README.en.md` | English documentation |
| `LICENSE` | CC BY-NC-SA 4.0 |

---

## Configuration

SteamShare uses a **SQLite database** (`steamshare.db`) stored in the user's application data directory. It tracks:

- Owned file group IDs and their local storage paths
- Download/upload progress state (pending tasks)
- User preferences (language, theme, auto-restart behavior)

---

## Warning to Users

SteamShare uses Steam's existing infrastructure for sharing files among friends. By using it, you acknowledge that:

1. **Valve may take action against your Steam account** if they detect unusual usage.
2. The Spacewar Workshop sandbox (App ID 480) is not guaranteed to remain available.
3. Share keys are the only way to access your files — keep them safe and only share with trusted friends.
4. You are solely responsible for the files you upload and download.
5. Scanning all downloaded files for malware is your responsibility.

---

## Contributing

Contributions are welcome under the same [CC BY-NC-SA 4.0](LICENSE) license. By contributing, you agree that your contributions will be licensed under the same terms.

---

## FAQ

**Why Steam Workshop?**
You and your friends are already on Steam — no extra accounts needed. Transfers go through Steam, no extra setup required.

**Why App ID 480 (Spacewar)?**
Spacewar is Valve's developer sandbox for testing Steamworks features. It has a working Workshop but no public Store page, so shared files are only visible to friends who have the key.

**Is this against Steam's ToS?**
The Spacewar Workshop is intended for developer testing. Moderate use among friends is unlikely to raise flags, but excessive usage carries risk. Use responsibly.

**Can Valve shut this down?**
Technically yes. Valve can restrict Workshop access for App ID 480. Using it moderately among a small circle of friends is the best way to keep a low profile.

**Why not use NuGet for Steamworks.NET?**
The NuGet package is outdated and unmaintained. SteamShare uses the latest standalone `.dll` from the [Steamworks.NET GitHub releases](https://github.com/rlabrecque/Steamworks.NET/releases).

---

*Steam is a registered trademark of Valve Corporation. SteamShare is not affiliated with Valve in any way.*
