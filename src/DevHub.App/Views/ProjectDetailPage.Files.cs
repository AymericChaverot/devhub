using DevHub.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Serilog;

namespace DevHub.App.Views;

public sealed partial class ProjectDetailPage
{
    private static readonly HashSet<string> ImageExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".ico" };
    private static string? _highlightJs;
    private static string? _highlightCssDark;
    private static string? _highlightCssLight;

    private bool _filesLoaded;
    private bool _fileViewScrollHooked;

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
                ShowFileMessage(Loc.F("Detail_FileTooLarge", "File is too large to preview ({0:N0} KB).", info.Length / 1024));
                return;
            }

            var bytes = await File.ReadAllBytesAsync(file.Path);
            if (bytes.Take(8000).Contains((byte)0))
            {
                ShowFileMessage(Loc.T("Detail_BinaryFile", "Binary file — no preview."));
                return;
            }

            await ShowHighlightedCodeAsync(
                System.Text.Encoding.UTF8.GetString(bytes),
                FileVisuals.LanguageFor(file.Name));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "File preview failed for {Path}", file.Path);
            ShowFileMessage(Loc.F("Detail_FileReadError", "Could not read the file: {0}", ex.Message));
        }
    }


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
}
