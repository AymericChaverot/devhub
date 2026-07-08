# DevHub

**A central hub for developing on Windows.**

DevHub is a native Windows application (WinUI 3) that streamlines the two things every
developer does on a new machine: **setting up tooling** and **working with projects**.
Install language runtimes and dev tools in one click, and browse every project on your
machine with automatic language & framework detection, README preview and
open-in-your-favorite-IDE — all in a fast, Fluent-design app.

> 🚧 DevHub is in active development (v0.1.0 preview). The MVP is complete;
> see the [roadmap](#roadmap) for what is coming next.

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

### 📁 Projects — scan, inspect, open

- Point DevHub at your `Projects` folder: every project is listed instantly (SQLite
  cache) and refreshed by background scans.
- **Automatic detection** of languages *and* frameworks with their versions, straight
  from the manifests: `package.json`, `Cargo.toml`, `go.mod`, `*.csproj`,
  `pyproject.toml`, `pom.xml`, `build.gradle(.kts)`, `composer.json`, `Gemfile` and more.
  Next.js, Angular, Astro, Vite, Tauri, Ratatui, ASP.NET Core, Django, Rails, Laravel...
  each shows as a colored badge.
- **Project pages**: hero with the project's tech stack, blurred brand-logo artwork,
  rendered README, a *View on GitHub / GitLab* button, folder rename, and per-project
  IDE choice (each project can open in a different editor — VS Code, Visual Studio,
  JetBrains IDEs, Cursor, Zed, Sublime Text are auto-detected).
- **Subprojects for monorepos**: split a repository into explorable subprojects
  (backend, frontend, docs...), each with its own tech badges, README and IDE shortcut.
- Add projects by **cloning from a Git URL**; remove them from the list or send the
  folder to the Recycle Bin (never a permanent delete).

### ⚙️ A real settings page

Projects folder, live light/dark theme, quick access to logs and app data, version
info and open-source licenses.

---

## Getting started

### Requirements

- Windows 10 2004 (build 19041) or later — Windows 11 recommended
- [winget](https://learn.microsoft.com/windows/package-manager/) (preinstalled on
  Windows 11 and recent Windows 10)

### Install

Grab the MSIX from the [latest release](https://github.com/AymericChaverot/devhub/releases)
(unsigned preview packages: enable Developer Mode to sideload), or build from source:

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

- **Git integration** — commit history, branches and working-tree status per project.
- **GitHub / GitLab integration** — pull requests, issues and review threads.
- **Developer dashboard** — machine stats, PRs awaiting review, recent projects.
- Code signing and distribution through winget itself.

---

## Credits

Brand icons by [Simple Icons](https://simpleicons.org) (CC0) and
[Devicon](https://devicon.dev) (MIT). All logos are trademarks of their respective
owners. Third-party library licenses are listed in the app under
**Settings → Open-source licenses**.
