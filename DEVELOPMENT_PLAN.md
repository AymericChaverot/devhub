# DevHub — Development Plan

> Status: **agreed** — stack and distribution chosen on 2026-07-07.
> This document is the single source of truth for *what* we build and *in which order*.
> The README describes the vision; this file describes the execution.

---

## 1. Decisions record

| # | Decision | Choice | Rationale |
|---|----------|--------|-----------|
| D1 | UI framework | **WinUI 3 (Windows App SDK)** | Matches the README's "native WinUI3 app" goal: true Fluent Design, Mica, native Windows 11 look. |
| D2 | Language / runtime | **C# / .NET 10 (current LTS)** | First-class WinUI 3 support; best library coverage for everything DevHub needs (winget COM, git, GitHub API, WMI). |
| D3 | Architecture pattern | **MVVM** via `CommunityToolkit.Mvvm` | Standard for WinUI 3; testable ViewModels, XAML bindings. |
| D4 | Local storage | **SQLite** (`Microsoft.Data.Sqlite`) for the project index; **JSON file** for user settings | Project scans must be cached and queryable; settings are tiny and human-readable. |
| D5 | Package installs | **winget** — COM API (`Microsoft.WindowsPackageManager.ComInterop`) with CLI fallback | winget is preinstalled on Win 10/11, handles UAC elevation per package, has a full catalog of dev tools. DevHub never elevates itself. |
| D6 | Git operations | **LibGit2Sharp** (clone, status, history) + `git` CLI fallback | In-process, no dependency on user's git for core features. |
| D7 | GitHub/GitLab API | **Octokit.NET** / **GitLabApiClient** (Phase 5, not MVP) | Mature, maintained clients. |
| D8 | Distribution | **Sideloaded installer** (MSIX from GitHub Releases), *not* Microsoft Store at first | Full freedom to launch installers/elevation; no Store review friction. Publish to `winget` itself later (`winget install devhub`). |
| D9 | CI/CD | **GitHub Actions** — build + test on PR, MSIX artifact on tag | Repo already lives on GitHub. |
| D10 | Logging | **Serilog** → rolling file in `%LOCALAPPDATA%\DevHub\logs` | Needed to debug scans/installs on user machines. |

Anything not listed here is an implementation detail — decide it in the PR that needs it.

---

## 2. Solution layout

```
devhub.sln
src/
  DevHub.App/            # WinUI 3 project: Views (XAML), App lifecycle, DI bootstrap
    Views/               #   ShellPage, EnvironmentsPage, ProjectsPage, SettingsPage
    ViewModels/          #   one VM per page + item VMs
    Assets/
  DevHub.Core/           # Pure .NET class library — NO WinUI dependency (testable)
    Models/              #   Project, ToolPackage, DetectedLanguage, IdeInfo, ...
    Services/            #   interfaces + implementations
      IPackageService    #   winget: search / install / uninstall / installed-version
      IProjectScanner    #   walks the Projects folder, detects languages & versions
      IProjectStore      #   SQLite persistence of the project index
      IIdeService        #   detect installed IDEs, open project in IDE
      IGitService        #   clone / basic status (LibGit2Sharp)
      ISettingsService   #   JSON settings (Projects folder path, preferred IDE, ...)
tests/
  DevHub.Core.Tests/     # xUnit; scanner + detection heuristics get the most coverage
.github/workflows/
  ci.yml                 # build + test on push/PR
  release.yml            # MSIX package + GitHub Release on tag v*
```

**Rule:** `DevHub.App` contains no business logic. Everything with behavior lives in
`DevHub.Core` behind an interface, injected with `Microsoft.Extensions.DependencyInjection`.

---

## 3. Data model (MVP)

```csharp
Project {
  Guid Id;
  string Name;              // folder name, editable later
  string Path;              // absolute
  string? GitRemoteUrl;     // origin, if a git repo
  DateTime LastOpenedAt;
  DateTime LastScannedAt;
  List<DetectedLanguage> Languages;
}

DetectedLanguage {
  string Language;          // "Node.js", "Rust", "Go", ".NET", "Python", ...
  string? Version;          // from manifest or toolchain file, if declared
  string EvidenceFile;      // e.g. "package.json"
}

ToolPackage {                // an installable environment shown in the catalog
  string WingetId;           // e.g. "OpenJS.NodeJS.LTS"
  string DisplayName;
  string Category;           // "Language", "Runtime", "Tool", "IDE"
  string? InstalledVersion;  // null = not installed
}
```

### Language detection heuristics (Phase 3)

Detection = presence of marker files at project root (one level deep max for monorepos).

| Marker file(s) | Language | Version source |
|---|---|---|
| `package.json` | Node.js | `engines.node`, `.nvmrc`, `.node-version` |
| `Cargo.toml` | Rust | `rust-toolchain(.toml)` |
| `go.mod` | Go | `go` directive in `go.mod` |
| `*.csproj`, `*.sln` | .NET | `<TargetFramework>` |
| `pyproject.toml`, `requirements.txt` | Python | `requires-python`, `.python-version` |
| `pom.xml`, `build.gradle(.kts)` | Java/Kotlin | `java.version` property when present |
| `composer.json` | PHP | `require.php` |
| `Gemfile` | Ruby | `.ruby-version` |
| `CMakeLists.txt`, `Makefile` | C/C++ | — |

