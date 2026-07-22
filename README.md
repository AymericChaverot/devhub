# DevHub

**A central hub for developing on Windows.**

DevHub is a native Windows application (WinUI 3) that streamlines the key developer tasks on a new machine: **setting up tooling**, **managing system environment variables**, and **organizing projects**. Install language runtimes and dev tools in one click, clean up and diagnose your system PATH, and browse every project on your machine with automatic language & framework detection, README preview and open-in-your-favorite-IDE — all in a fast, Fluent-design app.

> 🚧 DevHub is in active development (v0.1.8 preview).
> See the [roadmap](#roadmap) for what is coming next.

---

## Features

### 🧰 Environments — one-click tool installation

- A curated catalog of **35+ development tools**: languages and runtimes (Node.js,
  Python, Java, .NET, Rust, Go, PHP, Ruby, Deno, Bun, Zig...), version control, editors
  and IDEs, containers and cloud CLIs.
- **Version pickers for side-by-side installs** — JDK 25/21/17/11, Python 3.13/3.12/3.11,
  .NET SDK 10/9/8, Node.js LTS/Current can coexist; DevHub installs the exact variant you pick.
- Installations run through **winget** (elevation is handled by winget itself, DevHub
  never asks for admin rights).
- Search, category and installed-state filters; live detection of what is already on
  your machine, with real brand logos.

### 🔑 Environment Variables — user & system manager

- **Full User & System scope editor**: Inspect, add, edit, and delete environment variables.
- **Interactive PATH/list editor**: Parses and displays semicolon-delimited lists (such as `PATH`, `PATHEXT`, or `PSModulePath`) as separate, individually-editable items. Move entries up/down with dedicated buttons.
- **Path diagnostics & analyzer**: Instantly checks and flags invalid/dead paths (directories/files that do not exist on disk) and duplicate entries.
- **One-click "Repair"**: Clean up messy list variables (like `PATH`) by automatically removing all dead paths and duplicate entries in a single action.
- **Safe elevation**: Batch writes to machine-scope system variables into a single elevated operation (`.reg` script execution), meaning you get exactly one UAC elevation prompt per save instead of one per variable.
- **Live propagation**: Automatically broadcasts a system-wide `WM_SETTINGCHANGE` message, notifying terminal shells (PowerShell, Command Prompt) and IDEs to refresh their environment variables instantly without requiring a system restart or user logoff.
- **Backup & restore**: Export a complete backup of both User and System environment variables to a JSON file for quick migration or safe recovery.

### 📁 Projects — scan, inspect, open & create

- Point DevHub at your `Projects` folder: every project is listed instantly (SQLite
  cache) and refreshed by background scans.
- **Project Creation Wizard**: Create brand-new projects from scratch using popular templates (Next.js, React Vite, Rust/Cargo, Go, Python, .NET Core WebApi) or start with a clean empty folder.
- **Dependency checks**: Checks if the required runtime toolchain (Node.js, Cargo, Go, Python, .NET SDK) is installed before scaffolding. If missing, it provides a shortcut to install it via the Environments tab.
- **Git & forge initialization**: Automatically initialize a Git repository, customize the default branch, link a remote URL (paste an existing repo URL or create it directly on your connected GitHub/GitLab account), and push the initial commit.
- **Default IDE configuration**: Set a preferred editor/IDE for the new project to open it directly upon creation.
- **Automatic tech detection**: Detects languages *and* frameworks with their versions straight from manifests (`package.json`, `Cargo.toml`, `go.mod`, `*.csproj`, `pyproject.toml`, `pom.xml`, `build.gradle(.kts)`, `composer.json`, `Gemfile`, etc.), displaying them as clean, color-coded badges (Next.js, Angular, Astro, Vite, Tauri, ASP.NET Core, Django, Rails, Laravel, etc.).
- **Project pages**: Hero header with tech stack, blurred logo artwork, rendered README preview, *View on GitHub / GitLab* button, inline folder renaming, and per-project IDE selection (VS Code, Visual Studio, JetBrains, Cursor, Zed, Sublime Text are auto-detected).
- **Subprojects for monorepos**: Split a large repository into explorable subprojects (backend, frontend, docs, etc.), each with its own tech badges, README, and IDE preference.
- **Recycle Bin deletion**: Send project folders to the Windows Recycle Bin for safety, avoiding permanent accidental deletion.

### 🏠 Dashboard

- **System at a glance**: live CPU and memory ring gauges, GPU and OS cards, per-drive
  usage bars — refreshed every few seconds.
- **Weather** with a dynamic sky: gradient backdrop that follows the real conditions
  (starry at night, animated rain, snow and lightning), automatic IP location or a
  city of your choice.
- **Your work across forges**: reviews requested, your open PRs, assigned issues, and recently closed items aggregated across GitHub and GitLab.
- Recently opened projects and a tool-updates card with one-click *Update all*.

### 🔀 Git, GitHub & GitLab

- **Git tab on every project**: commit graph with branch lanes, branch switch/create,
  working-tree status, ahead/behind, fetch/pull/push (credentials via Git Credential
  Manager).
- **Connect GitHub and GitLab** with personal access tokens — stored in the Windows
  Credential Manager, never in a file. Repo pages show description, stars/forks, open
  PRs/MRs, open issues, recently closed items, and CI runs (GitHub Actions / GitLab pipelines) with live
  status colors.
- **Run configurations**: define named launch commands per project (dev, test...) and
  start them in Windows Terminal from the accent Run button.

### 🧪 Toolbox & Pomodoro

- Everyday utilities, all offline: Base64/hex/binary/URL converter, hash generator,
  JWT decoder, JSON formatter, UUIDs, unix timestamps, regex tester, text diff, color
  converter, password generator — plus a small API client.
- A **Pomodoro timer** with its own page, a remaining-minutes badge in the sidebar and
  a toast notification when time is up.

### ⚙️ A real settings page

Projects folder, weather location, appearance settings (light/dark/system theme), language selection, GitHub/GitLab accounts, live light/dark theme,
in-app updates, quick access to logs and app data, version info and open-source
licenses.

---

## Getting started

### Requirements

- Windows 10 2004 (build 19041) or later — Windows 11 recommended
- [winget](https://learn.microsoft.com/windows/package-manager/) (preinstalled on
  Windows 11 and recent Windows 10)

### Install

Preview packages are signed with a self-signed certificate, so Windows needs to trust it once. You can install DevHub using either the automatic script or manually.

#### Option 1: Automatic Install (Recommended)

Run the following command in a standard PowerShell window (no administrator rights needed to start):

```powershell
irm https://raw.githubusercontent.com/AymericChaverot/devhub/main/install.ps1 | iex
```

This interactive script will:
1. Verify system requirements (OS version and winget availability).
2. Check if the latest version is already installed on your machine.
3. Detect if the package signing certificate is already trusted (if it is, **no Administrator elevation** is requested).
4. If elevation is required to trust the certificate, ask for your permission before requesting UAC elevation.
5. Download, unblock, and register the application.
6. Ask if you want to launch the application immediately.


#### Option 2: Manual Install

1. Grab `DevHub-signing.cer` and the `.msix` from the [latest release](https://github.com/AymericChaverot/devhub/releases).
2. In an **administrator** PowerShell (the store must be *Local Machine* — the certificate wizard's "Current User" default will not work):

   ```powershell
   Import-Certificate -FilePath .\DevHub-signing.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople
   ```

3. Double-click the `.msix` — App Installer does the rest.

### Updating

Once installed, future updates are handled automatically or in-app:
- DevHub silently checks for updates at startup without blocking the user.
- If a new version is available, a notification banner appears on your dashboard with an "Install now" option.
- You can also manually check for updates via **Settings → About → Check for updates**.
- The certificate import step is a one-time operation; subsequent updates will install seamlessly.

Or build from source:

```powershell
git clone https://github.com/AymericChaverot/devhub.git
cd devhub
dotnet build devhub.slnx -p:Platform=x64
.\src\DevHub.App\bin\x64\Debug\net10.0-windows10.0.22621.0\win-x64\DevHub.App.exe
```

Building requires the **.NET 10 SDK** — everything else (Windows App SDK, WinUI) is
restored from NuGet.

### Run the tests

```powershell
dotnet test tests/DevHub.Core.Tests/DevHub.Core.Tests.csproj
```

---

## Architecture

| Layer | Project | Notes |
|---|---|---|
| UI | `src/DevHub.App` | WinUI 3 (Windows App SDK 2.2), MVVM with CommunityToolkit, Win2D for hero effects, WebView2 + Markdig for README rendering |
| Logic | `src/DevHub.Core` | Pure .NET class library: winget CLI integration, project scanner, language/framework detectors, SQLite cache, LibGit2Sharp |
| Tests | `tests/DevHub.Core.Tests` | xUnit — detectors, scanner, store, git and file operations |

Design decisions, phase history and conventions live in
[`DEVELOPMENT_PLAN.md`](DEVELOPMENT_PLAN.md).

---

## Roadmap

- **Deeper forge integration** — PR detail view with review threads, CI checks on PR
  rows, OAuth device flow instead of tokens, self-hosted GitLab.
- **Commit & stage from the Git tab.**
- Trusted code signing and distribution through winget itself.

---

## Credits

Brand icons by [Simple Icons](https://simpleicons.org) (CC0) and
[Devicon](https://devicon.dev) (MIT). All logos are trademarks of their respective
owners. Third-party library licenses are listed in the app under
**Settings → Open-source licenses**.
