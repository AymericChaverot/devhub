using System.Diagnostics;
using DevHub.App.ViewModels;
using DevHub.Core.Models;
using DevHub.Core.Services;
using Markdig;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.Svg;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using Serilog;

namespace DevHub.App.Views;

public sealed partial class ProjectDetailPage : Page
{
    private const int GraphPageSize = 100;
    private const double FallbackExpandedHeroHeight = 190;
    private const double CollapsedHeroHeight = 68;

    private readonly ISettingsService _settings;
    private readonly IIdeService _ides;
    private readonly IGitService _git;
    private readonly System.Collections.ObjectModel.ObservableCollection<GraphCommitRow> _graphRows = [];
    private Project? _project;
    private bool _initializingBranches;
    private IReadOnlyList<Core.Models.GitBranchInfo> _branchInfos = [];
    private int _graphCount;
    private int _graphMaxLane;
    private bool _graphComplete;
    private bool _graphLoadingMore;
    private bool _graphScrollHooked;
    private bool _filesLoaded;
    private bool _readmeScrollHooked;
    private bool _fileViewScrollHooked;
    private double _heroProgress = -1;
    private double? _expandedHeroHeightSnapshot;
    private Uri? _remoteWebUrl;
    private bool _initializingIdePicker;

    private string? _langDecorPath;
    private string? _ideDecorPath;
    private CanvasSvgDocument? _langSvg;
    private CanvasSvgDocument? _ideSvg;

    public ProjectDetailPage()
    {
        _settings = App.Services.GetRequiredService<ISettingsService>();
        _ides = App.Services.GetRequiredService<IIdeService>();
        _git = App.Services.GetRequiredService<IGitService>();
        InitializeComponent();

        // Win2D canvases must be torn down explicitly or they leak their device.
        Unloaded += (_, _) =>
        {
            LangDecor.RemoveFromVisualTree();
            IdeDecor.RemoveFromVisualTree();
        };
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        var (project, relativeDisplay) = e.Parameter switch
        {
            ProjectDetailArgs args => (args.Project, args.RelativePathDisplay),
            Project p => (p, null),
            _ => (null, null),
        };
        if (project is null)
        {
            return;
        }

        _project = project;
        ProjectName.Text = project.Name;
        ProjectPath.Text = relativeDisplay ?? project.Path;
        RefreshSubprojectButtons();
        var dark = ActualTheme == ElementTheme.Dark;
        TechList.ItemsSource = new List<TechLine>(
        [
            .. project.Languages.Select(l => TechLine.For(l, dark)),
            .. project.Frameworks.Select(f => TechLine.For(f, dark)),
        ]);

        ShowTechDecoration(project);
        ShowRemoteButton(project.GitRemoteUrl);

        await PopulateIdePickerAsync(project);
        await LoadGitAsync(project);
        await LoadReadmeAsync(project.Path);
    }

    private void SectionBar_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        var selected = sender.SelectedItem;
        ReadmeContainer.Visibility = selected == ReadmeTab ? Visibility.Visible : Visibility.Collapsed;
        GitContainer.Visibility = selected == GitTab ? Visibility.Visible : Visibility.Collapsed;
        FilesContainer.Visibility = selected == FilesTab ? Visibility.Visible : Visibility.Collapsed;

        if (selected == FilesTab && !_filesLoaded)
        {
            PopulateFileTreeRoot();
        }