The table is data-driven (a list of rules in code), so adding a language is one entry + one test.

---

## 4. Phases

Each phase ends with a working, demoable app. Do not start a phase before the previous
one's acceptance criteria pass.

### ✅ Phase 0 — Scaffolding *(foundation, no features)* — done 2026-07-07

1. Create the solution per the layout above; WinUI 3 packaged app template, .NET 10.
2. Wire up DI, Serilog, `ISettingsService` (JSON in `%LOCALAPPDATA%\DevHub\settings.json`).
3. Shell window: `NavigationView` with Environments / Projects / Settings items (empty pages), Mica backdrop, light/dark theme following system.
4. `ci.yml`: restore, build, run tests on every push/PR.
5. `release.yml`: on tag `v*`, produce a signed-or-self-signed MSIX and attach it to a GitHub Release.

**Accept when:** CI is green; the MSIX from a tag installs on a clean Windows 11 machine and the empty shell opens.

### ✅ Phase 1 — Environment installer *(README feature 1)* — done 2026-07-08
> Shipped beyond spec: search + category/status filters, per-tool version variants
> (side-by-side JDKs, Pythons, .NET SDKs...), real brand icons (Simple Icons SVGs).
> Deviation: winget CLI behind IPackageService instead of the COM API (progress is
> indeterminate); COM implementation can swap in later.

1. `IPackageService` over the winget COM API; fallback to parsing `winget` CLI output if COM init fails.
2. Curated catalog: a static `catalog.json` embedded in the app listing ~20 dev packages (Node LTS, Rust via `Rustlang.Rustup`, Go, Python, .NET SDK, Git, Docker Desktop, VS Code, JetBrains Toolbox, Windows Terminal, PowerShell 7, ...). Each entry = winget ID + display name + category.
3. Environments page: cards grouped by category, each showing installed state/version, with **Install** / **Uninstall** buttons and live progress (winget progress events → `ProgressBar`).
4. Elevation: none in DevHub — winget triggers UAC itself. Surface failures (user declined UAC, package not found) as `InfoBar` messages.
5. Concurrency: install queue, one winget operation at a time.

**Accept when:** on a machine without Node, clicking Install on "Node.js LTS" results in a working `node --version`, and the card flips to "Installed 22.x" without restarting the app.

### ✅ Phase 2 — Projects folder & listing *(README feature 2, part 1)* — done 2026-07-08

1. Settings page: pick the `Projects` folder (folder picker), stored in settings.
2. `IProjectScanner` v1: enumerate first-level subfolders → one `Project` each; read `origin` remote if it's a git repo. Runs off the UI thread; incremental (re-scan updates, doesn't wipe).
3. `IProjectStore`: SQLite cache so the list appears instantly on launch, refreshed by a background rescan.
4. Projects page: list/grid of projects with name, path, git badge; manual **Rescan** button; **Add from Git** (URL → `IGitService.Clone` into the Projects folder); **Remove** (with a dialog: *remove from list* vs *delete folder from disk* — deletion goes to the Recycle Bin, never permanent).

**Accept when:** pointing DevHub at a real Projects folder lists everything correctly in < 2 s from cache; cloning a public repo by URL adds it; removal behaves per the chosen option.

### ✅ Phase 3 — Language detection & open-in-IDE *(README features 2+3, completes MVP)* — done 2026-07-08
> Shipped beyond spec: project detail page (hero, README rendered via Markdig +
> WebView2, git remote link) and a **per-project** IDE preference instead of a
> single global default. Search/filter on the Projects page is still to come.

1. Implement the detection heuristics table above inside `IProjectScanner`; unit-test every rule with fixture folders.
2. Project cards show language badges + versions.
3. `IIdeService`: detect installed IDEs — VS Code (`code` on PATH / registry), Visual Studio (`vswhere.exe`), JetBrains IDEs (Toolbox `state.json` + default install dirs). "Open in ▸" flyout per project + a default IDE in Settings.
4. Filter/search box on the Projects page (by name and by language).

**Accept when:** a folder containing Node, Rust and .NET projects shows correct badges for each; "Open in VS Code" opens the right folder; all detection rules have passing tests.

### 🏁 Release v0.1.0 — in flight (2026-07-13)

Decision: preview releases are **self-signed** (paid cert / Azure Trusted Signing
deferred). release.yml signs the MSIX with a PFX from GitHub secrets
(`SIGNING_CERT_PFX_BASE64` + `SIGNING_CERT_PASSWORD`) and attaches the public
`DevHub-signing.cer` with one-time trust instructions; the README documents the
install flow. Releases are drafted by CI, then published manually — the in-app
updater only sees published releases. Remaining: push the `v0.1.0` tag, publish.
**Everything below is post-MVP and intentionally not specified in detail yet** — each future
phase gets its own design section in this file before work starts.

### ✅ Phase 4 — Git integration — done 2026-07-08
Full Git tab on every project page: lane-laid-out **commit graph** (custom
topological layout in `GitGraphBuilder`, unit-tested), branch switch/create,
working-tree status, ahead/behind, and fetch/pull/push. Local operations use
LibGit2Sharp; network operations shell out to the git CLI so Git Credential
Manager handles authentication (like an IDE). Commit/stage UI: future work.

### 🚧 Phase 5 — GitHub/GitLab integration — started 2026-07-12
Shipped so far: `IForgeService` abstraction with GitHub + GitLab REST implementations
(personal access tokens; validated via `/user`), `ITokenStore` over the **Windows Credential
Manager** (tokens never touch settings.json), account connect/disconnect UI in Settings,
a three-feed "Your work" card on the dashboard (reviews requested / my PRs / assigned
issues, aggregated across both forges), and a per-project GitHub/GitLab tab on the
project detail page (repo description + stars/forks, open PRs/MRs, open issues, and
CI runs — GitHub Actions / GitLab pipelines — with status icons; shown when the git
remote matches a connected account). PR/issue rows carry Octicon glyphs. GitHub feeds
use the /issues endpoint where possible (fine-grained-token friendly); review requests
still need the search API. Remaining: review threads, PR detail view, OAuth device
flow (needs a registered OAuth app client id), self-hosted GitLab base URL.

### ✅ Phase 7 — Toolbox — done 2026-07-13
"Toolbox" nav tab with everyday utilities, logic in `DevToolbox` (DevHub.Core, unit
tested): text ⇄ Base64/hex/binary/URL converter, MD5/SHA-1/SHA-256/SHA-512 hashes,
JWT decoder (no signature verification), JSON formatter/minifier, UUID generator,
unix-timestamp inspector, regex tester (1 s timeout guard), line-based text diff
(LCS), color converter (hex/RGB/HSL + swatch), password generator (crypto RNG),
and a small API client (method/URL/headers/body → status, duration, response
headers, pretty-printed JSON body).

### ✅ Dashboard polish & Pomodoro — done 2026-07-13
Weather location is user-configurable (Settings → General; empty = automatic IP
lookup) via Open-Meteo geocoding, the card shows "City, Country" and grew a bit,
and a blurred Win2D sun/moon + clouds/rain/snow decoration (driven by the WMO code
and Open-Meteo's is_day) sits in the Home page's top-left background. A Pomodoro
timer lives in the navigation pane footer — visible on every page while running —
with Focus/Break presets, custom durations, pause/stop, and a toast notification
(AppNotificationManager) when the countdown ends.

### ✅ In-app updater — done 2026-07-13
`GitHubUpdateService` polls the repo's latest GitHub Release at startup (silent, never
blocks) and compares its tag to the package identity version. When newer: dashboard
InfoBar with "Install now" (downloads the .msix/.msixbundle asset with progress, then
hands off to Windows App Installer) and a release-notes link; Settings → About gets a
manual "Check for updates" button. Requires releases tagged `v*` with an MSIX asset
(release.yml already does this).

### ✅ Run configurations — done 2026-07-13
Accent "Run" split-button on the project hero: named launch commands per project
(dev, test…) stored in settings, launched in Windows Terminal (PowerShell fallback)
inside the project folder. Flyout to pick/add/remove configurations; the main button
runs the first one.

### ✅ Phase 6 — Developer dashboard — done 2026-07-12
Home tab (default page): greeting with the user's first name, weather (ipwho.is +
Open-Meteo, keyless, 20-min cache, hides offline), CPU/memory ring gauges refreshed
every 5 s (WMI), GPU/OS cards, per-drive usage bars, tool-updates card wired to the
Environments inventory, recently-opened projects (tracked in settings), and the Phase 5
review feed.

---

## 5. Conventions

- **Branching:** `main` is protected; feature branches `feat/<topic>`, PRs required, CI must pass.
- **Commits:** Conventional Commits (`feat:`, `fix:`, `chore:` ...).
- **Versioning:** SemVer; `v0.x` until the MVP is stable in daily use.
- **Tests:** every `DevHub.Core` service gets unit tests; detection heuristics are the priority. UI is not unit-tested for now.
- **No blocking the UI thread** — scans, winget calls and git operations are all `async` and cancellable.

---

## 6. Open questions (decide when the phase starts)

- Phase 1: exact contents of the curated catalog — pick the ~20 packages together.
- Phase 2: monorepo handling — is one top-level folder always one project, or should nested git repos be split?
- ~~Release: code-signing certificate (paid) vs self-signed + install instructions vs Azure Trusted Signing.~~ → self-signed for previews (v0.1.0); revisit paid/Trusted Signing before a public launch.
- Phase 5: GitHub first and GitLab later, or both at once?