        // The graph's ScrollViewer only exists once the Git tab has been realized.
        if (selected == GitTab)
        {
            HookGraphScroll();
        }
    }

    /// <summary>
    /// 0 = full hero, 1 = collapsed to just the title. Driven by content scrolling;
    /// the decor logos grow and drift while the details fade out.
    /// </summary>
    private void SetHeroCollapse(double progress)
    {
        var p = Math.Clamp(progress, 0, 1);
        if (Math.Abs(p - _heroProgress) < 0.01)
        {
            return;
        }

        _heroProgress = p;
        var inv = 1 - p;

        ProjectPath.Opacity = inv;
        ProjectPath.MaxHeight = (22 * inv) + 0.01;
        TechList.Opacity = inv;
        TechList.MaxHeight = (240 * inv) + 0.01;
        ActionsRow.Opacity = inv;
        ActionsRow.MaxHeight = (48 * inv) + 0.01;
        ActionsRow.IsHitTestVisible = p < 0.5;
        HeroTopButtons.Opacity = inv;
        HeroTopButtons.MaxHeight = (480 * inv) + 0.01;
        HeroTopButtons.IsHitTestVisible = p < 0.5;

        // The title StackPanel's Spacing still reserves a gap for each child even when
        // faded/shrunk to near-zero; fully collapsing them removes that gap so the card
        // truly shrinks to just the title, not the title plus three leftover gutters.
        var fullyCollapsed = p >= 0.999;
        ProjectPath.Visibility = fullyCollapsed ? Visibility.Collapsed : Visibility.Visible;
        TechList.Visibility = fullyCollapsed ? Visibility.Collapsed : Visibility.Visible;
        ActionsRow.Visibility = fullyCollapsed ? Visibility.Collapsed : Visibility.Visible;

        // The content Grid's own top/bottom padding (24px each) was eating most of the
        // "collapsed" budget regardless of HeroCard's height, so it shrinks too.
        const double expandedPadding = 24;
        const double collapsedPadding = 6;
        var topBottomPadding = expandedPadding - ((expandedPadding - collapsedPadding) * p);
        HeroContentGrid.Padding = new Thickness(24, topBottomPadding, 224, topBottomPadding);

        double heroCardHeight;
        if (p <= 0.001)
        {
            // Fully expanded: let the card size itself to whatever this project actually
            // needs (tech lines, subprojects...) instead of a fixed height that would
            // either crop rich content or leave sparse content floating in extra space.
            // MinHeight is the floor that keeps sparse-content cards from looking tiny.
            HeroCard.MinHeight = 150;
            HeroCard.Height = double.NaN;
            _expandedHeroHeightSnapshot = null;
            heroCardHeight = HeroCard.ActualHeight > 0 ? HeroCard.ActualHeight : FallbackExpandedHeroHeight;
        }
        else
        {
            // Capture the natural height once, right as collapsing starts, so the
            // animation shrinks from THIS project's real expanded height, not a guess.
            _expandedHeroHeightSnapshot ??=
                HeroCard.ActualHeight > 0 ? HeroCard.ActualHeight : FallbackExpandedHeroHeight;
            var expanded = _expandedHeroHeightSnapshot.Value;
            heroCardHeight = expanded - ((expanded - CollapsedHeroHeight) * p);

            // The MinHeight floor above would otherwise clamp the shrink target back up
            // (MinHeight always wins over a smaller explicit Height), so it's lifted for
            // as long as the card is collapsing.
            HeroCard.MinHeight = 0;
            HeroCard.Height = heroCardHeight;
        }

        // At rest, the button keeps its small default (natural) size, top-aligned next
        // to the multi-line title block — no explicit Height. Once collapsing starts it
        // grows (expressed as a plain number rather than VerticalAlignment=Stretch: a
        // Stretch child with an unset Height inside this Auto-sized row gets measured
        // with an effectively infinite available height, which blew both the button and
        // the card up to a huge size) and re-centers on the now-single-line title,
        // since Top alignment would otherwise dump all the leftover margin at the bottom.
        const double naturalButtonSize = 32;
        if (p <= 0.001)
        {
            BackButton.Height = double.NaN;
            BackButton.VerticalAlignment = VerticalAlignment.Top;
        }
        else
        {
            // A small margin is left around the button even at full collapse, rather
            // than filling every last pixel of the padded content height.
            const double collapsedMargin = 8;
            var availableContentHeight =
                Math.Max(naturalButtonSize, heroCardHeight - (topBottomPadding * 2) - collapsedMargin);
            BackButton.Height = naturalButtonSize + ((availableContentHeight - naturalButtonSize) * p);
            BackButton.VerticalAlignment = p > 0.5 ? VerticalAlignment.Center : VerticalAlignment.Top;
        }

        LangDecorTransform.ScaleX = LangDecorTransform.ScaleY = 1 + (0.45 * p);
        LangDecorTransform.TranslateX = 70 * p;
        LangDecorTransform.TranslateY = -45 * p;
        LangDecorTransform.Rotation = -10 + (16 * p);
        IdeDecorTransform.ScaleX = IdeDecorTransform.ScaleY = 1 + (0.4 * p);
        IdeDecorTransform.TranslateX = -60 * p;
        IdeDecorTransform.TranslateY = 35 * p;
        IdeDecorTransform.Rotation = 10 - (16 * p);
        LangDecor.Opacity = 0.20 * (1 - (0.35 * p));
        IdeDecor.Opacity = 0.20 * (1 - (0.35 * p));
    }

    /// <summary>Bridges the WebView's scroll position out to the collapsing hero.</summary>
    private async Task HookWebViewScrollAsync(WebView2 view)
    {
        await view.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
            """
            window.addEventListener('scroll', function () {
                try { window.chrome.webview.postMessage(String(window.scrollY || 0)); } catch (e) { }
            }, { passive: true });
            """);
        view.CoreWebView2.WebMessageReceived += (_, e) =>
        {
            if (double.TryParse(
                e.TryGetWebMessageAsString(),
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture,
                out var y))
            {
                SetHeroCollapse(y / 160);
            }
        };
    }

    private async Task LoadGitAsync(Project project)
    {
        if (!project.IsGitRepository)
        {
            GitTab.Visibility = Visibility.Collapsed;
            return;
        }

        var info = await _git.GetRepositoryInfoAsync(project.Path);
        if (info is null)
        {
            GitTab.Visibility = Visibility.Collapsed;
            return;
        }

        GitTab.Visibility = Visibility.Visible;

        _initializingBranches = true;
        _branchInfos = info.Branches;
        BranchPicker.ItemsSource = info.Branches
            .Select(b => b.IsRemote ? $"{b.Name}  ·  remote" : b.Name)
            .ToList();
        BranchPicker.SelectedIndex = info.Branches.ToList().FindIndex(b => b.IsCurrent);
        BranchPicker.PlaceholderText = info.CurrentBranch;
        BranchPicker.IsEnabled = !info.IsDetached && info.Branches.Count > 1;
        _initializingBranches = false;

        AheadBehindText.Text = (info.Ahead, info.Behind) switch
        {
            (0, 0) => "",
            (var a, 0) => $"↑{a}",
            (0, var b) => $"↓{b}",
            var (a, b) => $"↑{a} ↓{b}",
        };

        var branchWord = info.Branches.Count == 1 ? "branch" : "branches";
        var commitWord = info.TotalCommits == 1 ? "commit" : "commits";
        GitCountsText.Text = $"{info.TotalCommits:N0} {commitWord} · {info.Branches.Count} {branchWord}";

        if (info.IsDirty)
        {
            string[] parts =
            [
                info.Modified > 0 ? $"{info.Modified} modified" : "",
                info.Added > 0 ? $"{info.Added} added" : "",
                info.Removed > 0 ? $"{info.Removed} removed" : "",
                info.Untracked > 0 ? $"{info.Untracked} untracked" : "",
            ];
            GitStatusText.Text = string.Join(" · ", parts.Where(p => p.Length > 0));
        }
        else
        {
            GitStatusText.Text = "Clean working tree";
        }

        _graphCount = GraphPageSize;
        var graph = await _git.GetCommitGraphAsync(project.Path, _graphCount);
        _graphComplete = graph is null || graph.Rows.Count < _graphCount;
        _graphMaxLane = graph?.MaxLane ?? 0;
        _graphRows.Clear();
        foreach (var row in graph?.Rows ?? [])
        {
            _graphRows.Add(new GraphCommitRow(row, _graphMaxLane));
        }

        GraphList.ItemsSource ??= _graphRows;
        HookGraphScroll();

        var cliAvailable = await _git.IsGitCliAvailableAsync();
        FetchButton.IsEnabled = PullButton.IsEnabled = PushButton.IsEnabled = cliAvailable;
        if (!cliAvailable)
        {
            GitStatusText.Text += "  ·  git CLI not found — fetch/pull/push unavailable";
        }
    }

    /// <summary>
    /// Extends the graph window when the list is scrolled near its bottom, and drives the
    /// collapsing hero. The ListView's internal ScrollViewer isn't realized (its control
    /// template hasn't run) while the Git tab starts hidden, so this retries on every
    /// layout pass until the tab becomes visible and the template is applied.
    /// </summary>
    private void HookGraphScroll()
    {
        if (_graphScrollHooked)
        {
            return;
        }

        var scrollViewer = FindScrollViewer(GraphList);
        if (scrollViewer is null)
        {
            GraphList.LayoutUpdated -= GraphList_LayoutUpdated;
            GraphList.LayoutUpdated += GraphList_LayoutUpdated;
            return;
        }

        _graphScrollHooked = true;
        GraphList.LayoutUpdated -= GraphList_LayoutUpdated;
        scrollViewer.ViewChanged += (_, _) =>
        {
            SetHeroCollapse(scrollViewer.VerticalOffset / 160);

            if (scrollViewer.VerticalOffset >= scrollViewer.ScrollableHeight - 400)
            {
                _ = LoadMoreGraphAsync();
            }
        };
    }

    private void GraphList_LayoutUpdated(object? sender, object e) => HookGraphScroll();

    private async Task LoadMoreGraphAsync()
    {
        if (_graphComplete || _graphLoadingMore || _project is null)
        {
            return;
        }

        _graphLoadingMore = true;
        try
        {
            _graphCount += 150;
            var graph = await _git.GetCommitGraphAsync(_project.Path, _graphCount);
            if (graph is null)
            {
                return;
            }

            _graphComplete = graph.Rows.Count < _graphCount;

            // The layout is deterministic, so a wider window only appends rows —
            // unless a new lane appeared, which changes the graph column width.
            if (graph.MaxLane != _graphMaxLane)
            {
                _graphMaxLane = graph.MaxLane;
                _graphRows.Clear();
            }

            foreach (var row in graph.Rows.Skip(_graphRows.Count))
            {
                _graphRows.Add(new GraphCommitRow(row, _graphMaxLane));
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Loading more commits failed");
        }
        finally
        {
            _graphLoadingMore = false;
        }
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        for (var i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i);
            if (child is ScrollViewer viewer)
            {
                return viewer;
            }

            if (FindScrollViewer(child) is { } nested)
            {
                return nested;
            }
        }

        return null;
    }

    private async void BranchPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializingBranches
            || _project is null
            || BranchPicker.SelectedIndex < 0
            || BranchPicker.SelectedIndex >= _branchInfos.Count)
        {
            return;
        }

        var branch = _branchInfos[BranchPicker.SelectedIndex].Name;
        if (_branchInfos[BranchPicker.SelectedIndex].IsCurrent)
        {
            return;
        }

        try
        {
            Log.Information("Checking out {Branch} in {Path}", branch, _project.Path);
            await _git.CheckoutAsync(_project.Path, branch);
            ShowGitResult($"Switched to branch '{branch}'.", success: true);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Checkout of {Branch} failed", branch);
            ShowGitResult(ex.Message, success: false);
        }

        // Reload regardless of outcome so the picker reflects the repository's true state.
        await LoadGitAsync(_project);
    }

    private async void NewBranch_Click(object sender, RoutedEventArgs e)
    {
        if (_project is null)
        {
            return;
        }

        var nameBox = new TextBox { PlaceholderText = "feature/my-branch", MinWidth = 320 };
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Create branch",
            Content = nameBox,
            PrimaryButtonText = "Create & switch",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary
            || nameBox.Text.Trim() is not { Length: > 0 } name)
        {
            return;
        }

        try
        {
            await _git.CreateBranchAsync(_project.Path, name);
            ShowGitResult($"Created and switched to '{name}'.", success: true);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Branch creation failed");
            ShowGitResult(ex.Message, success: false);
        }

        await LoadGitAsync(_project);
    }

    private void Fetch_Click(object sender, RoutedEventArgs e) => _ = RunGitOperationAsync("Fetch", _git.FetchAsync);

    private void Pull_Click(object sender, RoutedEventArgs e) => _ = RunGitOperationAsync("Pull", _git.PullAsync);

    private void Push_Click(object sender, RoutedEventArgs e) => _ = RunGitOperationAsync("Push", _git.PushAsync);

    private async Task RunGitOperationAsync(
        string operationName, Func<string, CancellationToken, Task<string>> operation)
    {
        if (_project is null || GitBusyRing.IsActive)
        {
            return;
        }

        GitBusyRing.IsActive = true;
        FetchButton.IsEnabled = PullButton.IsEnabled = PushButton.IsEnabled = false;
        try
        {
            Log.Information("{Operation} in {Path}", operationName, _project.Path);
            var output = await operation(_project.Path, CancellationToken.None);
            var lastLine = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .LastOrDefault();
            ShowGitResult($"{operationName} done. {lastLine}".Trim(), success: true);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "{Operation} failed", operationName);
            ShowGitResult(ex.Message, success: false);
        }
        finally
        {
            GitBusyRing.IsActive = false;
            await LoadGitAsync(_project);
        }
    }

    private void ShowGitResult(string message, bool success)
    {
        GitOpInfo.Severity = success ? InfoBarSeverity.Success : InfoBarSeverity.Error;
        GitOpInfo.Message = message;
        GitOpInfo.IsOpen = true;
    }

    // ==================== Files tab ====================

    private static readonly HashSet<string> ImageExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".ico" };

    private void PopulateFileTreeRoot()
    {
        if (_project is null)
        {
            return;
        }

        _filesLoaded = true;
        FileTree.ItemsSource = new System.Collections.ObjectModel.ObservableCollection<FileTreeNode>(
            ListEntries(_project.Path));
    }

    /// <summary>Directories first, alphabetical; the .git folder is internal noise.</summary>
    private IEnumerable<FileTreeNode> ListEntries(string directory)
    {
        var dark = ActualTheme == ElementTheme.Dark;
        IEnumerable<FileTreeNode> entries;
        try
        {
            entries =
            [
                .. Directory.EnumerateDirectories(directory)
                    .Where(d => !string.Equals(System.IO.Path.GetFileName(d), ".git", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(d => System.IO.Path.GetFileName(d), StringComparer.CurrentCultureIgnoreCase)
                    .Select(d => new FileTreeNode(System.IO.Path.GetFileName(d)!, d, true, dark)),
                .. Directory.EnumerateFiles(directory)
                    .OrderBy(f => System.IO.Path.GetFileName(f), StringComparer.CurrentCultureIgnoreCase)
                    .Select(f => new FileTreeNode(System.IO.Path.GetFileName(f)!, f, false, dark)),
            ];
        }
        catch (Exception)
        {
            entries = [];
        }

        return entries;
    }

    private void FileTree_Expanding(TreeView sender, TreeViewExpandingEventArgs args)
    {
        if (args.Item is FileTreeNode { IsDirectory: true } folder && !folder.ChildrenLoaded)
        {
            folder.ChildrenLoaded = true;
            args.Node.HasUnrealizedChildren = false;
            foreach (var entry in ListEntries(folder.Path))
            {
                folder.Children.Add(entry);
            }
        }
    }

    private async void FileTree_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
    {
        // Clicking a folder toggles it open/closed, like an IDE's explorer.
        if (args.InvokedItem is FileTreeNode { IsDirectory: true } folder)
        {
            if (sender.ContainerFromItem(folder) is TreeViewItem container)
            {
                container.IsExpanded = !container.IsExpanded;
            }

            return;
        }

        if (args.InvokedItem is not FileTreeNode { IsDirectory: false } file || _project is null)
        {
            return;
        }

        FileHeader.Text = System.IO.Path.GetRelativePath(_project.Path, file.Path);
        FileContentText.Visibility = Visibility.Collapsed;
        FilePreviewImage.Visibility = Visibility.Collapsed;
        FileView.Visibility = Visibility.Collapsed;
        FileFallbackScroll.Visibility = Visibility.Visible;

        try
        {
            if (ImageExtensions.Contains(System.IO.Path.GetExtension(file.Path)))
            {
                FilePreviewImage.Source =
                    new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(file.Path));
                FilePreviewImage.Visibility = Visibility.Visible;
                return;
            }

            var info = new FileInfo(file.Path);
            if (info.Length > 768 * 1024)
            {
                ShowFileMessage($"File is too large to preview ({info.Length / 1024:N0} KB).");
                return;
            }

            var bytes = await File.ReadAllBytesAsync(file.Path);
            if (bytes.Take(8000).Contains((byte)0))
            {
                ShowFileMessage("Binary file — no preview.");
                return;
            }

            await ShowHighlightedCodeAsync(
                System.Text.Encoding.UTF8.GetString(bytes),
                FileVisuals.LanguageFor(file.Name));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "File preview failed for {Path}", file.Path);
            ShowFileMessage($"Could not read the file: {ex.Message}");
        }
    }

    private static string? _highlightJs;
    private static string? _highlightCssDark;
    private static string? _highlightCssLight;

    /// <summary>Renders code through the bundled highlight.js inside the WebView.</summary>
    private async Task ShowHighlightedCodeAsync(string code, string language)
    {
        var assets = Path.Combine(AppContext.BaseDirectory, "Assets", "Highlight");
        _highlightJs ??= await File.ReadAllTextAsync(Path.Combine(assets, "highlight.min.js"));
        _highlightCssDark ??= await File.ReadAllTextAsync(Path.Combine(assets, "github-dark.min.css"));
        _highlightCssLight ??= await File.ReadAllTextAsync(Path.Combine(assets, "github.min.css"));

        var dark = ActualTheme == ElementTheme.Dark;
        var escaped = System.Net.WebUtility.HtmlEncode(code);
        var languageClass = language.Length > 0 ? $"language-{language}" : "";
        var html = $$"""
            <!DOCTYPE html>
            <html><head><meta charset="utf-8">
            <style>{{(dark ? _highlightCssDark : _highlightCssLight)}}</style>
            <style>
            body { margin: 0; background: {{(dark ? "#272727" : "#fbfbfb")}}; }
            pre { margin: 0; }
            code.hljs { background: transparent; padding: 16px 20px;
                        font-family: 'Cascadia Code', Consolas, monospace; font-size: 12.5px; }
            </style></head>
            <body><pre><code class="{{languageClass}}">{{escaped}}</code></pre>
            <script>{{_highlightJs}}</script>
            <script>hljs.highlightAll();</script>
            </body></html>
            """;

        FileView.DefaultBackgroundColor = Microsoft.UI.Colors.Transparent;
        await FileView.EnsureCoreWebView2Async();
        FileView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
        if (!_fileViewScrollHooked)
        {
            _fileViewScrollHooked = true;
            await HookWebViewScrollAsync(FileView);
        }

        FileView.NavigateToString(html);
        FileView.Visibility = Visibility.Visible;

        // The overlaying fallback ScrollViewer would swallow the WebView's wheel input.
        FileFallbackScroll.Visibility = Visibility.Collapsed;
    }

    private void ShowFileMessage(string message)
    {
        FileContentText.Text = message;
        FileContentText.Visibility = Visibility.Visible;
    }

    private void ShowTechDecoration(Project project)
    {
        // Framework logo preferred over language; among those, prefer brands whose
        // color actually shows on the card (near-black logos vanish on dark Mica).
        var candidates = project.Frameworks
            .Select(f => BadgeVisuals.ForFramework(f.Name).Slug)
            .Concat(project.Languages.Select(l => BadgeVisuals.ForLanguage(l.Language).Slug))
            .Where(s => s is not null)
            .ToList();

        var slug = candidates.FirstOrDefault(s => !HasLightVariant(s!)) ?? candidates.FirstOrDefault();
        _langDecorPath = BadgeVisuals.DecorSvgPath(slug);
        _langSvg = null;
        LangDecor.Invalidate();
    }

    /// <summary>A "-light" variant exists exactly for brands that are too dark to see.</summary>
    private static bool HasLightVariant(string slug) =>
        File.Exists(Path.Combine(AppContext.BaseDirectory, "Assets", "ToolIcons", $"{slug}-light.svg"));

    private void LangDecor_Draw(CanvasControl sender, CanvasDrawEventArgs args) =>
        DrawBlurredSvg(sender, args, _langDecorPath, ref _langSvg);

    private void IdeDecor_Draw(CanvasControl sender, CanvasDrawEventArgs args) =>
        DrawBlurredSvg(sender, args, _ideDecorPath, ref _ideSvg);

    /// <summary>Draws the SVG into a command list and runs it through a real gaussian blur.</summary>
    private static void DrawBlurredSvg(
        CanvasControl sender, CanvasDrawEventArgs args, string? svgPath, ref CanvasSvgDocument? cache)
    {
        if (svgPath is null)
        {
            return;
        }

        try
        {
            cache ??= CanvasSvgDocument.LoadFromXml(sender, File.ReadAllText(svgPath));

            // Padding around the glyph so the blur can bleed without hard clipping.
            var side = (float)Math.Min(sender.ActualWidth, sender.ActualHeight);
            var padding = side * 0.15f;
            var glyphSide = side - (2 * padding);

            var commands = new CanvasCommandList(sender);
            using (var session = commands.CreateDrawingSession())
            {
                session.DrawSvg(cache, new Windows.Foundation.Size(glyphSide, glyphSide), padding, padding);
            }

            args.DrawingSession.DrawImage(new GaussianBlurEffect
            {
                Source = commands,
                BlurAmount = side / 28f,
                BorderMode = EffectBorderMode.Soft,
            });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not draw hero decoration {Path}", svgPath);
        }
    }

    private void ShowRemoteButton(string? remoteUrl)
    {
        _remoteWebUrl = ToWebUrl(remoteUrl);
        if (_remoteWebUrl is null)
        {
            return;
        }

        var (label, slug) = _remoteWebUrl.Host.ToLowerInvariant() switch
        {
            var h when h.Contains("github") => ("View on GitHub", "github"),
            var h when h.Contains("gitlab") => ("View on GitLab", "gitlab"),
            var h when h.Contains("bitbucket") => ("View on Bitbucket", "bitbucket"),
            _ => ("View remote", "git"),
        };

        RemoteLabel.Text = label;
        // Dark buttons take the white glyph; light theme reads better with the brand color.
        RemoteIcon.Source = ActualTheme == ElementTheme.Dark
            ? BadgeVisuals.WhiteIcon(slug)
            : BadgeVisuals.ColorIcon(slug);
        RemoteButton.Visibility = Visibility.Visible;
    }

    private void Remote_Click(object sender, RoutedEventArgs e)
    {
        if (_remoteWebUrl is not null)
        {
            Process.Start(new ProcessStartInfo(_remoteWebUrl.ToString()) { UseShellExecute = true });
        }
    }

    /// <summary>"git@github.com:user/repo.git" / "https://github.com/user/repo.git" → browsable https URL.</summary>
    internal static Uri? ToWebUrl(string? remoteUrl)
    {
        if (remoteUrl is null)
        {
            return null;
        }

        var url = remoteUrl.Trim();
        if (url.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            url = url[..^4];
        }

        var at = url.IndexOf('@');
        var colon = url.IndexOf(':', at + 1);
        if (at >= 0 && colon > at && !url.Contains("://"))
        {
            url = $"https://{url[(at + 1)..colon]}/{url[(colon + 1)..]}";
        }

        return Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" ? uri : null;
    }

    private async Task PopulateIdePickerAsync(Project project)
    {
        var ides = await _ides.GetInstalledIdesAsync();
        _initializingIdePicker = true;
        IdePicker.ItemsSource = ides.Select(i => i.DisplayName).ToList();
        IdePicker.Tag = ides;

        if (ides.Count == 0)
        {
            IdePicker.PlaceholderText = "No IDE detected";
            OpenInIdeButton.IsEnabled = false;
            _initializingIdePicker = false;
            return;
        }

        var preferredId = _settings.Settings.ProjectIdePreferences.GetValueOrDefault(project.Path);
        var index = ides.ToList().FindIndex(i => i.Id == preferredId);
        IdePicker.SelectedIndex = index >= 0 ? index : 0;
        _initializingIdePicker = false;

        // First display: no animation, the decoration just appears with the page.
        _ideDecorPath = BadgeVisuals.DecorSvgPath(BadgeVisuals.IdeSlug(ides[IdePicker.SelectedIndex].Id));
        _ideSvg = null;
        IdeDecor.Invalidate();
    }

    private async void IdePicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializingIdePicker || _project is null || SelectedIde() is not { } ide)
        {
            return;
        }

        _settings.Settings.ProjectIdePreferences[_project.Path] = ide.Id;
        await _settings.SaveAsync();

        // Smooth swap: fade the decoration out, repoint it, fade it back in.
        await AnimateOpacityAsync(IdeDecor, 0, TimeSpan.FromMilliseconds(140));
        _ideDecorPath = BadgeVisuals.DecorSvgPath(BadgeVisuals.IdeSlug(ide.Id));
        _ideSvg = null;
        IdeDecor.Invalidate();
        await AnimateOpacityAsync(IdeDecor, 0.20, TimeSpan.FromMilliseconds(280));
    }

    private static Task AnimateOpacityAsync(UIElement element, double to, TimeSpan duration)
    {
        var completion = new TaskCompletionSource();
        var animation = new DoubleAnimation
        {
            To = to,
            Duration = duration,
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut },
        };
        Storyboard.SetTarget(animation, element);
        Storyboard.SetTargetProperty(animation, "Opacity");

        var storyboard = new Storyboard { Children = { animation } };
        storyboard.Completed += (_, _) => completion.TrySetResult();
        storyboard.Begin();
        return completion.Task;
    }

    private IdeInfo? SelectedIde() =>
        IdePicker.Tag is IReadOnlyList<IdeInfo> ides && IdePicker.SelectedIndex >= 0 && IdePicker.SelectedIndex < ides.Count
            ? ides[IdePicker.SelectedIndex]
            : null;

    private async void OpenInIde_Click(object sender, RoutedEventArgs e)
    {
        if (_project is not null && SelectedIde() is { } ide)
        {
            Log.Information("Opening {Path} in {Ide}", _project.Path, ide.Id);
            await _ides.OpenAsync(ide, _project.Path);
        }
    }

    private void OpenInExplorer_Click(object sender, RoutedEventArgs e)
    {
        if (_project is not null)
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_project.Path}\"") { UseShellExecute = true });
        }
    }

    private readonly System.Collections.ObjectModel.ObservableCollection<SubprojectItem> _subprojects = [];

    private async void RefreshSubprojectButtons()
    {
        if (_project is null)
        {
            return;
        }

        SubprojectsList.ItemsSource ??= _subprojects;
        _subprojects.Clear();

        // Settings list order == display order (drag & drop rewrites it).
        var names = _settings.Settings.ProjectSubprojects
            .GetValueOrDefault(_project.Path, [])
            .Where(name => Directory.Exists(Path.Combine(_project.Path, name)))
            .ToList();

        SubprojectsList.Visibility = names.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        var dark = ActualTheme == ElementTheme.Dark;

        foreach (var name in names)
        {
            var subPath = Path.Combine(_project.Path, name);
            var slugs = await Task.Run(() => LanguageDetector.Detect(subPath)
                .Select(l => BadgeVisuals.ForLanguage(l.Language).Slug)
                .Concat(FrameworkDetector.Detect(subPath)
                    .Select(f => BadgeVisuals.ForFramework(f.Name).Slug))
                .Where(s => s is not null)
                .Distinct()
                .Take(3)
                .ToList());

            var icons = slugs.Select(s => BadgeVisuals.TechIcon(s, dark)!).ToList();
            _subprojects.Add(new SubprojectItem(name, icons));
        }
    }

    private async void Subproject_ButtonClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is SubprojectItem item)
        {
            await NavigateToSubprojectAsync(item.Name);
        }
    }

    private async void ManageSubprojects_Click(object sender, RoutedEventArgs e)
    {
        if (_project is null)
        {
            return;
        }

        var current = _settings.Settings.ProjectSubprojects.GetValueOrDefault(_project.Path, []);
        var checkboxes = Directory.EnumerateDirectories(_project.Path)
            .Select(Path.GetFileName)
            .Where(name => name is not null && !name.StartsWith('.'))
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .Select(name => new CheckBox
            {
                Content = name,
                IsChecked = current.Contains(name!, StringComparer.OrdinalIgnoreCase),
            })
            .ToList();

        if (checkboxes.Count == 0)
        {
            await new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "No subfolders",
                Content = "This project has no subfolders that could become subprojects.",
                CloseButtonText = "OK",
            }.ShowAsync();
            return;
        }

        var panel = new StackPanel { Spacing = 4 };
        foreach (var checkbox in checkboxes)
        {
            panel.Children.Add(checkbox);
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Subprojects",
            Content = new ScrollViewer { Content = panel, MaxHeight = 420, MinWidth = 360 },
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        var selectedSet = checkboxes
            .Where(c => c.IsChecked == true)
            .Select(c => (string)c.Content)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Keep the user's custom order for existing entries; append newly checked ones.
        var selected = current.Where(selectedSet.Contains)
            .Concat(selectedSet.Except(current, StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase))
            .ToList();

        if (selected.Count > 0)
        {
            _settings.Settings.ProjectSubprojects[_project.Path] = selected;
        }
        else
        {
            _settings.Settings.ProjectSubprojects.Remove(_project.Path);
        }

        await _settings.SaveAsync();
        RefreshSubprojectButtons();
    }

    private async Task NavigateToSubprojectAsync(string name)
    {
        if (_project is null)
        {
            return;
        }

        var subPath = Path.Combine(_project.Path, name);
        if (!Directory.Exists(subPath))
        {
            return;
        }

        // Detect the subfolder's own stack on demand — the scanner only inspects project roots.
        var (languages, frameworks) = await Task.Run(
            () => (LanguageDetector.Detect(subPath), FrameworkDetector.Detect(subPath)));

        var subproject = new Project
        {
            Name = name,
            Path = subPath,
            // Same repository as the parent.
            GitRemoteUrl = _project.GitRemoteUrl,
            IsGitRepository = _project.IsGitRepository,
            LastScannedAt = DateTime.UtcNow,
            Languages = languages,
            Frameworks = frameworks,
        };

        var relative = Path.Combine(_project.Name, name);
        Frame.Navigate(typeof(ProjectDetailPage), new ProjectDetailArgs(subproject, relative));
    }

    private async void Rename_Click(object sender, RoutedEventArgs e)
    {
        if (_project is null)
        {
            return;
        }

        var nameBox = new TextBox { Text = _project.Name, MinWidth = 360 };
        nameBox.SelectAll();

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Rename project",
            Content = nameBox,
            PrimaryButtonText = "Rename",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary
            || nameBox.Text.Trim() is not { Length: > 0 } newName
            || newName == _project.Name)
        {
            return;
        }

        try
        {
            var fileOperations = App.Services.GetRequiredService<IFileOperations>();
            var newPath = await fileOperations.RenameFolderAsync(_project.Path, newName);
            Log.Information("Renamed {OldPath} to {NewPath}", _project.Path, newPath);

            // Cascade: per-project settings are keyed by path.
            var settingsChanged = false;
            if (_settings.Settings.ProjectIdePreferences.Remove(_project.Path, out var ideId))
            {
                _settings.Settings.ProjectIdePreferences[newPath] = ideId;
                settingsChanged = true;
            }

            if (_settings.Settings.ProjectSubprojects.Remove(_project.Path, out var subprojects))
            {
                _settings.Settings.ProjectSubprojects[newPath] = subprojects;
                settingsChanged = true;
            }

            if (settingsChanged)
            {
                await _settings.SaveAsync();
            }

            _project = _project with { Name = newName, Path = newPath };
            ProjectName.Text = newName;
            ProjectPath.Text = newPath;

            // The list keeps its own snapshot; refresh it in the background.
            _ = App.Services.GetRequiredService<ProjectsViewModel>().RescanAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Rename of {Path} failed", _project.Path);
            await new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "Could not rename the project",
                Content = ex.Message,
                CloseButtonText = "OK",
            }.ShowAsync();
        }
    }

    private void HeroCard_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // The decorative logos intentionally overflow; crop them at the card bounds.
        HeroCard.Clip = new Microsoft.UI.Xaml.Media.RectangleGeometry
        {
            Rect = new Windows.Foundation.Rect(0, 0, e.NewSize.Width, e.NewSize.Height),
        };
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (Frame.CanGoBack)
        {
            Frame.GoBack();
        }
    }

    private async Task LoadReadmeAsync(string projectPath)
    {
        var readmePath = Directory.EnumerateFiles(projectPath, "*.md")
            .FirstOrDefault(f => Path.GetFileNameWithoutExtension(f).Equals("README", StringComparison.OrdinalIgnoreCase));
        if (readmePath is null)
        {
            return;
        }

        try
        {
            var markdown = await File.ReadAllTextAsync(readmePath);
            var html = WrapHtml(Markdown.ToHtml(markdown, new MarkdownPipelineBuilder().UseAdvancedExtensions().Build()));

            // Transparent default background: no white flash while the control spins up or tears down.
            ReadmeView.DefaultBackgroundColor = Microsoft.UI.Colors.Transparent;
            await ReadmeView.EnsureCoreWebView2Async();
            ReadmeView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;

            // README links belong in the user's browser, not inside the app.
            ReadmeView.CoreWebView2.NavigationStarting += (_, navArgs) =>
            {
                if (navArgs.Uri?.StartsWith("http", StringComparison.OrdinalIgnoreCase) == true)
                {
                    navArgs.Cancel = true;
                    Process.Start(new ProcessStartInfo(navArgs.Uri) { UseShellExecute = true });
                }
            };
            ReadmeView.CoreWebView2.NewWindowRequested += (_, windowArgs) =>
            {
                windowArgs.Handled = true;
                if (windowArgs.Uri?.StartsWith("http", StringComparison.OrdinalIgnoreCase) == true)
                {
                    Process.Start(new ProcessStartInfo(windowArgs.Uri) { UseShellExecute = true });
                }
            };

            if (!_readmeScrollHooked)
            {
                _readmeScrollHooked = true;
                await HookWebViewScrollAsync(ReadmeView);
            }

            ReadmeView.NavigateToString(html);
            ReadmeView.Visibility = Visibility.Visible;
            NoReadmeText.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to render README {Path}", readmePath);
            NoReadmeText.Text = "The README could not be displayed.";
        }
    }

    private string WrapHtml(string body)
    {
        var dark = ActualTheme == ElementTheme.Dark;
        var background = dark ? "#272727" : "#fbfbfb";
        var text = dark ? "#e8e8e8" : "#1b1b1b";
        var muted = dark ? "#9d9d9d" : "#616161";
        var codeBg = dark ? "#1f1f1f" : "#f0f0f0";
        var border = dark ? "#3f3f3f" : "#e0e0e0";
        var link = dark ? "#4cc2ff" : "#005fb8";

        return $$"""
            <!DOCTYPE html>
            <html><head><meta charset="utf-8"><style>
            body { font-family: 'Segoe UI', sans-serif; background: {{background}}; color: {{text}};
                   margin: 0; padding: 24px 28px; line-height: 1.55; font-size: 14px; }
            h1, h2 { border-bottom: 1px solid {{border}}; padding-bottom: 6px; }
            a { color: {{link}}; text-decoration: none; } a:hover { text-decoration: underline; }
            code { background: {{codeBg}}; padding: 2px 5px; border-radius: 4px;
                   font-family: 'Cascadia Code', Consolas, monospace; font-size: 12.5px; }
            pre { background: {{codeBg}}; padding: 12px 14px; border-radius: 8px; overflow-x: auto; }
            pre code { background: none; padding: 0; }
            blockquote { border-left: 3px solid {{border}}; margin-left: 0; padding-left: 14px; color: {{muted}}; }
            table { border-collapse: collapse; } th, td { border: 1px solid {{border}}; padding: 6px 10px; }
            img { max-width: 100%; }
            </style></head><body>{{body}}</body></html>
            """;
    }
}
